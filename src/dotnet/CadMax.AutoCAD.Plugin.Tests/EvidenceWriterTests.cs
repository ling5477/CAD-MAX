using Xunit;

namespace CadMax.AutoCAD.Plugin.Tests;

public sealed class EvidenceWriterTests : IDisposable
{
    private readonly string testRoot = Path.Combine(
        AppContext.BaseDirectory,
        "cad-max-plugin-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void WriterAppendsUtf8JsonLinesWithoutOverwriting()
    {
        var filePath = Path.Combine(testRoot, "evidence.jsonl");
        var writer = new BoundedJsonLineEvidenceWriter(filePath, maximumBytes: 16_384);
        var entry = CreateEntry();

        Assert.True(writer.TryAppend(entry));
        Assert.True(writer.TryAppend(entry));

        var lines = File.ReadAllLines(filePath);
        Assert.Equal(2, lines.Length);
        Assert.All(lines, line => Assert.Contains("PLUGIN_INITIALIZE_SUCCEEDED", line));
    }

    [Fact]
    public void WriterStopsAtConfiguredBoundWithoutTruncatingExistingEvidence()
    {
        var filePath = Path.Combine(testRoot, "bounded.jsonl");
        var writer = new BoundedJsonLineEvidenceWriter(filePath, maximumBytes: 700);
        var entry = CreateEntry();

        Assert.True(writer.TryAppend(entry));
        var original = File.ReadAllBytes(filePath);

        const int maximumAppendAttempts = 10;
        var reachedConfiguredBound = false;
        for (var attempt = 0; attempt < maximumAppendAttempts; attempt++)
        {
            if (!writer.TryAppend(entry))
            {
                reachedConfiguredBound = true;
                break;
            }
        }

        var finalBytes = File.ReadAllBytes(filePath);
        Assert.True(reachedConfiguredBound);
        Assert.True(finalBytes.AsSpan().StartsWith(original));
        Assert.True(finalBytes.Length <= 700);
    }

    [Fact]
    public void WriterReturnsFalseForIoFailure()
    {
        Directory.CreateDirectory(testRoot);
        var parentFile = Path.Combine(testRoot, "not-a-directory");
        File.WriteAllText(parentFile, "fixture");
        var writer = new BoundedJsonLineEvidenceWriter(
            Path.Combine(parentFile, "evidence.jsonl"));

        var firstResult = true;
        var exception = Record.Exception(() => firstResult = writer.TryAppend(CreateEntry()));

        Assert.Null(exception);
        Assert.False(firstResult);
        Assert.False(writer.TryAppend(CreateEntry()));
    }

    public void Dispose()
    {
        if (Directory.Exists(testRoot))
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }

    private static PluginLifecycleEvidenceEntry CreateEntry() =>
        PluginLifecycleEvidenceEntry.Create(
            PluginMetadata.CreateDefault(),
            new PluginRuntimeInfo(2025, "R25.0.58.0.0", "0.1.0", true),
            PluginLifecycleEventType.PluginInitializeSucceeded,
            PluginLifecycleState.Starting,
            PluginLifecycleState.Ready,
            success: true,
            processId: 42);
}
