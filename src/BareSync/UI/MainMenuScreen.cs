using BareSync.Domain;

namespace BareSync.UI;

internal sealed class MainMenuScreen : IScreen
{
    private readonly AppConfig _config;
    private readonly IReadOnlyList<string> _options;
    private readonly string _zeroLabel;
    private readonly IReadOnlyList<string>? _footerLines;

    public MainMenuScreen(
        AppConfig config,
        IReadOnlyList<string> options,
        string zeroLabel,
        IReadOnlyList<string>? footerLines = null)
    {
        _config = config;
        _options = options;
        _zeroLabel = zeroLabel;
        _footerLines = footerLines;
    }

    public bool ShouldClearBeforeRender => true;

    public ScreenModel Build()
    {
        var bodyLines = new List<string>
        {
            $"Source = '{FormatValue(_config.SourceRoot)}'",
            $"Mirror = '{FormatValue(_config.MirrorRoot)}'",
            $"Mirror Mode = '{(_config.Mirror ? "ON" : "OFF")}'",
            string.Empty,
            "** Menu **",
            string.Empty
        };

        for (var index = 0; index < _options.Count; index++)
        {
            bodyLines.Add($"{index + 1}. {_options[index]}");
        }

        bodyLines.Add($"0. {_zeroLabel}");

        return new ScreenModel
        {
            Header = "** BareSync **",
            BodyLines = bodyLines,
            FooterLines = _footerLines is null ? new List<string>() : new List<string>(_footerLines)
        };
    }

    private static string FormatValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "<not set>" : value;
    }
}
