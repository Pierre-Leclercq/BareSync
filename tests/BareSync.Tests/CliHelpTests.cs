using Xunit;

namespace BareSync.Tests;

public sealed class CliHelpTests
{
    [Fact]
    public void IsHelpRequested_WithNullArgs_ReturnsFalse()
    {
        Assert.False(BareSync.App.CliHelp.IsHelpRequested(null!));
    }

    [Fact]
    public void IsHelpRequested_WithEmptyArgs_ReturnsFalse()
    {
        Assert.False(BareSync.App.CliHelp.IsHelpRequested(Array.Empty<string>()));
    }

    [Fact]
    public void IsHelpRequested_WithDashDashHelp_ReturnsTrue()
    {
        Assert.True(BareSync.App.CliHelp.IsHelpRequested(new[] { "--help" }));
    }

    [Fact]
    public void IsHelpRequested_WithDashH_ReturnsTrue()
    {
        Assert.True(BareSync.App.CliHelp.IsHelpRequested(new[] { "-h" }));
    }

    [Fact]
    public void IsHelpRequested_WithQuestionMark_ReturnsTrue()
    {
        Assert.True(BareSync.App.CliHelp.IsHelpRequested(new[] { "/?" }));
    }

    [Fact]
    public void IsHelpRequested_IsCaseInsensitive()
    {
        Assert.True(BareSync.App.CliHelp.IsHelpRequested(new[] { "--HELP" }));
        Assert.True(BareSync.App.CliHelp.IsHelpRequested(new[] { "-H" }));
    }

    [Fact]
    public void IsHelpRequested_WithBatchArg_ReturnsFalse()
    {
        Assert.False(BareSync.App.CliHelp.IsHelpRequested(new[] { "/BATCH:MyBatch" }));
    }

    [Fact]
    public void IsHelpRequested_WithUnknownArg_ReturnsFalse()
    {
        Assert.False(BareSync.App.CliHelp.IsHelpRequested(new[] { "/NOPE" }));
    }

    [Fact]
    public void BuildHelpText_ReturnsStableOutput()
    {
        var help1 = BareSync.App.CliHelp.BuildHelpText();
        var help2 = BareSync.App.CliHelp.BuildHelpText();

        Assert.Equal(help1, help2);
    }

    [Fact]
    public void BuildHelpText_ContainsKeySections()
    {
        var help = BareSync.App.CliHelp.BuildHelpText();

        Assert.Contains("BareSync", help);
        Assert.Contains("Usage:", help);
        Assert.Contains("Options:", help);
        Assert.Contains("/BATCH:", help);
        Assert.Contains("/EXTRACT:", help);
        Assert.Contains("Environment variables:", help);
        Assert.Contains("BARESYNC_LOCK_DIR", help);
        Assert.Contains("BARESYNC_APP_DATA_ROOT", help);
        Assert.Contains("BARESYNC_APPSETTINGS_PATH", help);
        Assert.Contains("BARESYNC_DISABLE_SECRET_STORE", help);
    }

    [Fact]
    public void BuildHelpText_ContainsHelpFlags()
    {
        var help = BareSync.App.CliHelp.BuildHelpText();

        Assert.Contains("--help", help);
        Assert.Contains("-h", help);
        Assert.Contains("/?", help);
    }

    [Fact]
    public void TryHandleCliHelpOrErrors_WithHelpFlag_ReturnsZero()
    {
        var output = new List<string>();
        var result = BareSync.App.CliHelp.TryHandleCliHelpOrErrors(
            new[] { "--help" },
            s => output.Add(s));

        Assert.Equal(0, result);
        Assert.Contains("BareSync", output[0]);
    }

    [Fact]
    public void TryHandleCliHelpOrErrors_WithQuestionMark_ReturnsZero()
    {
        var output1 = new List<string>();
        var result1 = BareSync.App.CliHelp.TryHandleCliHelpOrErrors(
            new[] { "/?" },
            s => output1.Add(s));

        var output2 = new List<string>();
        var result2 = BareSync.App.CliHelp.TryHandleCliHelpOrErrors(
            new[] { "--help" },
            s => output2.Add(s));

        Assert.Equal(0, result1);
        Assert.Equal(0, result2);
        Assert.Equal(output1, output2);
    }

    [Fact]
    public void TryHandleCliHelpOrErrors_WithUnknownArg_ReturnsTwo()
    {
        var output = new List<string>();
        var result = BareSync.App.CliHelp.TryHandleCliHelpOrErrors(
            new[] { "/NOPE" },
            s => output.Add(s));

        Assert.Equal(2, result);
        Assert.Contains("Unknown argument: /NOPE", output);
        Assert.Contains("Run with --help for usage.", output);
    }

    [Fact]
    public void TryHandleCliHelpOrErrors_WithBatchArg_ReturnsNull()
    {
        var output = new List<string>();
        var result = BareSync.App.CliHelp.TryHandleCliHelpOrErrors(
            new[] { "/BATCH:MyBatch" },
            s => output.Add(s));

        Assert.Null(result);
        Assert.Empty(output);
    }

    [Fact]
    public void TryHandleCliHelpOrErrors_WithExtractArg_ReturnsNull()
    {
        var output = new List<string>();
        var result = BareSync.App.CliHelp.TryHandleCliHelpOrErrors(
            new[] { "/EXTRACT:D:/Vault/a.bse" },
            s => output.Add(s));

        Assert.Null(result);
        Assert.Empty(output);
    }

    [Fact]
    public void TryHandleCliHelpOrErrors_WithEmptyArgs_ReturnsNull()
    {
        var output = new List<string>();
        var result = BareSync.App.CliHelp.TryHandleCliHelpOrErrors(
            Array.Empty<string>(),
            s => output.Add(s));

        Assert.Null(result);
        Assert.Empty(output);
    }

    [Fact]
    public void TryHandleCliHelpOrErrors_WithMixedArgs_PrioritizesHelp()
    {
        // When help flag is present alongside other args, help should be shown
        var output = new List<string>();
        var result = BareSync.App.CliHelp.TryHandleCliHelpOrErrors(
            new[] { "--help", "/BATCH:MyBatch" },
            s => output.Add(s));

        Assert.Equal(0, result);
        Assert.Contains("BareSync", output[0]);
    }
}