using BareSync.UI;
using Xunit;

namespace BareSync.Tests;

public sealed class PathPromptHelperTests
{
    [Fact]
    public void ResolveSuggestedFileName_WhenPreferDefault_IsUsesDefaultEvenIfCurrentExists()
    {
        var result = PathPromptHelper.ResolveSuggestedFileName(
            currentValue: @"D:\Data\existing.csv",
            defaultFileName: "generated_guid.csv",
            preferDefaultFileName: true);

        Assert.Equal("generated_guid.csv", result);
    }

    [Fact]
    public void ResolveSuggestedFileName_WhenPreferCurrent_UsesCurrentFileName()
    {
        var result = PathPromptHelper.ResolveSuggestedFileName(
            currentValue: @"D:\Data\existing.csv",
            defaultFileName: "generated_guid.csv",
            preferDefaultFileName: false);

        Assert.Equal("existing.csv", result);
    }

    [Fact]
    public void ResolveSuggestedFileName_WhenPreferredValueEmpty_FallsBackToOtherValue()
    {
        var preferDefaultFallback = PathPromptHelper.ResolveSuggestedFileName(
            currentValue: @"D:\Data\existing.csv",
            defaultFileName: string.Empty,
            preferDefaultFileName: true);

        var preferCurrentFallback = PathPromptHelper.ResolveSuggestedFileName(
            currentValue: string.Empty,
            defaultFileName: "generated_guid.csv",
            preferDefaultFileName: false);

        Assert.Equal("existing.csv", preferDefaultFallback);
        Assert.Equal("generated_guid.csv", preferCurrentFallback);
    }
}