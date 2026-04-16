using System.Text.Json;
using Bare.Primitive.UI;
using BareSync.App.BatchMode.Screens;
using BareSync.Domain;

namespace BareSync.Tests;

public sealed class SecretPromptScreenTests
{
    [Fact]
    public void Show_WhenRedirectedAndPasswordProvided_ReturnsSecretMap()
    {
        using var temp = new TempDirectory();
        var scope = $"scope-{Guid.NewGuid():N}";
        var descriptor = CreateDescriptor(temp.Path, scope);
        var config = new AppConfig { EncryptedOutputRoot = scope };

        var sut = new SecretPromptScreen();
        var result = sut.Show(
            descriptor,
            config,
            uiInput: new ScriptedUiInput(new[] { "secret-123" }),
            keyInput: new ScriptedUiKeyInput(Array.Empty<ConsoleKeyInfo>()),
            isInputRedirected: () => true,
            write: _ => { },
            writeLine: _ => { });

        var expectedSlot = BatchSecretSlot.GetSecretSlot(
            BatchOperationCatalog.OperationTypeCreateEncryptedFolder,
            scope);

        Assert.NotNull(result);
        Assert.True(result!.TryGetValue(expectedSlot, out var value));
        Assert.Equal("secret-123", value);
    }

    [Fact]
    public void Show_WhenRedirectedAndPasswordEmpty_ReturnsNull()
    {
        using var temp = new TempDirectory();
        var scope = $"scope-{Guid.NewGuid():N}";
        var descriptor = CreateDescriptor(temp.Path, scope);
        var config = new AppConfig { EncryptedOutputRoot = scope };

        var sut = new SecretPromptScreen();
        var result = sut.Show(
            descriptor,
            config,
            uiInput: new ScriptedUiInput(new[] { "" }),
            keyInput: new ScriptedUiKeyInput(Array.Empty<ConsoleKeyInfo>()),
            isInputRedirected: () => true,
            write: _ => { },
            writeLine: _ => { });

        Assert.Null(result);
    }

    [Fact]
    public void Show_WhenNotRedirectedAndEscapePressed_ReturnsNull()
    {
        using var temp = new TempDirectory();
        var scope = $"scope-{Guid.NewGuid():N}";
        var descriptor = CreateDescriptor(temp.Path, scope);
        var config = new AppConfig { EncryptedOutputRoot = scope };

        var sut = new SecretPromptScreen();
        var result = sut.Show(
            descriptor,
            config,
            uiInput: new ScriptedUiInput(Array.Empty<string?>()),
            keyInput: new ScriptedUiKeyInput(new[]
            {
                new ConsoleKeyInfo('\u001b', ConsoleKey.Escape, false, false, false)
            }),
            isInputRedirected: () => false,
            write: _ => { },
            writeLine: _ => { });

        Assert.Null(result);
    }

    private static BatchStorageDescriptor CreateDescriptor(string rootPath, string scope)
    {
        var batchPath = System.IO.Path.Combine(rootPath, "secret-batch.json");
        var payload = new
        {
            schemaVersion = 0,
            id = Guid.NewGuid().ToString("D"),
            name = "SecretBatch",
            createdUtc = "2026-02-18T00:00:00Z",
            updatedUtc = "2026-02-18T00:00:00Z",
            contextSnapshot = new
            {
                sourceRoot = "D:/Data/Source",
                sourceIndexCsvPath = "D:/Data/Indexes/source.csv",
                encryptedOutputRoot = scope
            },
            steps = new[]
            {
                new
                {
                    operationType = BatchOperationCatalog.OperationTypeCreateEncryptedFolder,
                    operationParams = new { values = new { } },
                    contextOverrides = new { }
                }
            }
        };

        File.WriteAllText(batchPath, JsonSerializer.Serialize(payload));
        return new BatchStorageDescriptor(
            Id: "secret-batch",
            Name: "SecretBatch",
            Status: BatchStorageStatus.Valid,
            Reason: string.Empty,
            Path: batchPath);
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "BareSync.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch
            {
            }
        }
    }
}