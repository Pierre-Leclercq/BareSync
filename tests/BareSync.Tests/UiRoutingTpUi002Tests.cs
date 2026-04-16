using System.Text.Json;
using BareSync.Infra;
using Xunit;

namespace BareSync.Tests;

public sealed class UiRoutingTpUi002Tests
{
    // Plan: TP-UI-002 (Docs/BareSync.Test.Plan.md)
    [Fact]
    public async Task TP_UI_002_InteractiveSyncApplyConfirmationDeclined()
    {
        using var temp = new TempDirectory();
        var sourceRoot = Path.Combine(temp.RootPath, "source");
        var mirrorRoot = Path.Combine(temp.RootPath, "mirror");
        Directory.CreateDirectory(sourceRoot);
        Directory.CreateDirectory(mirrorRoot);

        var sourceIndexPath = Path.Combine(sourceRoot, "source_index.csv");
        var destIndexPath = Path.Combine(mirrorRoot, "dest_index.csv");
        await File.WriteAllTextAsync(sourceIndexPath, CsvIndexWriter.Header + Environment.NewLine);
        await File.WriteAllTextAsync(destIndexPath, CsvIndexWriter.Header + Environment.NewLine);

        var configPayload = new
        {
            SourceRoot = sourceRoot,
            MirrorRoot = mirrorRoot,
            SourceIndexCsvPath = sourceIndexPath,
            DestIndexCsvPath = destIndexPath
        };
        var configJson = JsonSerializer.Serialize(configPayload, new JsonSerializerOptions { WriteIndented = true });

        var stdin = string.Join(
                Environment.NewLine,
                new[]
                {
                    "1",
                    "4",
                    "n",
                    "0",
                    "0"
                }) + Environment.NewLine;

        var result = await UiRoutingHarness.RunBareSyncAsync(
            stdin,
            timeout: TimeSpan.FromSeconds(30),
            configJson: configJson);

        var trace = result.ScreenTrace.ToList();

        Assert.True(
            trace.Contains("S1.6"),
            $"Expected S1.6 in trace. Trace=[{string.Join(", ", trace)}] ExitCode={result.ExitCode} StdoutSample={result.StdoutRaw} StderrSample={result.StderrRaw}");
        Assert.DoesNotContain("S1.8", trace);
        Assert.DoesNotContain("S1.5a", trace);

        var confirmIndex = trace.IndexOf("S1.6");
        var menuIndices = trace
            .Select((value, index) => (value, index))
            .Where(item => item.value == "S0.1")
            .Select(item => item.index)
            .ToList();

        if (confirmIndex >= 0 && menuIndices.Count > 0)
        {
            Assert.True(
                menuIndices.Any(index => index > confirmIndex),
                "Menu should appear after declining confirmation when detectable.");
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            RootPath = Path.Combine(
                Path.GetTempPath(),
                "BareSyncTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(RootPath);
        }

        public string RootPath { get; }

        public void Dispose()
        {
            if (!Directory.Exists(RootPath))
            {
                return;
            }

            try
            {
                Directory.Delete(RootPath, recursive: true);
            }
            catch
            {
            }
        }
    }
}
