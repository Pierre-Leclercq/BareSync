using System.Text;

namespace BareSync.App;

internal static class CliHelp
{
    private static readonly IReadOnlyList<CliOption> Options = new[]
    {
        new CliOption(new[] { "--help", "-h", "/?" }, "Show this help message and exit."),
        new CliOption(new[] { "/BATCH:<name>" }, "Run batch operations from command line. The batch name can be quoted if it contains spaces: /BATCH:\"My Batch\"."),
        new CliOption(new[] { "/EXTRACT:<path>" }, "Extract encrypted .bse content from a file or folder path. The path can be quoted: /EXTRACT:\"D:\\Vault\\abc.bse\".")
    };

    private static readonly IReadOnlyList<CliEnvironmentVariable> EnvironmentVariables = new[]
    {
        new CliEnvironmentVariable("BARESYNC_LOCK_DIR", "Override the lock directory path (default: application base directory)."),
        new CliEnvironmentVariable("BARESYNC_APP_DATA_ROOT", "Override the application data root path (default: application base directory)."),
        new CliEnvironmentVariable("BARESYNC_APPSETTINGS_PATH", "Override the configuration file path (default: appsettings.json in application base directory)."),
        new CliEnvironmentVariable("BARESYNC_DISABLE_SECRET_STORE", "Disable OS secret store. Set to 1, true, or yes to disable (passwords must be entered at each run).")
    };

    public static bool IsHelpRequested(string[] args)
    {
        if (args is null || args.Length == 0)
        {
            return false;
        }

        foreach (var arg in args)
        {
            if (string.IsNullOrWhiteSpace(arg))
            {
                continue;
            }

            var normalized = arg.Trim();
            if (normalized.Equals("--help", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("-h", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("/?", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    public static string BuildHelpText()
    {
        var sb = new StringBuilder();

        // Title
        sb.AppendLine("BareSync — filesystem integrity & synchronization tool");
        sb.AppendLine();

        // Usage
        sb.AppendLine("Usage:");
        sb.AppendLine("  BareSync [options]");
        sb.AppendLine("  BareSync /BATCH:<name> [options]");
        sb.AppendLine("  BareSync /EXTRACT:<path> [options]");
        sb.AppendLine();

        // Options
        sb.AppendLine("Options:");
        foreach (var opt in Options)
        {
            var forms = string.Join(", ", opt.Forms);
            sb.AppendLine($"  {forms}");
            sb.AppendLine($"      {opt.Description}");
        }
        sb.AppendLine();

        // Environment Variables
        sb.AppendLine("Environment variables:");
        foreach (var env in EnvironmentVariables)
        {
            sb.AppendLine($"  {env.Name}");
            sb.AppendLine($"      {env.Description}");
        }
        sb.AppendLine();

        // Examples
        sb.AppendLine("Examples:");
        sb.AppendLine("  BareSync --help");
        sb.AppendLine("  BareSync -h");
        sb.AppendLine("  BareSync /BATCH:\"My Backup\"");
        sb.AppendLine("  BareSync /EXTRACT:\"D:\\Vault\\A1B2C3.bse\"");

        return sb.ToString();
    }

    /// <summary>
    /// Handles help request and CLI errors. Returns exit code if handled, null to continue normal execution.
    /// </summary>
    public static int? TryHandleCliHelpOrErrors(
        string[] args,
        Action<string> writeLine)
    {
        if (args is null || args.Length == 0)
        {
            return null;
        }

        // Check for help request
        if (IsHelpRequested(args))
        {
            writeLine(BuildHelpText());
            return 0;
        }

        // Check for unknown arguments (anything that doesn't start with supported prefixes)
        foreach (var arg in args)
        {
            if (string.IsNullOrWhiteSpace(arg))
            {
                continue;
            }

            var trimmed = arg.Trim();

            // Skip if it looks like a valid /BATCH: or /EXTRACT: argument
            if (trimmed.StartsWith("/BATCH:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (trimmed.StartsWith("/EXTRACT:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Unknown argument
            writeLine($"Unknown argument: {trimmed}");
            writeLine("Run with --help for usage.");
            return 2;
        }

        return null;
    }

    private sealed record CliOption(IReadOnlyList<string> Forms, string Description);
    private sealed record CliEnvironmentVariable(string Name, string Description);
}