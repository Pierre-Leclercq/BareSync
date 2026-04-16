using BareSync.Domain;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace BareSync.App.Common;

/// <summary>
/// OS-backed secret storage provider used by interactive and batch secret prompts.
/// Windows: Credential Manager. Linux: secret-tool/libsecret.
/// </summary>
internal static class SecretStoreProvider
{
    private const string DisableSecretStoreEnvVar = "BARESYNC_DISABLE_SECRET_STORE";
    private const string AppSettingsPathOverrideEnvVar = "BARESYNC_APPSETTINGS_PATH";
    private const string StoreNamespacePrefix = "baresync";
    private static readonly ISecretStoreBackend Backend = CreateBackend();

    public static bool IsAvailable => Backend.IsAvailable;

    public static string GetEncryptionPasswordSlot(string encryptedOutputRoot)
    {
        return BatchSecretSlot.GetSecretSlot(
            BatchOperationCatalog.OperationTypeCreateEncryptedFolder,
            encryptedOutputRoot);
    }

    public static bool TryLoadSecret(string slotKey, out string secret)
    {
        secret = string.Empty;
        if (string.IsNullOrWhiteSpace(slotKey))
        {
            return false;
        }

        var namespacedSlotKey = BuildNamespacedSlotKey(slotKey);
        return Backend.TryGet(namespacedSlotKey, out secret);
    }

    public static bool TrySaveSecret(string slotKey, string secret)
    {
        if (string.IsNullOrWhiteSpace(slotKey) || string.IsNullOrWhiteSpace(secret))
        {
            return false;
        }

        var namespacedSlotKey = BuildNamespacedSlotKey(slotKey);
        return Backend.TrySet(namespacedSlotKey, secret);
    }

    public static void WriteUnavailableWarning()
    {
        if (IsAvailable)
        {
            return;
        }

        Bare.Primitive.UI.UiConsole.WriteLine("Warning: no persistent secret store is configured.");
        Bare.Primitive.UI.UiConsole.WriteLine("Passwords must be entered at each run.");
        Bare.Primitive.UI.UiConsole.WriteLine();
    }

    private static string BuildNamespacedSlotKey(string slotKey)
    {
        var storeNamespace = ResolveStoreNamespace();
        var namespaceHash = ComputeSha256Hex(storeNamespace);
        var slotHash = ComputeSha256Hex(slotKey.Trim());
        return $"{StoreNamespacePrefix}:{namespaceHash}:{slotHash}";
    }

    private static string ResolveStoreNamespace()
    {
        var overridePath = Environment.GetEnvironmentVariable(AppSettingsPathOverrideEnvVar);
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            return NormalizeNamespacePath(overridePath);
        }

