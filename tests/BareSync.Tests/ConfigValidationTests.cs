using BareSync.Domain;
using BareSync.Infra;
using Xunit;

namespace BareSync.Tests;

public sealed class ConfigValidationTests
{
    [Fact]
    public void Validate_ReturnsMissingFields()
    {
        var config = new AppConfig
        {
            SourceRoot = string.Empty,
            MirrorRoot = string.Empty,
            SourceIndexCsvPath = string.Empty,
            DestIndexCsvPath = string.Empty
        };

        var errors = ConfigService.Validate(config);

        Assert.Equal(4, errors.Count);
        Assert.Contains(errors, error => error.Field == "SourceRoot");
        Assert.Contains(errors, error => error.Field == "MirrorRoot");
        Assert.Contains(errors, error => error.Field == "SourceIndexCsvPath");
        Assert.Contains(errors, error => error.Field == "DestIndexCsvPath");
    }

    [Fact]
    public void Validate_RejectsInvalidIndexStabilitySettings()
    {
        using var sourceTemp = new TempDirectory();
        using var destTemp = new TempDirectory();

        var config = new AppConfig
        {
            SourceRoot = sourceTemp.RootPath,
            MirrorRoot = destTemp.RootPath,
            SourceIndexCsvPath = Path.Combine(sourceTemp.RootPath, "source.csv"),
            DestIndexCsvPath = Path.Combine(destTemp.RootPath, "dest.csv"),
            IndexCheckpointEveryFiles = 0,
            IndexCheckpointMinIntervalMs = -1,
            IndexIoCooldownMs = -1,
            IndexInterStageCooldownMs = -1
        };

        var errors = ConfigService.Validate(config);

        Assert.Contains(errors, error => error.Field == "IndexCheckpointEveryFiles");
        Assert.Contains(errors, error => error.Field == "IndexCheckpointMinIntervalMs");
        Assert.Contains(errors, error => error.Field == "IndexIoCooldownMs");
        Assert.Contains(errors, error => error.Field == "IndexInterStageCooldownMs");
    }

    [Fact]
    public void Validate_FailsWhenRequiredFieldIsNull()
    {
        using var sourceTemp = new TempDirectory();
        using var destTemp = new TempDirectory();
        var config = new AppConfig
        {
            SourceRoot = null!,
            MirrorRoot = destTemp.RootPath,
            SourceIndexCsvPath = Path.Combine(sourceTemp.RootPath, "source_index.csv"),
            DestIndexCsvPath = Path.Combine(destTemp.RootPath, "dest_index.csv")
        };

        var errors = ConfigService.Validate(config);

        var error = Assert.Single(errors);
        Assert.Equal("SourceRoot", error.Field);
    }

    [Fact]
    public void Validate_AppliesDefaultsWhenRootsValid()
    {
        using var sourceTemp = new TempDirectory();
        using var destTemp = new TempDirectory();
        var config = new AppConfig
        {
            SourceRoot = sourceTemp.RootPath,
            MirrorRoot = destTemp.RootPath,
            SourceIndexCsvPath = string.Empty,
            DestIndexCsvPath = string.Empty
        };

        var errors = ConfigService.Validate(config);

        Assert.Empty(errors);
        Assert.Equal(
            Path.Combine(sourceTemp.RootPath, AppConfig.DefaultSourceIndexCsvFileName),
            config.SourceIndexCsvPath);
        Assert.Equal(
            Path.Combine(destTemp.RootPath, AppConfig.DefaultDestIndexCsvFileName),
            config.DestIndexCsvPath);
    }

    [Fact]
    public void Validate_AllowsValidIndexCsvFullPaths()
    {
        using var sourceTemp = new TempDirectory();
        using var destTemp = new TempDirectory();
        var config = new AppConfig
        {
            SourceRoot = sourceTemp.RootPath,
            MirrorRoot = destTemp.RootPath,
            SourceIndexCsvPath = Path.Combine(sourceTemp.RootPath, "source_index.csv"),
            DestIndexCsvPath = Path.Combine(destTemp.RootPath, "dest_index.csv")
        };

        var errors = ConfigService.Validate(config);

        Assert.Empty(errors);
    }

