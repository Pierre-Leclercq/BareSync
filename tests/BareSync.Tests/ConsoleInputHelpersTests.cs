using System.Text;
using Bare.Primitive.UI;
using BareSync.App.Common;

namespace BareSync.Tests;

public sealed class ConsoleInputHelpersTests
{
    [Fact]
    public void ConfirmYesNo_DoesNotSpamPrompt_WhileWaitingForKey()
    {
        var promptCount = 0;
        var outputEvents = new List<string>();

        var result = ConsoleInputHelpers.ConfirmYesNo(
            prompt: "Proceed? ",
            uiInput: new ScriptedUiInput(Array.Empty<string?>()),
            keyInput: new DelayedScriptedUiKeyInput(
                noKeyReadCount: 5,
                keys: new[]
                {
                    new ConsoleKeyInfo('y', ConsoleKey.Y, false, false, false)
                }),
            isInputRedirected: () => false,
            write: text =>
            {
                outputEvents.Add($"W:{text}");
                if (text == "Proceed? ")
                {
                    promptCount++;
                }
            },
            writeLine: _ => outputEvents.Add("NL"));

        Assert.True(result);
        Assert.Equal(1, promptCount);
        Assert.NotEmpty(outputEvents);
        Assert.Equal("NL", outputEvents[^1]);
    }

    [Fact]
    public void ConfirmYesNo_WhenRedirected_RetriesUntilValidAnswer()
    {
        var promptCount = 0;

        var result = ConsoleInputHelpers.ConfirmYesNo(
            prompt: "Proceed? ",
            uiInput: new ScriptedUiInput(new string?[] { "", "maybe", " y " }),
            keyInput: new ScriptedUiKeyInput(Array.Empty<ConsoleKeyInfo>()),
            isInputRedirected: () => true,
            write: text =>
            {
                if (text == "Proceed? ")
                {
                    promptCount++;
                }
            },
            writeLine: _ => { });

        Assert.True(result);
        Assert.Equal(3, promptCount);
    }

    [Fact]
    public void ConfirmYesNo_WhenNotRedirected_UsesKeyInputAndRetriesUntilValidAnswer()
    {
        var promptCount = 0;
        var outputEndsOnNewLine = false;

        var result = ConsoleInputHelpers.ConfirmYesNo(
            prompt: "Proceed? ",
            uiInput: new ScriptedUiInput(Array.Empty<string?>()),
            keyInput: new ScriptedUiKeyInput(new[]
            {
                new ConsoleKeyInfo('x', ConsoleKey.X, false, false, false),
                new ConsoleKeyInfo('n', ConsoleKey.N, false, false, false)
            }),
            isInputRedirected: () => false,
            write: text =>
            {
                if (text == "Proceed? ")
                {
                    promptCount++;
                }

                outputEndsOnNewLine = false;
            },
            writeLine: _ => outputEndsOnNewLine = true);

        Assert.False(result);
        Assert.Equal(2, promptCount);
        Assert.True(outputEndsOnNewLine);
    }

    [Fact]
    public void ConfirmYesNo_WhenNotRedirected_InvalidKeyThenYes_ReprintsPromptOnce()
    {
        var promptCount = 0;
        var outputEndsOnNewLine = false;

        var result = ConsoleInputHelpers.ConfirmYesNo(
            prompt: "Proceed? ",
            uiInput: new ScriptedUiInput(Array.Empty<string?>()),
            keyInput: new ScriptedUiKeyInput(new[]
            {
                new ConsoleKeyInfo('x', ConsoleKey.X, false, false, false),
                new ConsoleKeyInfo('y', ConsoleKey.Y, false, false, false)
            }),
            isInputRedirected: () => false,
            write: text =>
            {
                if (text == "Proceed? ")
                {
                    promptCount++;
                }

                outputEndsOnNewLine = false;
            },
            writeLine: _ => outputEndsOnNewLine = true);

        Assert.True(result);
        Assert.Equal(2, promptCount);
        Assert.True(outputEndsOnNewLine);
    }