        return NormalizeNamespacePath(Path.Combine(AppContext.BaseDirectory, "appsettings.json"));
    }

    private static string NormalizeNamespacePath(string path)
    {
        string normalized;
        try
        {
            normalized = Path.GetFullPath(path.Trim());
        }
        catch
        {
            normalized = path.Trim();
        }

        if (OperatingSystem.IsWindows())
        {
            normalized = normalized.ToUpperInvariant();
        }

        return normalized;
    }

    private static string ComputeSha256Hex(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }

    private static ISecretStoreBackend CreateBackend()
    {
        if (IsSecretStoreDisabledByEnv())
        {
            return new NoSecretStoreBackend();
        }

        if (OperatingSystem.IsWindows())
        {
            return new WindowsCredentialManagerBackend();
        }

        if (OperatingSystem.IsLinux())
        {
            return new LinuxSecretToolBackend();
        }

        return new NoSecretStoreBackend();
    }

    private static bool IsSecretStoreDisabledByEnv()
    {
        var value = Environment.GetEnvironmentVariable(DisableSecretStoreEnvVar);
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim();
        return normalized.Equals("1", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("true", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }

    private interface ISecretStoreBackend
    {
        bool IsAvailable { get; }

        bool TryGet(string slotKey, out string secret);

        bool TrySet(string slotKey, string secret);
    }

    private sealed class NoSecretStoreBackend : ISecretStoreBackend
    {
        public bool IsAvailable => false;

        public bool TryGet(string slotKey, out string secret)
        {
            secret = string.Empty;
            return false;
        }

        public bool TrySet(string slotKey, string secret)
        {
            return false;
        }
    }

    private sealed class WindowsCredentialManagerBackend : ISecretStoreBackend
    {
        private const uint CredTypeGeneric = 1;
        private const uint CredPersistLocalMachine = 2;

        public bool IsAvailable => OperatingSystem.IsWindows();

        public bool TryGet(string slotKey, out string secret)
        {
            secret = string.Empty;
            if (!IsAvailable || string.IsNullOrWhiteSpace(slotKey))
            {
                return false;
            }

            if (!CredReadW(slotKey, CredTypeGeneric, 0, out var credentialPtr) || credentialPtr == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                var credential = Marshal.PtrToStructure<CREDENTIAL>(credentialPtr);
                if (credential.CredentialBlobSize == 0 || credential.CredentialBlob == IntPtr.Zero)
                {
                    return false;
                }

                var secretBytes = new byte[credential.CredentialBlobSize];
                Marshal.Copy(credential.CredentialBlob, secretBytes, 0, secretBytes.Length);
                secret = Encoding.UTF8.GetString(secretBytes);
                Array.Clear(secretBytes, 0, secretBytes.Length);
                return !string.IsNullOrWhiteSpace(secret);
            }
            catch
            {
                return false;
            }
            finally
            {
                CredFree(credentialPtr);
            }
        }

        public bool TrySet(string slotKey, string secret)
        {
            if (!IsAvailable || string.IsNullOrWhiteSpace(slotKey) || string.IsNullOrWhiteSpace(secret))
            {
                return false;
            }

            var secretBytes = Encoding.UTF8.GetBytes(secret);
            if (secretBytes.Length == 0)
            {
                return false;
            }

            var blobPtr = IntPtr.Zero;
            try
            {
                blobPtr = Marshal.AllocCoTaskMem(secretBytes.Length);
                Marshal.Copy(secretBytes, 0, blobPtr, secretBytes.Length);

                var credential = new CREDENTIAL
                {
                    Type = CredTypeGeneric,
                    TargetName = slotKey,
                    CredentialBlobSize = (uint)secretBytes.Length,
                    CredentialBlob = blobPtr,
                    Persist = CredPersistLocalMachine,
                    UserName = Environment.UserName
                };

                return CredWriteW(ref credential, 0);
            }
            catch
            {
                return false;
            }
            finally
            {
                Array.Clear(secretBytes, 0, secretBytes.Length);
                if (blobPtr != IntPtr.Zero)
                {
                    Marshal.FreeCoTaskMem(blobPtr);
                }
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct CREDENTIAL
        {
            public uint Flags;
            public uint Type;
            public string TargetName;
            public string Comment;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
            public uint CredentialBlobSize;
            public IntPtr CredentialBlob;
            public uint Persist;
            public uint AttributeCount;
            public IntPtr Attributes;
            public string TargetAlias;
            public string UserName;
        }

        [DllImport("Advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredReadW(string target, uint type, int reservedFlag, out IntPtr credentialPtr);

        [DllImport("Advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredWriteW([In] ref CREDENTIAL userCredential, [In] uint flags);

        [DllImport("Advapi32.dll", EntryPoint = "CredFree", SetLastError = true)]
        private static extern void CredFree([In] IntPtr cred);
    }

    private sealed class LinuxSecretToolBackend : ISecretStoreBackend
    {
        private const int ProcessTimeoutMs = 10000;
        private readonly bool _isAvailable;

        public LinuxSecretToolBackend()
        {
            _isAvailable = DetectAvailability();
        }

        public bool IsAvailable => _isAvailable;

        public bool TryGet(string slotKey, out string secret)
        {
            secret = string.Empty;
            if (!IsAvailable || string.IsNullOrWhiteSpace(slotKey))
            {
                return false;
            }

            var args = new[] { "lookup", "service", "baresync", "slot", slotKey };
            if (!TryRunSecretTool(args, stdinText: null, out var stdout, out _))
            {
                return false;
            }

            secret = stdout.TrimEnd('\r', '\n');
            return !string.IsNullOrWhiteSpace(secret);
        }

        public bool TrySet(string slotKey, string secret)
        {
            if (!IsAvailable || string.IsNullOrWhiteSpace(slotKey) || string.IsNullOrWhiteSpace(secret))
            {
                return false;
            }

            var args = new[] { "store", "--label", "BareSync", "service", "baresync", "slot", slotKey };
            return TryRunSecretTool(args, stdinText: secret + Environment.NewLine, out _, out _);
        }

        private static bool DetectAvailability()
        {
            if (!OperatingSystem.IsLinux())
            {
                return false;
            }

            var args = new[] { "--version" };
            return TryRunSecretTool(args, stdinText: null, out _, out _);
        }

        private static bool TryRunSecretTool(
            IReadOnlyList<string> args,
            string? stdinText,
            out string stdout,
            out string stderr)
        {
            stdout = string.Empty;
            stderr = string.Empty;

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "secret-tool",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    RedirectStandardInput = stdinText is not null,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                foreach (var arg in args)
                {
                    psi.ArgumentList.Add(arg);
                }

                using var process = new Process { StartInfo = psi };
                if (!process.Start())
                {
                    return false;
                }

                if (stdinText is not null)
                {
                    process.StandardInput.Write(stdinText);
                    process.StandardInput.Close();
                }

                var outputTask = process.StandardOutput.ReadToEndAsync();
                var errorTask = process.StandardError.ReadToEndAsync();

                if (!process.WaitForExit(ProcessTimeoutMs))
                {
                    try
                    {
                        process.Kill(entireProcessTree: true);
                    }
                    catch
                    {
                    }

                    return false;
                }

                stdout = outputTask.GetAwaiter().GetResult();
                stderr = errorTask.GetAwaiter().GetResult();
                return process.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }
    }
}
