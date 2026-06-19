using System.Diagnostics;

namespace Scrcap.Core.Diagnostics;

public static class ScrcapDiagnostics
{
    private const string EnvironmentVariable = "SCRCAP_DIAGNOSTICS";

    public static bool IsEnabled =>
        IsOptedIn(Environment.GetEnvironmentVariable(EnvironmentVariable));

    public static DiagnosticSpan Start(string name, params (string Key, object? Value)[] fields) =>
        IsEnabled ? new DiagnosticSpan(name, fields) : DiagnosticSpan.Disabled;

    public static void Mark(string name, params (string Key, object? Value)[] fields)
    {
        if (!IsEnabled)
        {
            return;
        }

        Trace.WriteLine(Format("mark", name, null, fields));
    }

    public static void Measure(string name, TimeSpan elapsed, params (string Key, object? Value)[] fields)
    {
        if (!IsEnabled)
        {
            return;
        }

        Trace.WriteLine(Format("span", name, elapsed, fields));
    }

    private static bool IsOptedIn(string? value) =>
        string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
        || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
        || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);

    internal static string Format(string kind, string name, TimeSpan? elapsed, IReadOnlyList<(string Key, object? Value)> fields)
    {
        var parts = new List<string>
        {
            $"scrcap.diagnostic.{kind}",
            $"name={name}",
        };

        if (elapsed is { } duration)
        {
            parts.Add($"elapsedMs={duration.TotalMilliseconds:F2}");
        }

        foreach (var (key, value) in fields)
        {
            if (value is null)
            {
                continue;
            }

            parts.Add($"{key}={value}");
        }

        return string.Join(" ", parts);
    }
}

public sealed class DiagnosticSpan : IDisposable
{
    internal static readonly DiagnosticSpan Disabled = new();

    private readonly string name;
    private readonly (string Key, object? Value)[] fields;
    private readonly Stopwatch stopwatch;
    private bool disposed;

    private DiagnosticSpan()
    {
        name = string.Empty;
        fields = [];
        stopwatch = new Stopwatch();
        disposed = true;
    }

    internal DiagnosticSpan(string name, params (string Key, object? Value)[] fields)
    {
        this.name = name;
        this.fields = fields;
        stopwatch = Stopwatch.StartNew();
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        stopwatch.Stop();
        Trace.WriteLine(ScrcapDiagnostics.Format("span", name, stopwatch.Elapsed, fields));
    }
}