    [Theory]
    [MemberData(nameof(OptionalEncryptedSettings))]
    public void Validate_AllowsMissingOptionalEncryptedSettings(string? encryptedOutputRoot)
    {
        using var sourceTemp = new TempDirectory();
        using var destTemp = new TempDirectory();
        var config = new AppConfig
        {
            SourceRoot = sourceTemp.RootPath,
            MirrorRoot = destTemp.RootPath,
            SourceIndexCsvPath = Path.Combine(sourceTemp.RootPath, "source_index.csv"),
            DestIndexCsvPath = Path.Combine(destTemp.RootPath, "dest_index.csv"),
            EncryptedOutputRoot = encryptedOutputRoot!
        };

        var errors = ConfigService.Validate(config);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_RejectsSourceIndexCsvPathWithMissingDirectory()
    {
        using var sourceTemp = new TempDirectory();
        using var destTemp = new TempDirectory();
        var config = new AppConfig
        {
            SourceRoot = sourceTemp.RootPath,
            MirrorRoot = destTemp.RootPath,
            SourceIndexCsvPath = Path.Combine(sourceTemp.RootPath, "missing", "source.csv"),
            DestIndexCsvPath = Path.Combine(destTemp.RootPath, "dest.csv")
        };

        var errors = ConfigService.Validate(config);

        Assert.Contains(errors, error => error.Field == "SourceIndexCsvPath");
    }

    [Fact]
    public void Validate_FailsWhenSourceRootMissing()
    {
        using var sourceTemp = new TempDirectory();
        using var destTemp = new TempDirectory();
        var config = new AppConfig
        {
            SourceRoot = string.Empty,
            MirrorRoot = destTemp.RootPath,
            SourceIndexCsvPath = Path.Combine(sourceTemp.RootPath, "source_index.csv"),
            DestIndexCsvPath = Path.Combine(destTemp.RootPath, "dest_index.csv")
        };

        var errors = ConfigService.Validate(config);

        Assert.Contains(errors, error => error.Field == "SourceRoot");
    }

    [Fact]
    public void Validate_RejectsDestIndexCsvPathWithMissingDirectory()
    {
        using var sourceTemp = new TempDirectory();
        using var destTemp = new TempDirectory();
        var config = new AppConfig
        {
            SourceRoot = sourceTemp.RootPath,
            MirrorRoot = destTemp.RootPath,
            SourceIndexCsvPath = Path.Combine(sourceTemp.RootPath, "source.csv"),
            DestIndexCsvPath = Path.Combine(destTemp.RootPath, "missing", "dest.csv")
        };

        var errors = ConfigService.Validate(config);

        Assert.Contains(errors, error => error.Field == "DestIndexCsvPath");
    }

    [Fact]
    public void Validate_ResolvesDriveLabelPaths_WhenUniqueMatch()
    {
        using var sourceTemp = new TempDirectory();
        using var destTemp = new TempDirectory();

        var config = new AppConfig
        {
            SourceRoot = @"SourceVolume\Data",
            MirrorRoot = @"MirrorVolume\Mirror",
            SourceIndexCsvPath = @"SourceVolume\Data\source.csv",
            DestIndexCsvPath = @"MirrorVolume\Mirror\dest.csv"
        };

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [@"SourceVolume\Data"] = Path.Combine(sourceTemp.RootPath, "Data"),
            [@"MirrorVolume\Mirror"] = Path.Combine(destTemp.RootPath, "Mirror"),
            [@"SourceVolume\Data\source.csv"] = Path.Combine(sourceTemp.RootPath, "Data", "source.csv"),
            [@"MirrorVolume\Mirror\dest.csv"] = Path.Combine(destTemp.RootPath, "Mirror", "dest.csv")
        };

        Directory.CreateDirectory(map[@"SourceVolume\Data"]);
        Directory.CreateDirectory(map[@"MirrorVolume\Mirror"]);

        var errors = ConfigService.Validate(config, path =>
        {
            return map.TryGetValue(path, out var resolved)
                ? DriveLabelResolutionResult.Resolved(resolved)
                : DriveLabelResolutionResult.NotApplicable(path);
        });

        Assert.Empty(errors);
        Assert.Equal(map[@"SourceVolume\Data"], config.SourceRoot);
        Assert.Equal(map[@"MirrorVolume\Mirror"], config.MirrorRoot);
        Assert.Equal(map[@"SourceVolume\Data\source.csv"], config.SourceIndexCsvPath);
        Assert.Equal(map[@"MirrorVolume\Mirror\dest.csv"], config.DestIndexCsvPath);
    }

