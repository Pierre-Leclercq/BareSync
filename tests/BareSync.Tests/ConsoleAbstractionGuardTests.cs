using System.Text.RegularExpressions;

namespace BareSync.Tests;

public sealed class ConsoleAbstractionGuardTests
{
    private static readonly (string Label, Regex Pattern)[] ForbiddenLegacyTuiPatterns =
    {
        ("using BareSync.Tui", new Regex(@"^\s*using\s+BareSync\.Tui\s*;", RegexOptions.CultureInvariant)),
        ("ConsoleUi", new Regex(@"\bConsoleUi\b", RegexOptions.CultureInvariant)),
        ("ConsoleInput", new Regex(@"\bConsoleInput\b", RegexOptions.CultureInvariant)),
        ("UiMode", new Regex(@"\bUiMode\b", RegexOptions.CultureInvariant)),
        ("ConsolePathPicker", new Regex(@"\bConsolePathPicker\b", RegexOptions.CultureInvariant)),
        ("Pager<T>", new Regex(@"\bPager\s*<", RegexOptions.CultureInvariant))
    };

    [Fact]
    public void ApplicationSources_DoNotUseDirectConsoleCalls()
    {
        var sourceRoot = Path.Combine(ResolveBareSyncRepositoryRoot(), "src", "BareSync");
        Assert.True(Directory.Exists(sourceRoot), $"Source root not found: {sourceRoot}");

        var violations = new List<string>();
        var directConsolePattern = new Regex(@"\bConsole\.", RegexOptions.CultureInvariant);

        foreach (var file in Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories))
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                if (!directConsolePattern.IsMatch(lines[i]))
                {
                    continue;
                }

                var relative = Path.GetRelativePath(sourceRoot, file).Replace('\\', '/');
                violations.Add($"{relative}:{i + 1}:{lines[i].Trim()}");
            }
        }

        Assert.True(
            violations.Count == 0,
            "Direct Console calls found in BareSync application sources:\n" + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void BareSyncProject_DoesNotLinkLegacyTuiCompatSources()
    {
        var csprojPath = Path.Combine(ResolveBareSyncRepositoryRoot(), "src", "BareSync", "BareSync.csproj");
        Assert.True(File.Exists(csprojPath), $"Project file not found: {csprojPath}");

        var csproj = File.ReadAllText(csprojPath);
        Assert.DoesNotContain("..\\BareSync.Tui\\", csproj, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TuiCompat", csproj, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PriorityMigrationSources_DoNotUseLegacyTuiSymbols()
    {
        var priorityFiles = new[]
        {
            "App/Program.cs",
            "App/BatchMode/Screens/ExecutionScreen.cs",
            "App/BatchMode/Screens/PreflightScreen.cs",
            "App/BatchMode/Screens/RunSummaryScreen.cs"
        };

        AssertNoLegacyTuiSymbols(priorityFiles, "priority migration sources");
    }

    [Fact]
    public void WaveBMigrationSources_DoNotUseLegacyTuiSymbols()
    {
        var waveBFiles = new[]
        {
            "App/BatchMode/Screens/BatchHomeScreen.cs",
            "App/BatchMode/Screens/BatchListScreen.cs",
            "App/BatchMode/Screens/BatchExecuteSelectionScreen.cs",
            "App/BatchMode/Screens/BatchExecuteSummaryScreen.cs",
            "App/BatchMode/Screens/BatchDetailsScreen.cs"
        };

        AssertNoLegacyTuiSymbols(waveBFiles, "wave B migration sources");
    }

    [Fact]
    public void WaveCMigrationSources_DoNotUseLegacyTuiSymbols()
    {
        var waveCFiles = new[]
        {
            "App/BatchMode/Screens/ValidityDetailsScreen.cs",
            "App/BatchMode/Screens/StepTypePickerScreen.cs",
            "App/BatchMode/Screens/StepsEditorScreen.cs",
            "App/BatchMode/Screens/StepEditorScreen.cs",
            "App/BatchMode/Screens/PurgeBatchIndexesScreen.cs",
            "App/BatchMode/Screens/ContextEditorScreen.cs",
            "App/BatchMode/Screens/ArtifactsScreen.cs",
            "App/BatchMode/Screens/SecretPromptScreen.cs",
            "App/BatchMode/BatchModeController.cs",
            "App/BatchMode/BatchUiHelpers.cs",
            "UI/ConsoleMenu.cs",
            "UI/SettingsEditor.cs",
            "UI/ScreenRenderer.cs",
            "UI/OperationRunner.cs"
        };

        AssertNoLegacyTuiSymbols(waveCFiles, "wave C migration sources");
    }

    private static void AssertNoLegacyTuiSymbols(IEnumerable<string> relativeFiles, string scopeLabel)
    {
        var sourceRoot = Path.Combine(ResolveBareSyncRepositoryRoot(), "src", "BareSync");
        Assert.True(Directory.Exists(sourceRoot), $"Source root not found: {sourceRoot}");

        var violations = new List<string>();

        foreach (var relativeFile in relativeFiles)
        {
            var absoluteFile = Path.Combine(sourceRoot, relativeFile.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(absoluteFile), $"Migration file not found: {absoluteFile}");

            var lines = File.ReadAllLines(absoluteFile);
            for (var i = 0; i < lines.Length; i++)
            {
                foreach (var (label, pattern) in ForbiddenLegacyTuiPatterns)
                {
                    if (!pattern.IsMatch(lines[i]))
                    {
                        continue;
                    }

                    var normalized = relativeFile.Replace('\\', '/');
                    violations.Add($"{normalized}:{i + 1}:{label}:{lines[i].Trim()}");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            $"Legacy TUI symbols found in {scopeLabel}:\n" + string.Join(Environment.NewLine, violations));
    }

    private static string ResolveBareSyncRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "BareSync.sln"))
                && Directory.Exists(Path.Combine(current.FullName, "src", "BareSync")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Unable to resolve BareSync repository root from test execution directory.");
    }
}