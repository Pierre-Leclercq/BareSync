using BareSync.Domain;
using BareSync.Infra;
using BareSync.UI;
using Bare.Primitive.UI;

namespace BareSync.UI;

internal static class SettingsEditor
{
    public static void ShowValidationErrors(
        IReadOnlyList<ConfigValidationError> errors,
        Action<string>? writeLine = null)
    {
        var resolvedWriteLine = writeLine ?? Bare.Primitive.UI.UiConsole.WriteLine;

        resolvedWriteLine("Missing or invalid settings:");
        foreach (var error in errors)
        {
            resolvedWriteLine($"- {error.Field}: {error.Message}");
        }

        UiInteraction.SkipNextClear();
    }

    public static void Run(AppConfig config)
    {
        Run(
            config,
            uiInput: null,
            keyInput: null,
            isInputRedirected: null,
            clear: null,
            write: null,
            writeLine: null,
            pathPromptService: null,
            save: null);
    }

    public static void Run(
        AppConfig config,
        IUiInput? uiInput,
        IUiKeyInput? keyInput,
        Func<bool>? isInputRedirected)
    {
        Run(
            config,
            uiInput,
            keyInput,
            isInputRedirected,
            clear: null,
            write: null,
            writeLine: null,
            pathPromptService: null,
            save: null);
    }

    public static void Run(
        AppConfig config,
        IUiInput? uiInput,
        IUiKeyInput? keyInput,
        Func<bool>? isInputRedirected,
        Action<string>? write,
        Action<string>? writeLine)
    {
        Run(
            config,
            uiInput,
            keyInput,
            isInputRedirected,
            clear: null,
            write,
            writeLine,
            pathPromptService: null,
            save: null);
    }

    internal static void Run(
        AppConfig config,
        IUiInput? uiInput,
        IUiKeyInput? keyInput,
        Func<bool>? isInputRedirected,
        Action? clear,
        Action<string>? write,
        Action<string>? writeLine,
        ISettingsPathPromptService? pathPromptService,
        Func<AppConfig, bool>? save)
    {
        var resolvedClear = clear ?? UiInteraction.Clear;
        var resolvedWrite = write ?? Bare.Primitive.UI.UiConsole.Write;
        var resolvedWriteLine = writeLine ?? Bare.Primitive.UI.UiConsole.WriteLine;
        var resolvedPathPromptService = pathPromptService ?? new SettingsPathPromptService();
        var resolvedSave = save ?? ConfigService.Save;

        var options = new[]
        {
            "Edit Source Root",
            "Edit Mirror Root",
            "Edit Source Index Csv Path",
            "Edit Dest Index Csv Path",
            "Toggle Mirror mode (delete destination-only files)",
            "Edit Encrypted Output Root (optional)",
            "Edit Restore Root (optional)",
            "Cycle Restore Smart mode (Smart/FastSmart)",
            "Toggle LogDebug mode (verbose per-item logs)"
        };

        var running = true;
        while (running)
        {
            var selection = PromptEditSettings(
                config,
                options,
                "Back",
                uiInput,
                keyInput,
                isInputRedirected,
                resolvedClear,
                resolvedWrite,
                resolvedWriteLine);
            switch (selection)
            {
                case 0:
                    running = false;
                    break;
                case 1:
                    UpdateDirectory(
                        config.SourceRoot,
                        config,
                        value => config.SourceRoot = value,
                        resolvedPathPromptService,
                        resolvedSave,
                        resolvedWriteLine);
                    break;
                case 2:
                    UpdateDirectory(
                        config.MirrorRoot,
                        config,
                        value => config.MirrorRoot = value,
                        resolvedPathPromptService,
                        resolvedSave,
                        resolvedWriteLine);
                    break;
                case 3:
                    UpdateIndexCsvPath(
                        config.SourceRoot,
                        config.SourceIndexCsvPath,
                        AppConfig.DefaultSourceIndexCsvFileName,
                        config,
                        value => config.SourceIndexCsvPath = value,
                        resolvedPathPromptService,
                        resolvedSave,
                        resolvedWriteLine);
                    break;
                case 4:
                    UpdateIndexCsvPath(
                        config.MirrorRoot,
                        config.DestIndexCsvPath,
                        AppConfig.DefaultDestIndexCsvFileName,
                        config,
                        value => config.DestIndexCsvPath = value,
                        resolvedPathPromptService,
                        resolvedSave,
                        resolvedWriteLine);
                    break;
                case 5:
                    config.Mirror = !config.Mirror;
                    if (!resolvedSave(config))
                    {
                        resolvedWriteLine("Failed to save settings.");
                    }
                    break;
                case 6:
                    UpdateDirectory(
                        config.EncryptedOutputRoot,
                        config,
                        value => config.EncryptedOutputRoot = value,
                        resolvedPathPromptService,
                        resolvedSave,
                        resolvedWriteLine);
                    break;
                case 7:
                    UpdateDirectory(
                        config.RestoreRoot,
                        config,
                        value => config.RestoreRoot = value,
                        resolvedPathPromptService,
                        resolvedSave,
                        resolvedWriteLine);
                    break;
                case 8:
                    config.RestoreSmartMode = config.RestoreSmartMode == RestoreSmartMode.Smart
                        ? RestoreSmartMode.FastSmart
                        : RestoreSmartMode.Smart;
                    if (!resolvedSave(config))
                    {
                        resolvedWriteLine("Failed to save settings.");
                    }
                    break;
                case 9:
                    config.LogDebug = !config.LogDebug;
                    if (!resolvedSave(config))
                    {
                        resolvedWriteLine("Failed to save settings.");
                    }
                    break;
            }
        }
    }

