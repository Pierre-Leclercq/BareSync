using BareSync.Domain;
using BareSync.Infra;

namespace BareSync.Tests;

public sealed class PathExclusionMatcherTests
{
    [Fact]
    public void Create_UsesDefaultWindowsExclusions_WhenConfigIsNull()
    {
        var matcher = PathExclusionMatcher.Create(config: null);

        Assert.True(matcher.ShouldExcludeFile("Thumbs.db", "Thumbs.db"));
        Assert.True(matcher.ShouldExcludeFile("desktop.ini", "desktop.ini"));
        Assert.True(matcher.ShouldExcludeDirectory("System Volume Information", "System Volume Information"));
        Assert.True(matcher.ShouldExcludeDirectory("$RECYCLE.BIN", "$RECYCLE.BIN"));
        Assert.False(matcher.ShouldExcludeFile("alpha.txt", "alpha.txt"));
    }

    [Fact]
    public void ShouldExcludeFile_UsesCustomFileNameRules()
    {
        var config = new AppConfig
        {
            ExcludeFileNames = ["custom.cache"],
            ExcludeDirectoryNames = [],
            ExcludePathGlobs = []
        };

        var matcher = PathExclusionMatcher.Create(config);

        Assert.True(matcher.ShouldExcludeFile("custom.cache", "sub/custom.cache"));
        Assert.False(matcher.ShouldExcludeFile("alpha.txt", "sub/alpha.txt"));
    }

    [Fact]
    public void ShouldExcludeFile_ExcludesFilesInsideExcludedDirectoryNames()
    {
        var config = new AppConfig
        {
            ExcludeFileNames = [],
            ExcludeDirectoryNames = ["skip-dir"],
            ExcludePathGlobs = []
        };

        var matcher = PathExclusionMatcher.Create(config);

        Assert.True(matcher.ShouldExcludeFile("a.txt", "skip-dir/a.txt"));
        Assert.True(matcher.ShouldExcludeFile("a.txt", "x/skip-dir/a.txt"));
        Assert.False(matcher.ShouldExcludeFile("a.txt", "keep/a.txt"));
    }

    [Fact]
    public void ShouldExcludeDirectory_ExcludesNestedDirectoriesWhenParentExcluded()
    {
        var config = new AppConfig
        {
            ExcludeFileNames = [],
            ExcludeDirectoryNames = ["skip"],
            ExcludePathGlobs = []
        };

        var matcher = PathExclusionMatcher.Create(config);

        Assert.True(matcher.ShouldExcludeDirectory("skip", "skip"));
        Assert.True(matcher.ShouldExcludeDirectory("child", "skip/child"));
        Assert.False(matcher.ShouldExcludeDirectory("keep", "keep"));
    }

    [Theory]
    [InlineData("*.tmp", "root.tmp", true)]
    [InlineData("*.tmp", "sub/nested.tmp", true)]
    [InlineData("**/*.tmp", "sub/nested.tmp", true)]
    [InlineData("cache/**", "cache/a.bin", true)]
    [InlineData("cache/**", "deep/cache/a.bin", false)]
    [InlineData("**/cache/**", "deep/cache/a.bin", true)]
    [InlineData("temp/?ile.txt", "temp/file.txt", true)]
    [InlineData("temp/?ile.txt", "temp/xxile.txt", false)]
    public void ShouldExcludeRelativePath_MatchesGlobPatterns(string glob, string relativePath, bool expected)
    {
        var config = new AppConfig
        {
            ExcludeFileNames = [],
            ExcludeDirectoryNames = [],
            ExcludePathGlobs = [glob]
        };

        var matcher = PathExclusionMatcher.Create(config);

        Assert.Equal(expected, matcher.ShouldExcludeRelativePath(relativePath, IndexEntryKind.File));
    }
}
