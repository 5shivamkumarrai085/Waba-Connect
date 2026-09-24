using System.Text;
using WhatsAppCampaignApi.Helpers;
using WhatsAppCampaignApi.Models.DTOs.Common;
using WhatsAppCampaignApi.Models.Enums;

namespace EmailChannel.Tests;

/// <summary>
/// Phase 7 — the bulk-CSV path, per channel.
///
/// <para>
/// The bulk importer judges a file twice: once for the preview the operator confirms, and once
/// when the campaign is actually built. Those two readings have to agree, which is why both go
/// through <see cref="BulkCampaignCsv"/> — and why the channel has to reach both of them. A file
/// accepted at preview and rejected at create (or, worse, accepted at both but sent to nobody)
/// is the failure these assertions are here to prevent.
/// </para>
/// <para>
/// The rule being pinned down: phone and name are required on every channel, because a Contact
/// is identified by its phone number. An email campaign additionally requires a usable email
/// address in every row, because that is what the message is addressed to.
/// </para>
/// </summary>
public static class Phase7_BulkCsvTests
{
    public static async Task RunAsync(TestHarness harness, TestRun run)
    {
        run.BeginPhase("Phase 7 — bulk CSV, per channel");

        try
        {
            // ── Header mapping ──────────────────────────────────────────────────────────────────
            run.Section("Which files each channel accepts");

            var withEmail = await HeaderOf("phone,firstname,lastname,email,country");
            var withoutEmail = await HeaderOf("phone,firstname,lastname,country");
            var withoutPhone = await HeaderOf("firstname,email");

            run.Check("WhatsApp accepts a file with no email column",
                BulkCampaignCsv.MapColumns(withoutEmail, MessageChannel.WhatsApp) is not null);

            run.Check("WhatsApp still accepts a file that happens to have one",
                BulkCampaignCsv.MapColumns(withEmail, MessageChannel.WhatsApp) is not null);

            // The whole point of the channel argument: without it, an email campaign would accept
            // a file it cannot address a single message from.
            run.Check("Email rejects a file with no email column",
                BulkCampaignCsv.MapColumns(withoutEmail, MessageChannel.Email) is null);

            run.Check("Email accepts a file that has one",
                BulkCampaignCsv.MapColumns(withEmail, MessageChannel.Email) is not null);

            // Contact identity is the phone number on every channel — it is not an email-channel
            // relaxation, and a file without it cannot create contacts whatever is being sent.
            run.Check("Email still rejects a file with no phone column",
                BulkCampaignCsv.MapColumns(withoutPhone, MessageChannel.Email) is null);

            run.Check("WhatsApp rejects a file with no phone column",
                BulkCampaignCsv.MapColumns(withoutPhone, MessageChannel.WhatsApp) is null);

            // Absent means WhatsApp, so every caller written before the email channel existed
            // still maps its files exactly as it did.
            run.Check("the default channel behaves as WhatsApp",
                BulkCampaignCsv.MapColumns(withoutEmail) is not null);

            // ── Row validation ──────────────────────────────────────────────────────────────────
            run.Section("Which rows each channel accepts");

            var map = BulkCampaignCsv.MapColumns(withEmail, MessageChannel.Email)!;

            bool Read(string[] fields, MessageChannel channel, out List<CsvRowError> errors) =>
                BulkCampaignCsv.TryReadRow(fields, 2, map, out _, out errors, channel);

            string[] Row(string email) => ["919143000000", "Shivam", "Rai", email, "India"];

            run.Check("a row with a good address is accepted on Email",
                Read(Row("shivam@example.com"), MessageChannel.Email, out _));

            run.Check("a row with an empty address is rejected on Email",
                !Read(Row(""), MessageChannel.Email, out _));

            // The same row is fine on WhatsApp — the address is genuinely optional there, and
            // tightening it would break the existing bulk path for no benefit.
            run.Check("the same row is accepted on WhatsApp",
                Read(Row(""), MessageChannel.WhatsApp, out _));

            run.Check("the rejection names the email column",
                !Read(Row(""), MessageChannel.Email, out var emptyErrors)
                    && emptyErrors.Any(e => e.Column == "email"),
                string.Join(" | ", (Read(Row(""), MessageChannel.Email, out var e2) ? [] : e2)
                    .Select(x => $"{x.Column}: {x.Reason}")));

            // Each of these is a shape a spreadsheet actually produces — a name left in the wrong
            // column, a half-typed domain, a trailing separator.
            foreach (var bad in new[] { "not-an-address", "shivam@", "@example.com", "shivam@example", "a b@example.com", "two@@example.com", "shivam@example." })
            {
                run.Check($"\"{bad}\" is rejected on Email",
                    !Read(Row(bad), MessageChannel.Email, out _));
            }

            // Deliberately permissive: the receiving mail server is the only authority on whether
            // an address exists, so a stricter local rule can only reject deliverable mail.
            foreach (var good in new[] { "shivam+campaign@example.co.uk", "first.last@sub.example.com", "x@y.io" })
            {
                run.Check($"\"{good}\" is accepted on Email",
                    Read(Row(good), MessageChannel.Email, out _));
            }

            // ── The preview and the create path agree ───────────────────────────────────────────
            run.Section("The preview and the create path read a file identically");

            var file = """
                phone,firstname,lastname,email
                919143000001,Asha,Rao,asha@example.com
                919143000002,Bad,Row,
                919143000003,Chen,Li,chen@example.com
                """;

            var (valid, invalid) = await CountAsync(file, MessageChannel.Email);

            run.Check("an email campaign counts only the addressable rows", valid == 2, $"valid={valid}");
            run.Check("and reports the rest as errors", invalid == 1, $"invalid={invalid}");

            var (waValid, waInvalid) = await CountAsync(file, MessageChannel.WhatsApp);

            run.Check("the same file is fully valid on WhatsApp", waValid == 3, $"valid={waValid}");
            run.Check("with nothing reported", waInvalid == 0, $"invalid={waInvalid}");
        }
        catch (Exception ex)
        {
            run.Error("Phase 7 threw", ex);
        }
    }

    /// <summary>Parses one header line through the real reader, so quoting behaves as in production.</summary>
    private static async Task<List<string>> HeaderOf(string header)
    {
        await foreach (var (_, fields) in ReadAsync(header))
        {
            return fields;
        }

        throw new InvalidOperationException("The reader returned no header row.");
    }

    /// <summary>
    /// Counts a file exactly the way both the preview endpoint and the create service do.
    ///
    /// This mirrors their loop rather than calling them, because the point is to check the
    /// judgement the shared helper makes — the two call sites are thin wrappers around it.
    /// </summary>
    private static async Task<(int Valid, int Invalid)> CountAsync(string content, MessageChannel channel)
    {
        CsvColumnMap? map = null;
        int valid = 0, invalid = 0;

        await foreach (var (rowNumber, fields) in ReadAsync(content))
        {
            if (map is null)
            {
                map = BulkCampaignCsv.MapColumns(fields, channel)
                    ?? throw new InvalidOperationException($"Header rejected for {channel}.");
                continue;
            }

            if (BulkCampaignCsv.TryReadRow(fields, rowNumber, map, out _, out _, channel)) valid++;
            else invalid++;
        }

        return (valid, invalid);
    }

    private static IAsyncEnumerable<(int RowNumber, List<string> Fields)> ReadAsync(string content) =>
        BulkCampaignCsv.ReadRecordsAsync(new MemoryStream(Encoding.UTF8.GetBytes(content)));
}
