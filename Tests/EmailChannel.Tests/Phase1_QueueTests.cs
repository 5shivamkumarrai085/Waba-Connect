using Microsoft.Extensions.DependencyInjection;
using WhatsAppCampaignApi.Services.Queue;

namespace EmailChannel.Tests;

/// <summary>
/// Phase 1 — the durable queue.
///
/// <para>
/// The most important tests in the suite. Everything downstream assumes the queue never hands one
/// job to two workers, never loses work when an instance dies, and never retries something that
/// can only fail again. Those are all concurrency properties, so they are exercised against the
/// real PostgreSQL claim query with genuinely concurrent callers — a mock cannot fail in the ways
/// that matter here.
/// </para>
/// </summary>
public static class Phase1_QueueTests
{
    public static async Task RunAsync(TestHarness harness, TestRun run)
    {
        run.BeginPhase("Phase 1 — durable job queue (Postgres, SKIP LOCKED)");

        using var scope = harness.CreateScope();
        var queue = scope.ServiceProvider.GetRequiredService<IJobQueue>();

        // Its own queue name, so nothing here can touch or be disturbed by real work.
        var queueName = $"zz-{harness.Tag}";

        QueueMessage Message(
            string body,
            string? idempotencyKey = null,
            string? partitionKey = null,
            int priority = 0,
            int? maxAttempts = null) => new()
            {
                QueueName = queueName,
                Payload = $"{{\"body\":\"{body}\"}}",
                IdempotencyKey = idempotencyKey,
                PartitionKey = partitionKey,
                Priority = priority,
                MaxAttempts = maxAttempts
            };

        async Task ResetAsync() =>
            await harness.ExecAsync("""DELETE FROM "JobQueue" WHERE "QueueName" = @q""", ("q", queueName));

        async Task<QueueLease?> ClaimOneAsync(string worker, TimeSpan? visibility = null)
        {
            var leases = await queue.ClaimAsync(new QueueClaimRequest
            {
                QueueName = queueName,
                WorkerId = worker,
                BatchSize = 1,
                VisibilityTimeout = visibility
            });

            if (leases.Count > 0) return leases[0];

            run.Check($"[{worker}] expected to claim a job", false, "claim returned nothing");
            return null;
        }

        try
        {
            run.Section("Enqueue and de-duplication");

            var first = await queue.EnqueueAsync([Message("a", "k-a"), Message("b", "k-b"), Message("c")]);
            run.Check("three new messages enqueued", first is { Enqueued: 3, Duplicates: 0 }, first.ToString());

            var second = await queue.EnqueueAsync([Message("a", "k-a"), Message("d", "k-d")]);
            run.Check("a repeated idempotency key is skipped, a new one accepted",
                second is { Enqueued: 1, Duplicates: 1 }, second.ToString());

            var third = await queue.EnqueueAsync([Message("c"), Message("c")]);
            run.Check("messages without an idempotency key are never de-duplicated",
                third is { Enqueued: 2, Duplicates: 0 }, third.ToString());

            run.Section("Concurrent claim — the core safety property");
            await ResetAsync();

            await queue.EnqueueAsync(Enumerable.Range(0, 120)
                .Select(i => Message($"j{i}", $"bulk-{i}", partitionKey: $"p{i % 4}"))
                .ToList());

            // Eight workers claiming at once, repeatedly. A read-then-write race would show up
            // here as the same job id appearing in two workers' results.
            var claims = await Task.WhenAll(Enumerable.Range(0, 8).Select(async worker =>
            {
                var mine = new List<long>();

                for (var round = 0; round < 6; round++)
                {
                    var leases = await queue.ClaimAsync(new QueueClaimRequest
                    {
                        QueueName = queueName,
                        WorkerId = $"worker-{worker}",
                        BatchSize = 5
                    });

                    mine.AddRange(leases.Select(l => l.JobId));
                    foreach (var lease in leases) await queue.CompleteAsync(lease);
                }

                return mine;
            }));

            var allClaimed = claims.SelectMany(c => c).ToList();
            run.Check("no job was ever claimed twice",
                allClaimed.Count == allClaimed.Distinct().Count(),
                $"{allClaimed.Count} claims, {allClaimed.Distinct().Count()} distinct");

            run.Check("all 120 jobs were drained", allClaimed.Distinct().Count() == 120,
                $"{allClaimed.Distinct().Count()}");

            var completed = await harness.CountAsync(
                """SELECT count(*) FROM "JobQueue" WHERE "QueueName" = @q AND "Status" = 'Completed'""",
                ("q", queueName));
            run.Check("all 120 rows are marked Completed", completed == 120, $"{completed}");

            run.Section("Concurrent claim under maximum contention (regression)");
            await ResetAsync();

            // Far more workers than jobs, batches of one, and nothing between claim and complete.
            // That shape is what exposes the snapshot-versus-row-lock race: every worker sees the
            // same rows as claimable in the same instant, and only the post-lock re-check can
            // separate them.
            //
            // The bug this guards against let one job be handed to two workers, because the
            // claimability predicate lived one query level above the FOR UPDATE and so was never
            // re-evaluated after the lock was taken.
            const int contendedJobs = 60;
            await queue.EnqueueAsync(Enumerable.Range(0, contendedJobs)
                .Select(i => Message($"race{i}", $"race-{i}", partitionKey: $"rp{i % 3}"))
                .ToList());

            var raceClaims = await Task.WhenAll(Enumerable.Range(0, 20).Select(async worker =>
            {
                var mine = new List<long>();

                // Enough rounds that 20 workers collectively attempt well past the job count,
                // so the queue is drained under contention rather than comfortably.
                for (var round = 0; round < 12; round++)
                {
                    var leases = await queue.ClaimAsync(new QueueClaimRequest
                    {
                        QueueName = queueName,
                        WorkerId = $"race-worker-{worker}",
                        BatchSize = 1,
                        PerPartitionCap = 1,

                        // Long enough that nothing expires mid-test: a re-claim here must mean a
                        // genuine double-hand-out, not a legitimate lease recovery.
                        VisibilityTimeout = TimeSpan.FromMinutes(10)
                    });

                    foreach (var lease in leases)
                    {
                        mine.Add(lease.JobId);
                        await queue.CompleteAsync(lease);
                    }
                }

                return mine;
            }));

            var raceAll = raceClaims.SelectMany(c => c).ToList();
            var raceDistinct = raceAll.Distinct().Count();

            run.Check("no job was handed to two workers under heavy contention",
                raceAll.Count == raceDistinct,
                $"{raceAll.Count} claims but only {raceDistinct} distinct jobs");

            run.Check("every contended job was claimed exactly once",
                raceDistinct == contendedJobs, $"{raceDistinct} of {contendedJobs}");

            var raceCompleted = await harness.CountAsync(
                """SELECT count(*) FROM "JobQueue" WHERE "QueueName" = @q AND "Status" = 'Completed'""",
                ("q", queueName));
            run.Check("every contended job ended Completed", raceCompleted == contendedJobs,
                $"{raceCompleted}");

            run.Section("Per-partition fairness");
            await ResetAsync();

            // A large campaign enqueued first, a small one behind it. An earlier implementation
            // used a single global scan window, which looked fair but wasn't: the big campaign
            // filled the window and the small one waited for all 200.
            await queue.EnqueueAsync(Enumerable.Range(0, 200)
                .Select(i => Message($"big{i}", $"big-{i}", partitionKey: "campaign-big")).ToList());
            await queue.EnqueueAsync(Enumerable.Range(0, 5)
                .Select(i => Message($"small{i}", $"small-{i}", partitionKey: "campaign-small")).ToList());

            var fair = await queue.ClaimAsync(new QueueClaimRequest
            {
                QueueName = queueName,
                WorkerId = "fairness",
                BatchSize = 10,
                PerPartitionCap = 5
            });

            var bigShare = fair.Count(l => l.PartitionKey == "campaign-big");
            var smallShare = fair.Count(l => l.PartitionKey == "campaign-small");

            run.Check("the large campaign is capped at the per-partition limit", bigShare <= 5, $"{bigShare}");
            run.Check("the small campaign still got work despite queueing later",
                smallShare > 0, $"{smallShare}");

            foreach (var lease in fair) await queue.CompleteAsync(lease);

            run.Section("Retry with backoff, then dead-letter");
            await ResetAsync();
            await queue.EnqueueAsync([Message("flaky", "flaky-1", maxAttempts: 3)]);

            var attempts = new List<int>();
            string? statusAfterAttempts = null;

            for (var i = 0; i < 4; i++)
            {
                var leases = await queue.ClaimAsync(new QueueClaimRequest
                {
                    QueueName = queueName, WorkerId = "flaky-worker", BatchSize = 5
                });

                if (leases.Count == 0) break;

                attempts.Add(leases[0].Attempt);
                await queue.FailAsync(leases[0], new QueueFailure("simulated transient failure", IsTransient: true));

                // The backoff is asserted separately below; clearing it here keeps this test from
                // having to wait it out.
                await harness.ExecAsync(
                    """UPDATE "JobQueue" SET "AvailableAt" = now() WHERE "QueueName" = @q""", ("q", queueName));

                statusAfterAttempts = await harness.ScalarAsync(
                    """SELECT "Status" FROM "JobQueue" WHERE "QueueName" = @q LIMIT 1""", ("q", queueName));
            }

            run.Check("the attempt counter increments on each delivery",
                attempts.SequenceEqual([1, 2, 3]), string.Join(",", attempts));
            run.Check("the job dead-letters once MaxAttempts is reached",
                statusAfterAttempts == "DeadLettered", statusAfterAttempts);

            var deadLetteredAt = await harness.CountAsync("""
                SELECT count(*) FROM "JobQueue" WHERE "QueueName" = @q AND "DeadLetteredAt" IS NOT NULL
                """, ("q", queueName));
            run.Check("DeadLetteredAt is stamped", deadLetteredAt == 1, $"{deadLetteredAt}");

            var lastError = await harness.ScalarAsync(
                """SELECT "LastError" FROM "JobQueue" WHERE "QueueName" = @q LIMIT 1""", ("q", queueName));
            run.Check("the failure reason is recorded",
                lastError is not null && lastError.Contains("simulated transient"), lastError);

            run.Section("A permanent failure skips retries entirely");
            await ResetAsync();
            await queue.EnqueueAsync([Message("permanent", "perm-1", maxAttempts: 5)]);

            var permanent = await ClaimOneAsync("permanent");
            if (permanent is not null)
            {
                await queue.FailAsync(permanent,
                    new QueueFailure("rejected sender address", IsTransient: false));
            }

            var permanentStatus = await harness.ScalarAsync(
                """SELECT "Status" FROM "JobQueue" WHERE "QueueName" = @q LIMIT 1""", ("q", queueName));
            run.Check("dead-lettered on attempt 1 of 5, rather than retried four more times",
                permanentStatus == "DeadLettered", permanentStatus);

            run.Section("Backoff actually delays the retry");
            await ResetAsync();
            await queue.EnqueueAsync([Message("backoff", "backoff-1", maxAttempts: 5)]);

            var backoffLease = await ClaimOneAsync("backoff");
            if (backoffLease is not null)
            {
                await queue.FailAsync(backoffLease, new QueueFailure("transient", IsTransient: true));
            }

            var secondsAhead = double.TryParse(await harness.ScalarAsync("""
                SELECT EXTRACT(EPOCH FROM ("AvailableAt" - now()))
                  FROM "JobQueue" WHERE "QueueName" = @q LIMIT 1
                """, ("q", queueName)), out var ahead) ? ahead : 0;

            run.Check("the retry is scheduled into the future", secondsAhead > 1, $"{secondsAhead:0.0}s");

            var premature = await queue.ClaimAsync(new QueueClaimRequest
            {
                QueueName = queueName, WorkerId = "backoff", BatchSize = 1
            });
            run.Check("the job is not claimable before its backoff elapses",
                premature.Count == 0, $"{premature.Count} claimed");

            run.Section("A crashed worker's job is recovered");
            await ResetAsync();
            await queue.EnqueueAsync([Message("orphan", "orphan-1")]);

            var orphanLease = await ClaimOneAsync("doomed-worker", TimeSpan.FromSeconds(120));

            var whileLeased = await queue.ClaimAsync(new QueueClaimRequest
            {
                QueueName = queueName, WorkerId = "other", BatchSize = 1
            });
            run.Check("a live lease is invisible to other workers",
                whileLeased.Count == 0, $"{whileLeased.Count} claimed");

            // Simulating the instance dying: the lease simply lapses. No reaper process is
            // involved — the claim query treats an expired lease as available.
            await harness.ExecAsync("""
                UPDATE "JobQueue" SET "LeaseExpiresAt" = now() - interval '1 second' WHERE "QueueName" = @q
                """, ("q", queueName));

            var reclaimed = await queue.ClaimAsync(new QueueClaimRequest
            {
                QueueName = queueName, WorkerId = "rescuer", BatchSize = 1
            });
            run.Check("an expired lease is reclaimed by another worker", reclaimed.Count == 1, $"{reclaimed.Count}");
            run.Check("reclaiming increments the attempt counter",
                reclaimed.Count == 1 && reclaimed[0].Attempt == 2,
                reclaimed.Count == 1 ? $"attempt {reclaimed[0].Attempt}" : "no lease");

            run.Section("A stale lease cannot corrupt a re-claimed job");

            var staleComplete = orphanLease is not null && await queue.CompleteAsync(orphanLease);
            run.Check("the dead worker cannot mark the job complete", !staleComplete);

            var stillLeased = await harness.ScalarAsync(
                """SELECT "Status" FROM "JobQueue" WHERE "QueueName" = @q LIMIT 1""", ("q", queueName));
            run.Check("the job stays owned by its new holder", stillLeased == "Leased", stillLeased);

            var staleExtend = orphanLease is not null
                           && await queue.ExtendLeaseAsync(orphanLease, TimeSpan.FromSeconds(60));
            run.Check("the dead worker cannot extend the lease either", !staleExtend);

            run.Section("Lease heartbeat");

            var before = double.TryParse(await harness.ScalarAsync("""
                SELECT EXTRACT(EPOCH FROM ("LeaseExpiresAt" - now()))
                  FROM "JobQueue" WHERE "QueueName" = @q LIMIT 1
                """, ("q", queueName)), out var beforeSeconds) ? beforeSeconds : 0;

            var extended = reclaimed.Count == 1
                        && await queue.ExtendLeaseAsync(reclaimed[0], TimeSpan.FromSeconds(600));

            var after = double.TryParse(await harness.ScalarAsync("""
                SELECT EXTRACT(EPOCH FROM ("LeaseExpiresAt" - now()))
                  FROM "JobQueue" WHERE "QueueName" = @q LIMIT 1
                """, ("q", queueName)), out var afterSeconds) ? afterSeconds : 0;

            run.Check("the current holder can extend its own lease", extended);
            run.Check("the expiry moves further out", after > before + 100, $"{before:0}s -> {after:0}s");

            if (reclaimed.Count == 1) await queue.CompleteAsync(reclaimed[0]);

            run.Section("Depth reporting and dead-letter requeue");
            await ResetAsync();
            await queue.EnqueueAsync(Enumerable.Range(0, 7)
                .Select(i => Message($"d{i}", $"depth-{i}")).ToList());

            var forDepth = await queue.ClaimAsync(new QueueClaimRequest
            {
                QueueName = queueName, WorkerId = "depth", BatchSize = 2
            });
            await queue.FailAsync(forDepth[0], new QueueFailure("permanent", IsTransient: false));

            var depth = await queue.GetDepthAsync(queueName);
            run.Check("pending counted", depth.Pending == 5, $"{depth.Pending}");
            run.Check("leased counted", depth.Leased == 1, $"{depth.Leased}");
            run.Check("dead-lettered counted", depth.DeadLettered == 1, $"{depth.DeadLettered}");

            // Depth alone cannot distinguish a healthy burst from a stalled queue, which is why
            // the age of the oldest pending job is reported too.
            run.Check("the oldest pending timestamp is reported", depth.OldestPendingAt is not null);

            var requeued = await queue.RequeueDeadLetteredAsync(queueName, 10);
            run.Check("a dead-lettered job can be requeued", requeued == 1, $"{requeued}");

            var afterRequeue = await queue.GetDepthAsync(queueName);
            run.Check("the dead-letter count clears", afterRequeue.DeadLettered == 0, $"{afterRequeue.DeadLettered}");

            var resetAttempt = await harness.ScalarAsync("""
                SELECT "Attempt" FROM "JobQueue"
                 WHERE "QueueName" = @q AND "Status" = 'Pending' AND "IdempotencyKey" = 'depth-0'
                """, ("q", queueName));
            run.Check("a requeued job gets its full retry budget back", resetAttempt == "0", resetAttempt);

            run.Section("Scheduling is the same mechanism as backoff");
            await ResetAsync();

            await queue.EnqueueAsync([new QueueMessage
            {
                QueueName = queueName,
                Payload = "{}",
                IdempotencyKey = "future-1",
                AvailableAt = DateTime.UtcNow.AddHours(1)
            }]);

            var futureClaim = await queue.ClaimAsync(new QueueClaimRequest
            {
                QueueName = queueName, WorkerId = "scheduler", BatchSize = 5
            });
            run.Check("a future-dated job is not claimable yet", futureClaim.Count == 0, $"{futureClaim.Count}");

            var futureDepth = await queue.GetDepthAsync(queueName);
            run.Check("a scheduled job counts as pending but is not the oldest ready one",
                futureDepth is { Pending: 1, OldestPendingAt: null },
                $"pending {futureDepth.Pending}, oldest {futureDepth.OldestPendingAt?.ToString("O") ?? "none"}");
        }
        catch (Exception ex)
        {
            run.Error("queue phase threw", ex);
        }
        finally
        {
            await harness.ExecAsync("""DELETE FROM "JobQueue" WHERE "QueueName" = @q""", ("q", queueName));
        }
    }
}
