using System.Text.Json;

namespace PotPlayerNext.Services;

public static class RuntimeEvidence
{
    // Opt-in integration-test telemetry. Normal launches do not log paths or events.
    private static readonly string? Destination = Environment.GetEnvironmentVariable("PPN_TEST_EVENTS");
    private static readonly object Gate = new();
    public static void Write(string name, object? data = null)
    {
        if (string.IsNullOrWhiteSpace(Destination)) return;
        try
        {
            var entry = JsonSerializer.Serialize(new { name, data, processId = Environment.ProcessId, timestampUtc = DateTimeOffset.UtcNow });
            lock (Gate) File.AppendAllText(Destination, entry + Environment.NewLine);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { System.Diagnostics.Debug.WriteLine(error); }
    }
}
