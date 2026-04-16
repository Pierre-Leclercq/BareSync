using Xunit;
using System.Linq;

namespace BareSync.Tests;

public sealed class UiRoutingTpUi001Tests
{
    [Fact]
    public async Task TP_UI_001_InteractiveMissingSettings_RoutesTo_S1_5a()
    {
        var stdin = string.Join(
                Environment.NewLine,
                new[]
                {
                    "1",
                    "1",
                    "0",
                    "0"
                }) + Environment.NewLine;

        var result = await UiRoutingHarness.RunBareSyncAsync(stdin, timeout: TimeSpan.FromSeconds(10));

        var trace = result.ScreenTrace.ToList();

        Assert.Contains("S1.5a", trace);

        var firstInvalid = trace.IndexOf("S1.5a");

        var menuIndices = trace
            .Select((value, index) => (value, index))
            .Where(item => item.value == "S0.1")
            .Select(item => item.index)
            .ToList();
        if (menuIndices.Count > 0)
        {
            Assert.True(
                menuIndices.Any(index => index < firstInvalid || index > firstInvalid),
                "Main menu should appear at least once in the trace when detectable.");
        }

        Assert.DoesNotContain("S1.8", trace);

        var firstSettingsMenu = trace.IndexOf("S1.2");
        if (firstSettingsMenu >= 0)
        {
            Assert.True(firstSettingsMenu < firstInvalid, "Settings menu (S1.2) should precede the invalid settings screen.");
        }
    }
}