    private static int PromptEditSettings(
        AppConfig config,
        IReadOnlyList<string> options,
        string zeroLabel,
        IUiInput? uiInput,
        IUiKeyInput? keyInput,
        Func<bool>? isInputRedirected,
        Action clear,
        Action<string> write,
        Action<string> writeLine)
    {
        clear();
        RenderEditSettings(config, options, zeroLabel, writeLine);
        write("Select an option: ");

        return UiInteraction.ReadMenuDigit(
            0,
            options.Count,
            uiInput,
            keyInput,
            isInputRedirected);
    }

    private static void RenderEditSettings(
        AppConfig config,
        IReadOnlyList<string> options,
        string zeroLabel,
        Action<string> writeLine)
    {
        writeLine("** Current config **");
        writeLine(string.Empty);
        writeLine($"Source Root='{FormatValue(config.SourceRoot)}'");
        writeLine($"Mirror Root='{FormatValue(config.MirrorRoot)}'");
        writeLine($"Mirror Mode='{(config.Mirror ? "ON" : "OFF")}'");
        writeLine(string.Empty);
        writeLine($"Source Index Csv Path='{FormatValue(config.SourceIndexCsvPath)}'");
        writeLine($"Dest Index Csv Path='{FormatValue(config.DestIndexCsvPath)}'");
        writeLine(string.Empty);
        writeLine($"Encrypted Output Root (optional)='{FormatValue(config.EncryptedOutputRoot)}'");
        writeLine($"Restore Root (optional)='{FormatValue(config.RestoreRoot)}'");
        writeLine($"RestoreSmartMode='{config.RestoreSmartMode}'");
        writeLine($"LogDebug='{(config.LogDebug ? "ON" : "OFF")}'");
        writeLine(string.Empty);
        writeLine("** Menu **");
        writeLine(string.Empty);

        for (var index = 0; index < options.Count; index++)
        {
            writeLine($"{index + 1}. {options[index]}");
        }

        writeLine($"0. {zeroLabel}");
        writeLine(string.Empty);
    }

    private static string FormatValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "<not set>" : value;
    }

    private static void UpdateDirectory(
        string startDir,
        AppConfig config,
        Action<string> assign,
        ISettingsPathPromptService pathPromptService,
        Func<AppConfig, bool> save,
        Action<string> writeLine)
    {
        var selected = pathPromptService.PickDirectory("Select directory", startDir);
        if (string.IsNullOrWhiteSpace(selected))
        {
            return;
        }

        assign(selected);
        if (!save(config))
        {
            writeLine("Failed to save settings.");
        }
    }

    private static void UpdateIndexCsvPath(
        string rootPath,
        string currentValue,
        string defaultFileName,
        AppConfig config,
        Action<string> assign,
        ISettingsPathPromptService pathPromptService,
        Func<AppConfig, bool> save,
        Action<string> writeLine)
    {
        var selected = pathPromptService.PickIndexCsvPath(
            "Select index csv",
            rootPath,
            currentValue,
            defaultFileName);
        if (string.IsNullOrWhiteSpace(selected))
        {
            return;
        }

        assign(selected);
        if (!save(config))
        {
            writeLine("Failed to save settings.");
        }
    }

    private static void UpdateFilePath(
        string currentValue,
        string defaultFileName,
        AppConfig config,
        Action<string> assign,
        ISettingsPathPromptService pathPromptService,
        Func<AppConfig, bool> save,
        Action<string> writeLine)
    {
        var selected = pathPromptService.PickFilePath("Select file", currentValue, defaultFileName);
        if (string.IsNullOrWhiteSpace(selected))
        {
            return;
        }

        assign(selected);
        if (!save(config))
        {
            writeLine("Failed to save settings.");
        }
    }
}
