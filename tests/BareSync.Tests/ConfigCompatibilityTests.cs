using System.Text.Json;
using BareSync.Domain;
using BareSync.Infra;
using Xunit;

namespace BareSync.Tests;

public sealed class ConfigCompatibilityTests
{
    [Fact]
    public async Task LoadOrCreate_MapsLegacyOutputCsvToSourceIndex()
    {
        using var sourceTemp = new TempDirectory();
        using var mirrorTemp = new TempDirectory();
        var legacyIndexPath = Path.Combine(sourceTemp.RootPath, "legacy_index.csv");
        var appDir = AppContext.BaseDirectory;

        var legacyConfig = new
        {
            SourceRoot = sourceTemp.RootPath,
            MirrorRoot = mirrorTemp.RootPath,
            OutputCsvFileName = legacyIndexPath
        };
        var json = JsonSerializer.Serialize(legacyConfig, new JsonSerializerOptions { WriteIndented = true });

        await AppSettingsIsolation.WithIsolatedAppSettingsAsync(
            appDir,
            json,
            () =>
            {
                var config = ConfigService.LoadOrCreate(out var created);

                Assert.False(created);
                Assert.Equal(legacyIndexPath, config.SourceIndexCsvPath);
                Assert.Equal(
                    Path.Combine(mirrorTemp.RootPath, AppConfig.DefaultDestIndexCsvFileName),
                    config.DestIndexCsvPath);
                return Task.CompletedTask;
            });
    }

    [Fact]
    public async Task LoadOrCreate_AddsDefaultsForNewSettings()
    {
        using var sourceTemp = new TempDirectory();
        using var mirrorTemp = new TempDirectory();
        var appDir = AppContext.BaseDirectory;

        var legacyConfig = new
        {
            SourceRoot = sourceTemp.RootPath,
            MirrorRoot = mirrorTemp.RootPath,
            SourceIndexCsvPath = Path.Combine(sourceTemp.RootPath, "source.csv"),
            DestIndexCsvPath = Path.Combine(mirrorTemp.RootPath, "dest.csv")
        };
        var json = JsonSerializer.Serialize(legacyConfig, new JsonSerializerOptions { WriteIndented = true });

        await AppSettingsIsolation.WithIsolatedAppSettingsAsync(
            appDir,
            json,
            () =>
            {
                var config = ConfigService.LoadOrCreate(out var created);

                Assert.False(created);
                Assert.Equal(string.Empty, config.EncryptedOutputRoot);
                Assert.Equal(RestoreSmartMode.Smart, config.RestoreSmartMode);
                Assert.False(config.Mirror);
                Assert.False(config.LogDebug);
                Assert.Equal(
                    AppConfig.CreateDefaultExcludeFileNames(),
                    config.ExcludeFileNames,
                    StringComparer.OrdinalIgnoreCase);
                Assert.Equal(
                    AppConfig.CreateDefaultExcludeDirectoryNames(),
                    config.ExcludeDirectoryNames,
                    StringComparer.OrdinalIgnoreCase);
                Assert.Empty(config.ExcludePathGlobs);
                Assert.Equal(250, config.IndexCheckpointEveryFiles);
                Assert.Equal(1500, config.IndexCheckpointMinIntervalMs);
                Assert.Equal(0, config.IndexIoCooldownMs);
                Assert.Equal(1500, config.IndexInterStageCooldownMs);
                Assert.True(config.IndexForceGcBetweenStages);
                return Task.CompletedTask;
            });
    }

    [Fact]
    public async Task Save_PersistsNewSettings()
    {
        using var sourceTemp = new TempDirectory();
        using var mirrorTemp = new TempDirectory();
        var appDir = AppContext.BaseDirectory;

        await AppSettingsIsolation.WithIsolatedAppSettingsAsync(
            appDir,
            newJson: null,
            () =>
            {
                var config = new AppConfig
                {
                    SourceRoot = sourceTemp.RootPath,
                    MirrorRoot = mirrorTemp.RootPath,
                    Mirror = true,
                    SourceIndexCsvPath = Path.Combine(sourceTemp.RootPath, "source.csv"),
                    DestIndexCsvPath = Path.Combine(mirrorTemp.RootPath, "dest.csv"),
                    EncryptedOutputRoot = Path.Combine(mirrorTemp.RootPath, "encrypted"),
                    RestoreSmartMode = RestoreSmartMode.FastSmart,
                    LogDebug = true,
                    ExcludeFileNames = ["Thumbs.db", "desktop.ini", "Custom.db"],
                    ExcludeDirectoryNames = ["$RECYCLE.BIN", "System Volume Information", "tmp-cache"],
                    ExcludePathGlobs = ["*.tmp", "cache/**"],
                    IndexCheckpointEveryFiles = 42,
                    IndexCheckpointMinIntervalMs = 333,
                    IndexIoCooldownMs = 12,
                    IndexInterStageCooldownMs = 987,
                    IndexForceGcBetweenStages = false
                };

                Assert.True(ConfigService.Save(config));

                var reloaded = ConfigService.LoadOrCreate(out var created);

                Assert.False(created);
                Assert.Equal(config.Mirror, reloaded.Mirror);
                Assert.Equal(config.EncryptedOutputRoot, reloaded.EncryptedOutputRoot);
                Assert.Equal(config.RestoreSmartMode, reloaded.RestoreSmartMode);
                Assert.Equal(config.LogDebug, reloaded.LogDebug);
                Assert.Equal(config.ExcludeFileNames, reloaded.ExcludeFileNames, StringComparer.OrdinalIgnoreCase);
                Assert.Equal(config.ExcludeDirectoryNames, reloaded.ExcludeDirectoryNames, StringComparer.OrdinalIgnoreCase);
                Assert.Equal(config.ExcludePathGlobs, reloaded.ExcludePathGlobs, StringComparer.OrdinalIgnoreCase);
                Assert.Equal(config.IndexCheckpointEveryFiles, reloaded.IndexCheckpointEveryFiles);
                Assert.Equal(config.IndexCheckpointMinIntervalMs, reloaded.IndexCheckpointMinIntervalMs);
                Assert.Equal(config.IndexIoCooldownMs, reloaded.IndexIoCooldownMs);
                Assert.Equal(config.IndexInterStageCooldownMs, reloaded.IndexInterStageCooldownMs);
                Assert.Equal(config.IndexForceGcBetweenStages, reloaded.IndexForceGcBetweenStages);
                return Task.CompletedTask;
            });
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            RootPath = Path.Combine(Path.GetTempPath(), "BareSyncTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(RootPath);
        }

        public string RootPath { get; }

        public void Dispose()
        {
            if (!Directory.Exists(RootPath))
            {
                return;
            }

            try
            {
                Directory.Delete(RootPath, recursive: true);
            }
            catch
            {
            }
        }
    }
}