    [Fact]
    public void Validate_ReturnsError_WhenDriveLabelIsAmbiguous()
    {
        using var sourceTemp = new TempDirectory();
        using var destTemp = new TempDirectory();
        var config = new AppConfig
        {
            SourceRoot = @"Data\Source",
            MirrorRoot = destTemp.RootPath,
            SourceIndexCsvPath = Path.Combine(sourceTemp.RootPath, "source.csv"),
            DestIndexCsvPath = Path.Combine(destTemp.RootPath, "dest.csv")
        };

        var errors = ConfigService.Validate(config, path =>
        {
            return string.Equals(path, @"Data\Source", StringComparison.OrdinalIgnoreCase)
                ? DriveLabelResolutionResult.Ambiguous(path, new[] { @"D:\\", @"E:\\" })
                : DriveLabelResolutionResult.NotApplicable(path);
        });

        Assert.Contains(errors, error =>
            error.Field == "SourceRoot"
            && error.Message.Contains("ambiguous", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [MemberData(nameof(InvalidFileNames))]
    public void Validate_RejectsInvalidSourceIndexCsvPath(string fileName)
    {
        using var sourceTemp = new TempDirectory();
        using var destTemp = new TempDirectory();
        var config = new AppConfig
        {
            SourceRoot = sourceTemp.RootPath,
            MirrorRoot = destTemp.RootPath,
            SourceIndexCsvPath = fileName,
            DestIndexCsvPath = Path.Combine(destTemp.RootPath, "dest.csv")
        };

        var errors = ConfigService.Validate(config);

        Assert.Contains(errors, error => error.Field == "SourceIndexCsvPath");
    }

    [Theory]
    [MemberData(nameof(InvalidFileNames))]
    public void Validate_RejectsInvalidDestIndexCsvPath(string fileName)
    {
        using var sourceTemp = new TempDirectory();
        using var destTemp = new TempDirectory();
        var config = new AppConfig
        {
            SourceRoot = sourceTemp.RootPath,
            MirrorRoot = destTemp.RootPath,
            SourceIndexCsvPath = Path.Combine(sourceTemp.RootPath, "source.csv"),
            DestIndexCsvPath = fileName
        };

        var errors = ConfigService.Validate(config);

        Assert.Contains(errors, error => error.Field == "DestIndexCsvPath");
    }

    public static IEnumerable<object[]> InvalidFileNames()
    {
        yield return new object[] { "file..csv" };
        yield return new object[] { "index.txt" };
        yield return new object[] { $"dir{Path.DirectorySeparatorChar}file.csv" };
        if (Path.AltDirectorySeparatorChar != Path.DirectorySeparatorChar)
        {
            yield return new object[] { $"dir{Path.AltDirectorySeparatorChar}file.csv" };
        }
    }

    public static IEnumerable<object?[]> OptionalEncryptedSettings()
    {
        yield return new object?[] { string.Empty };
        yield return new object?[] { null };
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            RootPath = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "BareSyncTests_" + Guid.NewGuid().ToString("N"));
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
