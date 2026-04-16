using BareSync.App.Common;
using BareSync.Domain;
using BareSync.UI;

namespace BareSync.App;

internal sealed class ExtractCommandLineRunner
{
    private const string EncryptedIndexFileName = ".baresync.encindex.bse";
    private const string EncryptedDataFileExtension = ".bse";

    private readonly EncryptedFolderService _encryptedService;

    public ExtractCommandLineRunner(EncryptedFolderService? encryptedService = null)
    {
        _encryptedService = encryptedService ?? new EncryptedFolderService();
    }

    public async Task<int> RunAsync(string sourcePathArgument, CancellationToken ct = default)
    {
        if (!TryResolveSourcePath(sourcePathArgument, out var sourcePath, out var sourceIsFile, out var sourceIsDirectory, out var error))
        {
            Bare.Primitive.UI.UiConsole.WriteLine(error);
            return 1;
        }

        if (sourceIsDirectory)
        {
            return await RunDirectoryExtractionAsync(sourcePath, ct).ConfigureAwait(false);
        }

        return await RunFileExtractionAsync(sourcePath, ct).ConfigureAwait(false);
    }

    private async Task<int> RunDirectoryExtractionAsync(string sourceDirectory, CancellationToken ct)
    {
        var indexArchivePath = Path.Combine(sourceDirectory, EncryptedIndexFileName);
        var hasEncryptedIndex = File.Exists(indexArchivePath);

        var representativeArchive = hasEncryptedIndex
            ? indexArchivePath
            : Directory.EnumerateFiles(sourceDirectory, $"*{EncryptedDataFileExtension}", SearchOption.AllDirectories)
                .FirstOrDefault(path => EncryptedFolderService.IsNativeBseArchive(path));

        if (string.IsNullOrWhiteSpace(representativeArchive))
        {
            Bare.Primitive.UI.UiConsole.WriteLine("No native .bse archive found in source folder.");
            return 1;
        }

        var passwordResolution = await ResolvePasswordAsync(sourceDirectory, representativeArchive, ct).ConfigureAwait(false);
        if (!passwordResolution.Success)
        {
            return 1;
        }

        var destinationRoot = PromptFolderDestination(sourceDirectory);
        if (string.IsNullOrWhiteSpace(destinationRoot))
        {
            Bare.Primitive.UI.UiConsole.WriteLine("Extraction cancelled.");
            return 1;
        }

        if (hasEncryptedIndex)
        {
            var restoreConfig = new AppConfig
            {
                EncryptedOutputRoot = sourceDirectory,
                RestoreRoot = destinationRoot,
                Mirror = false
            };

            var result = await _encryptedService.RestoreEncryptedFilesAsync(
                    restoreConfig,
                    passwordResolution.Password,
                    progress: new Progress<ProgressInfo>(),
                    ct)
                .ConfigureAwait(false);

            Bare.Primitive.UI.UiConsole.WriteLine(result.StatusLine);
            return result.SuccessOrWarningFlag ? 0 : 1;
        }

        Bare.Primitive.UI.UiConsole.WriteLine("Warning: encrypted index not found. Original names cannot be restored.");

        var archives = Directory
            .EnumerateFiles(sourceDirectory, $"*{EncryptedDataFileExtension}", SearchOption.AllDirectories)
            .Where(EncryptedFolderService.IsNativeBseArchive)
            .OrderBy(path => path, OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
            .ToList();

        var extractedCount = 0;
        foreach (var archivePath in archives)
        {
            ct.ThrowIfCancellationRequested();

            var relativeArchivePath = Path.GetRelativePath(sourceDirectory, archivePath);
            var relativeTargetPath = relativeArchivePath.EndsWith(EncryptedDataFileExtension, StringComparison.OrdinalIgnoreCase)
                ? relativeArchivePath[..^EncryptedDataFileExtension.Length]
                : relativeArchivePath;
            var destinationFilePath = Path.Combine(destinationRoot, relativeTargetPath);

            var extractResult = await _encryptedService.ExtractSingleEncryptedArchiveAsync(
                    archivePath,
                    passwordResolution.Password,
                    destinationFilePath,
                    expectedCrc64Hex: null,
                    ct)
                .ConfigureAwait(false);

            if (!extractResult.SuccessOrWarningFlag)
            {
                Bare.Primitive.UI.UiConsole.WriteLine(extractResult.StatusLine);
                return 1;
            }

            extractedCount++;
        }

        Bare.Primitive.UI.UiConsole.WriteLine($"Extracted {extractedCount} file(s) to: {destinationRoot}");
        return 0;
    }

    private async Task<int> RunFileExtractionAsync(string sourceFile, CancellationToken ct)
    {
        if (!sourceFile.EndsWith(EncryptedDataFileExtension, StringComparison.OrdinalIgnoreCase)
            || !EncryptedFolderService.IsNativeBseArchive(sourceFile))
        {
            Bare.Primitive.UI.UiConsole.WriteLine("Unsupported extract source: expected a native .bse archive file.");
            return 1;
        }

        var encryptedRoot = FindEncryptedRoot(sourceFile);
        var passwordResolution = await ResolvePasswordAsync(encryptedRoot, sourceFile, ct).ConfigureAwait(false);
        if (!passwordResolution.Success)
        {
            return 1;
        }

        EncryptedFolderService.EncryptedIndexEntry? resolvedEntry = null;
        var indexPath = Path.Combine(encryptedRoot, EncryptedIndexFileName);
        if (File.Exists(indexPath))
        {
            var resolve = await _encryptedService.TryResolveEntryForArchiveAsync(
                    encryptedRoot,
                    sourceFile,
                    passwordResolution.Password,
                    ct)
                .ConfigureAwait(false);

            resolvedEntry = resolve.Entry;
            if (resolve.Error is not null)
            {
                Bare.Primitive.UI.UiConsole.WriteLine($"Warning: {resolve.Error}");
            }
        }

        var resolvedFileName = ResolveExtractedFileName(sourceFile, resolvedEntry);
        var suggestedSubfolder = ResolveSuggestedSubfolderName(sourceFile, resolvedFileName);
        if (resolvedEntry is not null)
        {
            Bare.Primitive.UI.UiConsole.WriteLine($"Resolved original file: {resolvedEntry.OriginalRelativePath}");
        }

        var destinationFilePath = PromptFileDestination(resolvedFileName, suggestedSubfolder);
        if (string.IsNullOrWhiteSpace(destinationFilePath))
        {
            Bare.Primitive.UI.UiConsole.WriteLine("Extraction cancelled.");
            return 1;
        }

        var result = await _encryptedService.ExtractSingleEncryptedArchiveAsync(
                sourceFile,
                passwordResolution.Password,
                destinationFilePath,
                resolvedEntry?.Crc64Hex,
                ct)
            .ConfigureAwait(false);

        Bare.Primitive.UI.UiConsole.WriteLine(result.StatusLine);
        return result.SuccessOrWarningFlag ? 0 : 1;
    }

    private async Task<(bool Success, string Password)> ResolvePasswordAsync(
        string secretScope,
        string representativeArchivePath,
        CancellationToken ct)
    {
        var slotKey = SecretStoreProvider.GetEncryptionPasswordSlot(secretScope);
        if (SecretStoreProvider.TryLoadSecret(slotKey, out var storedSecret))
        {
            var validationError = await _encryptedService
                .ValidateArchivePasswordAsync(representativeArchivePath, storedSecret, ct)
                .ConfigureAwait(false);

            if (validationError is null)
            {
                Bare.Primitive.UI.UiConsole.WriteLine("Using password from OS secret store.");
                return (true, storedSecret);
            }

            Bare.Primitive.UI.UiConsole.WriteLine("Stored vault secret could not be validated for this source.");
        }

        var noSecretValidationError = await _encryptedService
            .ValidateArchivePasswordAsync(representativeArchivePath, string.Empty, ct)
            .ConfigureAwait(false);
        if (noSecretValidationError is null)
        {
            Bare.Primitive.UI.UiConsole.WriteLine("Archive can be extracted without secret.");
            return (true, string.Empty);
        }

        SecretStoreProvider.WriteUnavailableWarning();

        while (true)
        {
            Bare.Primitive.UI.UiConsole.Write("Enter password (will not be echoed): ");
            var password = ConsoleInputHelpers.ReadPassword(maskInput: true);
            Bare.Primitive.UI.UiConsole.WriteLine();

            var validationError = await _encryptedService
                .ValidateArchivePasswordAsync(representativeArchivePath, password, ct)
                .ConfigureAwait(false);
            if (validationError is null)
            {
                if (SecretStoreProvider.IsAvailable
                    && !Bare.Primitive.UI.UiConsole.IsInputRedirected
                    && !string.IsNullOrWhiteSpace(password)
                    && ConsoleInputHelpers.ConfirmYesNo("Save password to OS secret store for this scope? (Y/N) "))
                {
                    if (!SecretStoreProvider.TrySaveSecret(slotKey, password))
                    {
                        Bare.Primitive.UI.UiConsole.WriteLine("Warning: failed to store password in OS secret store.");
                    }
                    else
                    {
                        Bare.Primitive.UI.UiConsole.WriteLine("Password saved to OS secret store.");
                    }
                }

                return (true, password);
            }

            Bare.Primitive.UI.UiConsole.WriteLine("Password validation failed (wrong password or corrupted archive).");
            if (!ConsoleInputHelpers.ConfirmYesNo("Try another password? (Y/N) "))
            {
                return (false, string.Empty);
            }
        }
    }

    private static string PromptFolderDestination(string sourceDirectory)
    {
        Bare.Primitive.UI.UiConsole.WriteLine("Folder extraction is recursive by default.");
        Bare.Primitive.UI.UiConsole.WriteLine("For folder sources, extraction currently runs directly into a sub-folder.");

        var folderName = new DirectoryInfo(sourceDirectory).Name;
        if (string.IsNullOrWhiteSpace(folderName))
        {
            folderName = "Extracted";
        }

        var defaultDestination = Path.Combine(Environment.CurrentDirectory, folderName);
        Bare.Primitive.UI.UiConsole.WriteLine($"Extract to (default): {defaultDestination}");
        if (ConsoleInputHelpers.ConfirmYesNo("Use default Extract to destination? (Y/N) "))
        {
            return defaultDestination;
        }

        Bare.Primitive.UI.UiConsole.Write("Enter destination folder path (empty to cancel): ");
        var customPath = Bare.Primitive.UI.UiConsole.ReadLine();
        if (string.IsNullOrWhiteSpace(customPath))
        {
            return string.Empty;
        }

        return Path.GetFullPath(customPath.Trim());
    }

    private static string PromptFileDestination(string resolvedFileName, string suggestedSubfolder)
    {
        var currentDir = Environment.CurrentDirectory;
        var defaultFolderPath = Path.Combine(currentDir, suggestedSubfolder);

        Bare.Primitive.UI.UiConsole.WriteLine($"Default Extract to: {defaultFolderPath}");
        if (ConsoleInputHelpers.ConfirmYesNo("Use default Extract to destination? (Y/N) "))
        {
            return Path.Combine(defaultFolderPath, resolvedFileName);
        }

        Bare.Primitive.UI.UiConsole.WriteLine();
        Bare.Primitive.UI.UiConsole.WriteLine("** Extract to **");
        Bare.Primitive.UI.UiConsole.WriteLine("1. Current folder");
        Bare.Primitive.UI.UiConsole.WriteLine($"2. Sub-folder '{suggestedSubfolder}'");
        Bare.Primitive.UI.UiConsole.WriteLine("3. Custom folder path");
        Bare.Primitive.UI.UiConsole.WriteLine("0. Cancel");
        Bare.Primitive.UI.UiConsole.Write("Select an option: ");

        var selection = UiInteraction.ReadMenuDigit(0, 3);
        return selection switch
        {
            0 => string.Empty,
            1 => Path.Combine(currentDir, resolvedFileName),
            2 => Path.Combine(defaultFolderPath, resolvedFileName),
            3 => ResolveCustomFileDestination(resolvedFileName),
            _ => string.Empty
        };
    }

    private static string ResolveCustomFileDestination(string resolvedFileName)
    {
        Bare.Primitive.UI.UiConsole.Write("Enter destination folder path (empty to cancel): ");
        var customPath = Bare.Primitive.UI.UiConsole.ReadLine();
        if (string.IsNullOrWhiteSpace(customPath))
        {
            return string.Empty;
        }

        var fullPath = Path.GetFullPath(customPath.Trim());
        return Path.Combine(fullPath, resolvedFileName);
    }

    private static bool TryResolveSourcePath(
        string sourcePathArgument,
        out string sourcePath,
        out bool sourceIsFile,
        out bool sourceIsDirectory,
        out string error)
    {
        sourcePath = string.Empty;
        sourceIsFile = false;
        sourceIsDirectory = false;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(sourcePathArgument))
        {
            error = "Invalid extract argument (empty path).";
            return false;
        }

        try
        {
            sourcePath = Path.GetFullPath(sourcePathArgument.Trim());
        }
        catch (Exception ex)
        {
            error = $"Invalid extract path: {ex.Message}";
            return false;
        }

        sourceIsFile = File.Exists(sourcePath);
        sourceIsDirectory = Directory.Exists(sourcePath);
        if (!sourceIsFile && !sourceIsDirectory)
        {
            error = $"Extract source not found: {sourcePath}";
            return false;
        }

        return true;
    }

    private static string FindEncryptedRoot(string sourceFile)
    {
        var currentDirectory = new FileInfo(sourceFile).Directory;
        while (currentDirectory is not null)
        {
            var candidateIndex = Path.Combine(currentDirectory.FullName, EncryptedIndexFileName);
            if (File.Exists(candidateIndex))
            {
                return currentDirectory.FullName;
            }

            currentDirectory = currentDirectory.Parent;
        }

        return Path.GetDirectoryName(sourceFile) ?? Environment.CurrentDirectory;
    }

    private static string ResolveExtractedFileName(
        string sourceFile,
        EncryptedFolderService.EncryptedIndexEntry? entry)
    {
        var fileName = entry is null
            ? Path.GetFileNameWithoutExtension(sourceFile)
            : Path.GetFileName(entry.OriginalRelativePath);

        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = "extracted.bin";
        }

        return fileName;
    }

    private static string ResolveSuggestedSubfolderName(string sourceFile, string extractedFileName)
    {
        var baseName = Path.GetFileNameWithoutExtension(extractedFileName);
        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = Path.GetFileNameWithoutExtension(sourceFile);
        }

        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = "Extracted";
        }

        return baseName;
    }
}
