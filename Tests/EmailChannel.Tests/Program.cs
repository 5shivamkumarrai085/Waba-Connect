using EmailChannel.Tests;

// Integration suite for the email channel.
//
// Every phase runs every time, and the whole suite is re-run after each change — a phase that is
// only run once, when it was written, stops being a regression test.
//
//   dotnet run --project Tests/EmailChannel.Tests
//   dotnet run --project Tests/EmailChannel.Tests -- 1 5     (phases 1 and 5 only)
//
// Exits non-zero if anything failed, so this drops into CI unchanged.

var backendDirectory = ResolveBackendDirectory();
if (backendDirectory is null)
{
    Console.Error.WriteLine(
        "Could not locate the Backend directory (expected a sibling of Tests/ containing appsettings.json).");
    return 2;
}

// No argument means every phase. Naming phases runs just those, which is what makes iterating on
// one of them bearable — the full suite takes over a minute, most of it waiting on workers.
var requested = args
    .Where(a => int.TryParse(a, out _))
    .Select(int.Parse)
    .ToHashSet();

bool ShouldRun(int phase) => requested.Count == 0 || requested.Contains(phase);

Console.WriteLine("".PadRight(78, '═'));
Console.WriteLine("  Email channel — integration suite");
Console.WriteLine($"  Backend: {backendDirectory}");
if (requested.Count > 0) Console.WriteLine($"  Phases:  {string.Join(", ", requested.Order())}");
Console.WriteLine("".PadRight(78, '═'));

var run = new TestRun();
await using var harness = TestHarness.Create(backendDirectory);

try
{
    // Left over from a previous crashed run. Cleaning up first rather than only afterwards means
    // a run that died halfway cannot make the next one fail during setup.
    await harness.CleanupAsync();

    if (ShouldRun(0)) await Phase0_SchemaTests.RunAsync(harness, run);
    if (ShouldRun(1)) await Phase1_QueueTests.RunAsync(harness, run);
    if (ShouldRun(2)) await Phase2_ConnectionTests.RunAsync(harness, run);
    if (ShouldRun(3)) await Phase3_DomainTests.RunAsync(harness, run);
    if (ShouldRun(4)) await Phase4_TemplateTests.RunAsync(harness, run);
    if (ShouldRun(5)) await Phase5_PipelineTests.RunAsync(harness, run);
    if (ShouldRun(6)) await Phase6_EventTests.RunAsync(harness, run);
    if (ShouldRun(7)) await Phase7_BulkCsvTests.RunAsync(harness, run);
    if (ShouldRun(8)) await Phase8_HardeningTests.RunAsync(harness, run);
    if (ShouldRun(9)) await Phase9_ConnectionScopingTests.RunAsync(harness, run);
    if (ShouldRun(10)) await Phase10_ApprovalTests.RunAsync(harness, run);
    if (ShouldRun(11)) await Phase11_RetryTests.RunAsync(harness, run);
    if (ShouldRun(12)) await Phase12_ComplianceTests.RunAsync(harness, run);
    if (ShouldRun(13)) await Phase13_SegmentsAndLinksTests.RunAsync(harness, run);
    if (ShouldRun(14)) await Phase14_DeliverabilityTests.RunAsync(harness, run);
    if (ShouldRun(15)) await Phase15_AbTestTests.RunAsync(harness, run);
    if (ShouldRun(16)) await Phase16_FollowUpTests.RunAsync(harness, run);
    if (ShouldRun(17)) await Phase17_ChatOperationsTests.RunAsync(harness, run);
    if (ShouldRun(18)) await Phase18_InteractiveTemplateTests.RunAsync(harness, run);
    if (ShouldRun(19)) await Phase19_WebhookAndScheduleTests.RunAsync(harness, run);
    if (ShouldRun(20)) await Phase20_EncryptionKeyGuardTests.RunAsync(harness, run);
    if (ShouldRun(21)) await Phase21_CatalogTests.RunAsync(harness, run);
    if (ShouldRun(22)) await Phase22_Round4Tests.RunAsync(harness, run);
}
finally
{
    // Always, including after a failure. A suite that leaves rows behind on failure poisons the
    // next run and sends whoever is debugging down the wrong path.
    try
    {
        await harness.CleanupAsync();
        Console.WriteLine();
        Console.WriteLine("  Cleanup complete.");
    }
    catch (Exception ex)
    {
        Console.WriteLine();
        Console.WriteLine($"  WARNING: cleanup failed — {ex.Message}");
        Console.WriteLine($"  Remove rows named like '{TestHarness.Prefix}%' by hand before the next run.");
    }
}

return run.Report();

/// <summary>
/// Walks up from the executing assembly to find the Backend directory.
///
/// <para>
/// Resolved at runtime rather than hardcoded so the suite works from any checkout path and from
/// any working directory — including CI, where nobody's home folder is in the path.
/// </para>
/// </summary>
static string? ResolveBackendDirectory()
{
    var directory = new DirectoryInfo(AppContext.BaseDirectory);

    while (directory is not null)
    {
        var candidate = Path.Combine(directory.FullName, "Backend");
        if (File.Exists(Path.Combine(candidate, "appsettings.json"))) return candidate;
        directory = directory.Parent;
    }

    return null;
}
