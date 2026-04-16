using BareSync.Infra;
using Xunit;

namespace BareSync.Tests;

public sealed class DriveLabelPathResolverTests
{
    [Fact]
    public void Resolve_NotApplicable_ForAbsoluteDriveLetterPath()
    {
        var entries = new List<DriveLabelEntry>
        {
            new("Data", @"D:\")
        };

        var result = DriveLabelPathResolver.Resolve(@"C:\Source", entries);

        Assert.Equal(DriveLabelResolutionStatus.NotApplicable, result.Status);
        Assert.Equal(@"C:\Source", result.ResolvedPath);
    }

    [Fact]
    public void Resolve_Resolved_ForUniqueDriveLabel()
    {
        var entries = new List<DriveLabelEntry>
        {
            new("Vault", @"E:\")
        };

        var result = DriveLabelPathResolver.Resolve(@"Vault\Backups\Daily", entries);

        Assert.Equal(DriveLabelResolutionStatus.Resolved, result.Status);
        Assert.Equal(@"E:\Backups\Daily", result.ResolvedPath);
    }

    [Fact]
    public void Resolve_NotFound_WhenDriveLabelIsUnknown()
    {
        var entries = new List<DriveLabelEntry>
        {
            new("Vault", @"E:\")
        };

        var result = DriveLabelPathResolver.Resolve(@"Unknown\Backups", entries);

        Assert.Equal(DriveLabelResolutionStatus.NotFound, result.Status);
    }

    [Fact]
    public void Resolve_Ambiguous_WhenMultipleDrivesMatchAndNoDisambiguation()
    {
        var entries = new List<DriveLabelEntry>
        {
            new("Data", @"D:\"),
            new("Data", @"E:\")
        };

        var result = DriveLabelPathResolver.Resolve(
            @"Data\Project",
            entries,
            directoryExists: _ => false,
            fileExists: _ => false);

        Assert.Equal(DriveLabelResolutionStatus.Ambiguous, result.Status);
        Assert.Equal(2, result.CandidateRoots.Count);
        Assert.Contains(@"D:\", result.CandidateRoots);
        Assert.Contains(@"E:\", result.CandidateRoots);
    }

    [Fact]
    public void Resolve_Resolved_WhenAmbiguousLabelButSingleExistingPath()
    {
        var entries = new List<DriveLabelEntry>
        {
            new("Data", @"D:\"),
            new("Data", @"E:\")
        };

        var existingPath = @"E:\Project\Input";
        var result = DriveLabelPathResolver.Resolve(
            @"Data\Project\Input",
            entries,
            directoryExists: candidate => string.Equals(candidate, existingPath, StringComparison.OrdinalIgnoreCase),
            fileExists: _ => false);

        Assert.Equal(DriveLabelResolutionStatus.Resolved, result.Status);
        Assert.Equal(existingPath, result.ResolvedPath);
    }
}