    [Fact]
    public void ReadLineWithEscape_WhenNotRedirected_SupportsBackspaceAndEcho()
    {
        var writes = new StringBuilder();
        var lineCount = 0;

        var value = ConsoleInputHelpers.ReadLineWithEscape(
            uiInput: new ScriptedUiInput(Array.Empty<string?>()),
            keyInput: new ScriptedUiKeyInput(new[]
            {
                new ConsoleKeyInfo('a', ConsoleKey.A, false, false, false),
                new ConsoleKeyInfo('b', ConsoleKey.B, false, false, false),
                new ConsoleKeyInfo('\b', ConsoleKey.Backspace, false, false, false),
                new ConsoleKeyInfo('c', ConsoleKey.C, false, false, false),
                new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false)
            }),
            isInputRedirected: () => false,
            write: text => writes.Append(text),
            writeLine: _ => lineCount++);

        Assert.Equal("ac", value);
        Assert.Contains("\b \b", writes.ToString(), StringComparison.Ordinal);
        Assert.Equal(1, lineCount);
    }

    [Fact]
    public void ReadPasswordWithEscape_WhenMasked_DoesNotEchoTypedCharacters()
    {
        var writes = new StringBuilder();

        var value = ConsoleInputHelpers.ReadPasswordWithEscape(
            maskInput: true,
            uiInput: new ScriptedUiInput(Array.Empty<string?>()),
            keyInput: new ScriptedUiKeyInput(new[]
            {
                new ConsoleKeyInfo('a', ConsoleKey.A, false, false, false),
                new ConsoleKeyInfo('b', ConsoleKey.B, false, false, false),
                new ConsoleKeyInfo('\b', ConsoleKey.Backspace, false, false, false),
                new ConsoleKeyInfo('c', ConsoleKey.C, false, false, false),
                new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false)
            }),
            isInputRedirected: () => false,
            write: text => writes.Append(text),
            writeLine: _ => { });

        var output = writes.ToString();
        Assert.Equal("ac", value);
        Assert.Contains("*", output, StringComparison.Ordinal);
        Assert.DoesNotContain("a", output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("b", output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("c", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReadPasswordWithEscape_WhenEscapePressed_ReturnsNull()
    {
        var lineCount = 0;

        var value = ConsoleInputHelpers.ReadPasswordWithEscape(
            maskInput: true,
            uiInput: new ScriptedUiInput(Array.Empty<string?>()),
            keyInput: new ScriptedUiKeyInput(new[]
            {
                new ConsoleKeyInfo('\u001b', ConsoleKey.Escape, false, false, false)
            }),
            isInputRedirected: () => false,
            write: _ => { },
            writeLine: _ => lineCount++);

        Assert.Null(value);
        Assert.Equal(1, lineCount);
    }

    [Fact]
    public void ReadLineWithEscape_WhenRedirected_UsesUiInput()
    {
        var value = ConsoleInputHelpers.ReadLineWithEscape(
            uiInput: new ScriptedUiInput(new[] { "from-redirected-input" }),
            keyInput: new ScriptedUiKeyInput(Array.Empty<ConsoleKeyInfo>()),
            isInputRedirected: () => true,
            write: _ => { },
            writeLine: _ => { });

        Assert.Equal("from-redirected-input", value);
    }

    private sealed class DelayedScriptedUiKeyInput : IUiKeyInput
    {
        private readonly Queue<ConsoleKeyInfo> _keys;
        private int _remainingNoKeyReads;

        public DelayedScriptedUiKeyInput(int noKeyReadCount, IEnumerable<ConsoleKeyInfo> keys)
        {
            _remainingNoKeyReads = noKeyReadCount;
            _keys = new Queue<ConsoleKeyInfo>(keys);
        }

        public bool TryReadKey(out ConsoleKeyInfo keyInfo, bool intercept = true)
        {
            if (_remainingNoKeyReads > 0)
            {
                _remainingNoKeyReads--;
                keyInfo = default;
                return false;
            }

            if (_keys.Count == 0)
            {
                keyInfo = default;
                return false;
            }

            keyInfo = _keys.Dequeue();
            return true;
        }
    }
}