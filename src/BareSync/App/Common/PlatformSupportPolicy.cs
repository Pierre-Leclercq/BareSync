namespace BareSync.App.Common;

internal static class PlatformSupportPolicy
{
    private const string Windows10EndOfSupportDate = "2025-10-14";

    public static void WriteStartupNotice()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 14393))
        {
            Bare.Primitive.UI.UiConsole.WriteLine("Warning: unsupported Windows version detected.");
            Bare.Primitive.UI.UiConsole.WriteLine("Minimum supported Windows runtime baseline: 10.0.14393 (Windows 10 version 1607).");
            Bare.Primitive.UI.UiConsole.WriteLine();
            return;
        }

        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            Bare.Primitive.UI.UiConsole.WriteLine($"Warning: Windows 10 reached end of support on {Windows10EndOfSupportDate}.");
            Bare.Primitive.UI.UiConsole.WriteLine("Supported baseline recommendation: Windows 11 (build 22000+) or newer.");
            Bare.Primitive.UI.UiConsole.WriteLine();
        }
    }
}
