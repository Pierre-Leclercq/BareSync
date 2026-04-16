using BareSync.UI;

namespace BareSync.Tests;

public sealed class InlineProgressTextTests
{
    [Fact]
    public void PadForRewrite_WhenPreviousIsLonger_PadsWithTrailingSpaces()
    {
        var padded = InlineProgressText.PadForRewrite("  Progress: 9/10 (90%)", previousLength: 28);

        Assert.Equal(28, padded.Length);
        Assert.StartsWith("  Progress: 9/10 (90%)", padded, StringComparison.Ordinal);
    }

    [Fact]
    public void PadForRewrite_WhenPreviousIsShorter_ReturnsOriginalText()
    {
        var text = "  Progress: 100/100 (100%)";

        var padded = InlineProgressText.PadForRewrite(text, previousLength: 10);

        Assert.Equal(text, padded);
    }
}
