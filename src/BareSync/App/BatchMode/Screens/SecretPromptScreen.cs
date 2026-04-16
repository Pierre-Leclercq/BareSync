using BareSync.Domain;
using BareSync.App.Common;
using BareSync.UI;
using Bare.Primitive.UI;

namespace BareSync.App.BatchMode.Screens;

/// <summary>
/// S2.13 Secret Prompt - Collect passwords for encrypted operations.
/// </summary>
internal sealed class SecretPromptScreen
{
    /// <summary>
    /// Prompts for required secrets. Returns null if cancelled.
    /// </summary>
    public Dictionary<string, string>? Show(BatchStorageDescriptor descriptor, AppConfig config)
    {
        return Show(
            descriptor,
            config,
            uiInput: null,
            keyInput: null,
            isInputRedirected: null,
            write: null,
            writeLine: null);
    }

    /// <summary>
    /// Prompts for required secrets with injectable input/output collaborators for deterministic tests.
    /// Returns null if cancelled.
    /// </summary>
    public Dictionary<string, string>? Show(
        BatchStorageDescriptor descriptor,
        AppConfig config,
        IUiInput? uiInput,
        IUiKeyInput? keyInput,
        Func<bool>? isInputRedirected,
        Action<string>? write,
        Action<string>? writeLine)
    {
        var resolvedInput = uiInput ?? new ConsoleUiInput();
        var redirected = isInputRedirected ?? (() => Bare.Primitive.UI.UiConsole.IsInputRedirected);
        var resolvedWrite = write ?? Bare.Primitive.UI.UiConsole.Write;
        var resolvedWriteLine = writeLine ?? Bare.Primitive.UI.UiConsole.WriteLine;

        var batch = BatchUiHelpers.LoadBatchV0(descriptor.Path);
        if (batch is null)
        {
            return null;
        }

        var secrets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var requiredSlots = BatchSecretResolver.GetRequiredSecretSlots(batch, config);

        if (requiredSlots.Count == 0)
        {
            return secrets;
        }

        UiInteraction.Clear();
        Bare.Primitive.UI.UiConsole.WriteLine("** BareSync **");
        Bare.Primitive.UI.UiConsole.WriteLine();
        SecretStoreProvider.WriteUnavailableWarning();

        foreach (var slot in requiredSlots)
        {
            if (SecretStoreProvider.TryLoadSecret(slot.SlotKey, out var storedSecret)
                && !string.IsNullOrWhiteSpace(storedSecret))
            {
                resolvedWriteLine($"Secret loaded from OS secret store for scope {slot.Scope}.");
                secrets[slot.SlotKey] = storedSecret;
                continue;
            }

            resolvedWriteLine($"Secret required: EncryptionPassword for scope {slot.Scope}");
            resolvedWrite("Enter password (will not be echoed): ");
            
            string? password;
            if (redirected())
            {
                password = resolvedInput.ReadLine();
                if (string.IsNullOrWhiteSpace(password))
                {
                    return null; // Cancelled
                }
            }
            else
            {
                password = ConsoleInputHelpers.ReadPasswordWithEscape(
                    maskInput: true,
                    uiInput: resolvedInput,
                    keyInput: keyInput,
                    isInputRedirected: redirected,
                    write: resolvedWrite,
                    writeLine: resolvedWriteLine);
                if (password is null)
                {
                    return null; // ESC pressed - cancelled
                }
            }
            
            if (string.IsNullOrWhiteSpace(password))
            {
                return null;
            }

            secrets[slot.SlotKey] = password;

            if (SecretStoreProvider.IsAvailable
                && !redirected()
                && ConsoleInputHelpers.ConfirmYesNo(
                    "Save password to OS secret store for this scope? (Y/N) ",
                    uiInput: resolvedInput,
                    keyInput: keyInput,
                    isInputRedirected: redirected,
                    write: resolvedWrite,
                    writeLine: resolvedWriteLine))
            {
                if (!SecretStoreProvider.TrySaveSecret(slot.SlotKey, password))
                {
                    resolvedWriteLine("Warning: failed to store password in OS secret store.");
                }
                else
                {
                    resolvedWriteLine("Password saved to OS secret store.");
                }
            }
        }

        return secrets;
    }
}
