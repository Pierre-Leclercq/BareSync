namespace BareSync.UI;

internal static class InlineProgressText
{
    public static string PadForRewrite(string nextText, int previousLength)
    {
        var safeText = nextText ?? string.Empty;
        if (previousLength <= safeText.Length)
        {
            return safeText;
        }

        return safeText + new string(' ', previousLength - safeText.Length);
    }
}
