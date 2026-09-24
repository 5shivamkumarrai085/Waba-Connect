using System.Diagnostics;

namespace EmailChannel.Tests;

/// <summary>
/// Collects results across every phase and prints one report.
///
/// <para>
/// Assertions record and continue rather than throwing. A phase that stops at its first failure
/// hides everything after it, and when several things break together — as they did the first time
/// this suite ran — seeing all of them at once is what makes the common cause obvious.
/// </para>
/// </summary>
public sealed class TestRun
{
    private readonly List<string> _failures = [];
    private readonly Dictionary<string, (int Passed, int Failed)> _byPhase = [];
    private string _phase = "(none)";
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

    public int Passed { get; private set; }
    public int Failed { get; private set; }

    public void BeginPhase(string phase)
    {
        _phase = phase;
        _byPhase.TryAdd(phase, (0, 0));
        Console.WriteLine();
        Console.WriteLine($"══ {phase} ".PadRight(78, '═'));
    }

    public void Section(string title)
    {
        Console.WriteLine();
        Console.WriteLine($"  {title}");
    }

    /// <param name="label">What is being asserted, phrased as the property that should hold.</param>
    /// <param name="ok">The assertion.</param>
    /// <param name="detail">
    /// The actual value, when there is one. Worth supplying for anything numeric or textual — a
    /// bare "FAIL" forces whoever is reading to go and reproduce it by hand.
    /// </param>
    public void Check(string label, bool ok, string? detail = null)
    {
        var counts = _byPhase[_phase];

        if (ok)
        {
            Passed++;
            _byPhase[_phase] = (counts.Passed + 1, counts.Failed);
            Console.WriteLine($"    PASS  {label}");
            return;
        }

        Failed++;
        _byPhase[_phase] = (counts.Passed, counts.Failed + 1);
        var rendered = detail is null ? label : $"{label}  (got: {detail})";
        _failures.Add($"[{_phase}] {rendered}");
        Console.WriteLine($"    FAIL  {rendered}");
    }

    /// <summary>Records a phase that could not run at all, so it cannot be mistaken for a pass.</summary>
    public void Error(string label, Exception ex)
    {
        Failed++;
        var counts = _byPhase.TryGetValue(_phase, out var existing) ? existing : (Passed: 0, Failed: 0);
        _byPhase[_phase] = (counts.Passed, counts.Failed + 1);

        var rendered = $"{label}: {ex.GetType().Name}: {ex.Message}";
        _failures.Add($"[{_phase}] {rendered}");

        Console.WriteLine($"    ERROR {rendered}");
        Console.WriteLine($"          {ex.StackTrace?.Split('\n').FirstOrDefault()?.Trim()}");
    }

    /// <summary>Notes a test that was deliberately not run, and why.</summary>
    public void Skip(string label, string reason)
    {
        Console.WriteLine($"    SKIP  {label}  ({reason})");
    }

    public int Report()
    {
        Console.WriteLine();
        Console.WriteLine("".PadRight(78, '═'));
        Console.WriteLine("  Summary");
        Console.WriteLine("".PadRight(78, '─'));

        foreach (var (phase, counts) in _byPhase)
        {
            var status = counts.Failed == 0 ? "ok  " : "FAIL";
            Console.WriteLine($"  {status}  {phase.PadRight(46)} {counts.Passed,3} passed  {counts.Failed,3} failed");
        }

        Console.WriteLine("".PadRight(78, '─'));

        if (_failures.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("  Failures:");
            foreach (var failure in _failures) Console.WriteLine($"    • {failure}");
        }

        Console.WriteLine();
        Console.WriteLine($"  {Passed} passed, {Failed} failed in {_stopwatch.Elapsed.TotalSeconds:0.0}s");
        Console.WriteLine("".PadRight(78, '═'));

        return Failed == 0 ? 0 : 1;
    }
}
