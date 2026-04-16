using System.Text;
using Bare.Primitive.UI;
using BareSync.Domain;
using BareSync.UI;

namespace BareSync.Tests;

public sealed class SettingsEditorTests
{
    private sealed class StubSettingsPathPromptService : ISettingsPathPromptService
    {
        public string? DirectoryValue { get; set; }
        public string? IndexCsvValue { get; set; }
        public string? FileValue { get; set; }

        public string? PickDirectory(string title, string? currentValue) => DirectoryValue;

        public string? PickIndexCsvPath(string title, string? rootPath, string? currentValue, string defaultFileName) =>
            IndexCsvValue;

        public string? PickFilePath(string title, string? currentValue, string defaultFileName) => FileValue;
    }

    [Fact]
    public void ShowValidationErrors_UsesInjectedWriteLine()
    {
        var lines = new List<string>();
        var errors = new[]
        {
            new ConfigValidationError("SourceRoot", "SourceRoot is required."),
            new ConfigValidationError("MirrorRoot", "MirrorRoot is required.")
        };

        SettingsEditor.ShowValidationErrors(errors, writeLine: lines.Add);

        Assert.Equal(3, lines.Count);
        Assert.Equal("Missing or invalid settings:", lines[0]);
        Assert.Equal("- SourceRoot: SourceRoot is required.", lines[1]);
        Assert.Equal("- MirrorRoot: MirrorRoot is required.", lines[2]);
    }

