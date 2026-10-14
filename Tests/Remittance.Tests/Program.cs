using Remittance.Tests;

// Integration suite for Send Money over WhatsApp Flows.
//
//   dotnet run --project Tests/Remittance.Tests
//   dotnet run --project Tests/Remittance.Tests -- 1 4     (phases 1 and 4 only)
//
// Exits non-zero if anything failed.

var backendDirectory = ResolveBackendDirectory();
if (backendDirectory is null)
{
    Console.Error.WriteLine("Could not locate the Backend directory (expected a sibling of Tests/ containing appsettings.json).");
    return 2;
}

var requested = args.Where(a => int.TryParse(a, out _)).Select(int.Parse).ToHashSet();
bool ShouldRun(int phase) => requested.Count == 0 || requested.Contains(phase);

Console.WriteLine("".PadRight(78, '═'));
Console.WriteLine("  Send Money (WhatsApp Flows) — integration suite");
Console.WriteLine($"  Backend: {backendDirectory}");
if (requested.Count > 0) Console.WriteLine($"  Phases:  {string.Join(", ", requested.Order())}");
Console.WriteLine("".PadRight(78, '═'));

var run = new TestRun();
await using var harness = RemittanceHarness.Create(backendDirectory);

try
{
    await harness.CleanupAsync();
    await harness.SeedAsync();

    if (ShouldRun(1)) await Phase1_FlowCryptoTests.RunAsync(harness, run);
    if (ShouldRun(2)) await Phase2_OtpTests.RunAsync(harness, run);
    if (ShouldRun(3)) await Phase3_PricingAndQuoteTests.RunAsync(harness, run);
    if (ShouldRun(4)) await Phase4_FlowJourneyTests.RunAsync(harness, run);
    if (ShouldRun(5)) await Phase5_PaymentTests.RunAsync(harness, run);
    if (ShouldRun(6)) await Phase6_LiveRateTests.RunAsync(harness, run);
    if (ShouldRun(7)) await Phase7_BotRoutingTests.RunAsync(harness, run);
    if (ShouldRun(8)) await Phase8_MoneySafetyTests.RunAsync(harness, run);
}
catch (Exception ex)
{
    run.Error("Suite aborted", ex);
}
finally
{
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
    }
}

return run.Report();

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
