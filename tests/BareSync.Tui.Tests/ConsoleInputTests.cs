using Bare.Primitive.UI;
using Xunit;

namespace BareSync.Tui.Tests;

public sealed class ConsoleInputTests
{
    [Theory]
    [InlineData('0', 0, 3, 0)]
    [InlineData('2', 0, 3, 2)]
    [InlineData('5', 1, 7, 5)]
    public void TryMapMenuDigit_AcceptsDigitsInRange(char keyChar, int min, int max, int expected)
    {
        Assert.True(ConsoleInput.TryMapMenuDigit(keyChar, min, max, out var selection));
        Assert.Equal(expected, selection);
    }

    [Theory]
    [InlineData('a', 0, 9)]
    [InlineData('0', 1, 9)]
    [InlineData('9', 0, 8)]
    public void TryMapMenuDigit_RejectsInvalidInput(char keyChar, int min, int max)
    {
        Assert.False(ConsoleInput.TryMapMenuDigit(keyChar, min, max, out var selection));
        Assert.Equal(0, selection);
    }

    [Fact]
    public void TryMapMenuDigit_EscapeMapsToZeroWhenAllowed()
    {
        Assert.True(ConsoleInput.TryMapMenuDigit('\u001b', 0, 5, out var selection));
        Assert.Equal(0, selection);
    }

    [Fact]
    public void ReadMenuDigit_UsesScriptedUiInput_WhenRedirected()
    {
        var value = ConsoleInput.ReadMenuDigit(
            min: 0,
            max: 3,
            uiInput: new ScriptedUiInput(new[] { "2" }),
            keyInput: new ScriptedUiKeyInput(Array.Empty<ConsoleKeyInfo>()),
            isInputRedirected: () => true);

        Assert.Equal(2, value);
    }

    [Fact]
    public void ReadMenuDigit_UsesScriptedUiKeyInput_WhenNotRedirected()
    {
        var value = ConsoleInput.ReadMenuDigit(
            min: 0,
            max: 3,
            uiInput: new ScriptedUiInput(Array.Empty<string?>()),
            keyInput: new ScriptedUiKeyInput(new[]
            {
                new ConsoleKeyInfo('3', ConsoleKey.D3, false, false, false)
            }),
            isInputRedirected: () => false);

        Assert.Equal(3, value);
    }
}