    [Fact]
    public async Task Run_WithInjectedWritersAndRedirectedInput_RendersMenuAndExitsOnZero()
    {
        var output = new StringBuilder();
        var config = new AppConfig();

        await AppSettingsIsolation.WithIsolatedAppSettingsAsync(
            AppContext.BaseDirectory,
            newJson: null,
            async () =>
            {
                SettingsEditor.Run(
                    config,
                    uiInput: new ScriptedUiInput(new[] { "0" }),
                    keyInput: new ScriptedUiKeyInput(Array.Empty<ConsoleKeyInfo>()),
                    isInputRedirected: () => true,
                    write: text => output.Append(text),
                    writeLine: text => output.AppendLine(text));

                await Task.CompletedTask;
            });

        var rendered = output.ToString();
        Assert.Contains("** Current config **", rendered, StringComparison.Ordinal);
        Assert.Contains("LogDebug='OFF'", rendered, StringComparison.Ordinal);
        Assert.Contains("RestoreSmartMode='Smart'", rendered, StringComparison.Ordinal);
        Assert.Contains("** Menu **", rendered, StringComparison.Ordinal);
        Assert.Contains("1. Edit Source Root", rendered, StringComparison.Ordinal);
        Assert.Contains("7. Edit Restore Root (optional)", rendered, StringComparison.Ordinal);
        Assert.Contains("8. Cycle Restore Smart mode (Smart/FastSmart)", rendered, StringComparison.Ordinal);
        Assert.Contains("9. Toggle LogDebug mode (verbose per-item logs)", rendered, StringComparison.Ordinal);
        Assert.Contains("0. Back", rendered, StringComparison.Ordinal);
        Assert.Contains("Select an option: ", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Run_WithInjectedClear_InvokesClearBeforeRendering()
    {
        var clearCalls = 0;

        await AppSettingsIsolation.WithIsolatedAppSettingsAsync(
            AppContext.BaseDirectory,
            newJson: null,
            async () =>
            {
                SettingsEditor.Run(
                    new AppConfig(),
                    uiInput: new ScriptedUiInput(new[] { "0" }),
                    keyInput: new ScriptedUiKeyInput(Array.Empty<ConsoleKeyInfo>()),
                    isInputRedirected: () => true,
                    clear: () => clearCalls++,
                    write: _ => { },
                    writeLine: _ => { },
                    pathPromptService: new StubSettingsPathPromptService(),
                    save: _ => true);

                await Task.CompletedTask;
            });

        Assert.Equal(1, clearCalls);
    }

    [Fact]
    public async Task Run_WhenDirectorySelectedAndSaveFails_WritesFailureMessage()
    {
        var output = new StringBuilder();
        var config = new AppConfig();
        var pathPromptService = new StubSettingsPathPromptService
        {
            DirectoryValue = @"C:\\Injected\\Source"
        };

        await AppSettingsIsolation.WithIsolatedAppSettingsAsync(
            AppContext.BaseDirectory,
            newJson: null,
            async () =>
            {
                SettingsEditor.Run(
                    config,
                    uiInput: new ScriptedUiInput(new[] { "1", "0" }),
                    keyInput: new ScriptedUiKeyInput(Array.Empty<ConsoleKeyInfo>()),
                    isInputRedirected: () => true,
                    clear: () => { },
                    write: _ => { },
                    writeLine: text => output.AppendLine(text),
                    pathPromptService: pathPromptService,
                    save: _ => false);

                await Task.CompletedTask;
            });

        Assert.Equal(@"C:\\Injected\\Source", config.SourceRoot);
        Assert.Contains("Failed to save settings.", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Run_WhenOptionalDirectoryPickerCanceled_DoesNotAttemptSave()
    {
        var saveCalls = 0;
        var config = new AppConfig
        {
            RestoreRoot = @"C:\\Restore"
        };
        var pathPromptService = new StubSettingsPathPromptService
        {
            DirectoryValue = null
        };

        await AppSettingsIsolation.WithIsolatedAppSettingsAsync(
            AppContext.BaseDirectory,
            newJson: null,
            async () =>
            {
                SettingsEditor.Run(
                    config,
                    uiInput: new ScriptedUiInput(new[] { "7", "0" }),
                    keyInput: new ScriptedUiKeyInput(Array.Empty<ConsoleKeyInfo>()),
                    isInputRedirected: () => true,
                    clear: () => { },
                    write: _ => { },
                    writeLine: _ => { },
                    pathPromptService: pathPromptService,
                    save: _ =>
                    {
                        saveCalls++;
                        return true;
                    });

                await Task.CompletedTask;
            });

        Assert.Equal(0, saveCalls);
        Assert.Equal(@"C:\\Restore", config.RestoreRoot);
    }

    [Fact]
    public async Task Run_WhenToggleLogDebugSelected_TogglesAndSaves()
    {
        var saveCalls = 0;
        var config = new AppConfig
        {
            LogDebug = false
        };

        await AppSettingsIsolation.WithIsolatedAppSettingsAsync(
            AppContext.BaseDirectory,
            newJson: null,
            async () =>
            {
                SettingsEditor.Run(
                    config,
                    uiInput: new ScriptedUiInput(new[] { "9", "0" }),
                    keyInput: new ScriptedUiKeyInput(Array.Empty<ConsoleKeyInfo>()),
                    isInputRedirected: () => true,
                    clear: () => { },
                    write: _ => { },
                    writeLine: _ => { },
                    pathPromptService: new StubSettingsPathPromptService(),
                    save: _ =>
                    {
                        saveCalls++;
                        return true;
                    });

                await Task.CompletedTask;
            });

        Assert.True(config.LogDebug);
        Assert.Equal(1, saveCalls);
    }

    [Fact]
    public async Task Run_WhenCycleRestoreSmartModeSelected_CyclesAndSaves()
    {
        var saveCalls = 0;
        var config = new AppConfig
        {
            RestoreSmartMode = RestoreSmartMode.Smart
        };

        await AppSettingsIsolation.WithIsolatedAppSettingsAsync(
            AppContext.BaseDirectory,
            newJson: null,
            async () =>
            {
                SettingsEditor.Run(
                    config,
                    uiInput: new ScriptedUiInput(new[] { "8", "8", "0" }),
                    keyInput: new ScriptedUiKeyInput(Array.Empty<ConsoleKeyInfo>()),
                    isInputRedirected: () => true,
                    clear: () => { },
                    write: _ => { },
                    writeLine: _ => { },
                    pathPromptService: new StubSettingsPathPromptService(),
                    save: _ =>
                    {
                        saveCalls++;
                        return true;
                    });

                await Task.CompletedTask;
            });

        Assert.Equal(RestoreSmartMode.Smart, config.RestoreSmartMode);
        Assert.Equal(2, saveCalls);
    }
}
