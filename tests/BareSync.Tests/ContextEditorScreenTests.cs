using BareSync.App.BatchMode.Screens;
using Xunit;

namespace BareSync.Tests;

public sealed class ContextEditorScreenTests
{
    [Fact]
    public void BuildGuidSuffixedDefaultIndexFileName_SourceBaseName_HasExpectedPrefixAndCsvExtension()
    {
        var fileName = ContextEditorScreen.BuildGuidSuffixedDefaultIndexFileName("baresync_source_index.csv");

        Assert.Matches("^baresync_source_index_[0-9a-f]{32}\\.csv$", fileName);
    }

    [Fact]
    public void BuildGuidSuffixedDefaultIndexFileName_DestBaseName_GeneratesDifferentValuesAcrossCalls()
    {
        var first = ContextEditorScreen.BuildGuidSuffixedDefaultIndexFileName("baresync_dest_index.csv");
        var second = ContextEditorScreen.BuildGuidSuffixedDefaultIndexFileName("baresync_dest_index.csv");

        Assert.NotEqual(first, second);
        Assert.Matches("^baresync_dest_index_[0-9a-f]{32}\\.csv$", first);
        Assert.Matches("^baresync_dest_index_[0-9a-f]{32}\\.csv$", second);
    }
}
