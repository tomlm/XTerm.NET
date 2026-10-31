using System.Globalization;

namespace XTerm.Common;

/// <summary>
/// Parsing and formatting for the colour specifications used by OSC 4 and OSC 10/11/12.
/// </summary>
/// <remarks>
/// Colours are carried as 0xRRGGBB. The wire formats are X11's, and a terminal that only accepts
/// one of them looks broken for half the programs that set colours: rgb: is what xterm's own
/// documentation uses, # is what most theme scripts emit, and bare names are what shell snippets
/// tend to hand-write.
/// </remarks>
public static class ColorSpec
{
    /// <summary>
    /// Parses an X11 colour specification into 0xRRGGBB.
    /// </summary>
    /// <remarks>
    /// Accepts:
    ///   rgb:R/G/B          with 1 to 4 hex digits per channel
    ///   #RGB #RRGGBB #RRRRGGGGBBBB
    ///   a colour name from the small set below
    /// </remarks>
    public static bool TryParse(string? spec, out int rgb)
    {
        rgb = 0;
        if (string.IsNullOrWhiteSpace(spec))
        {
            return false;
        }

        spec = spec.Trim();

        if (spec.StartsWith("rgb:", StringComparison.OrdinalIgnoreCase))
        {
            return TryParseRgbForm(spec.Substring(4), out rgb);
        }

        if (spec[0] == '#')
        {
            return TryParseHashForm(spec.Substring(1), out rgb);
        }

        // The device-independent Xcms spaces -- rgbi:, CIEXYZ:, CIELab: and the rest -- go
        // through X11's own conversion pipeline. See XcmsColor for why byte-exactness matters.
        if (spec.Contains(':') && XcmsColor.TryParse(spec, out rgb))
        {
            return true;
        }

        return NamedColors.TryGetValue(spec, out rgb);
    }

    /// <summary>
    /// Formats a colour as the reply body for an OSC colour query.
    /// </summary>
    /// <remarks>
    /// Four hex digits per channel, which is what xterm emits and therefore what programs that
    /// probe a terminal are written to read. Each 8-bit channel is widened by repetition rather
    /// than by shifting, so 0xff becomes 0xffff and not 0xff00 -- full intensity has to stay full.
    /// </remarks>
    public static string Format(int rgb)
    {
        var r = (rgb >> 16) & 0xFF;
        var g = (rgb >> 8) & 0xFF;
        var b = rgb & 0xFF;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"rgb:{r:x2}{r:x2}/{g:x2}{g:x2}/{b:x2}{b:x2}");
    }

    /// <summary>
    /// Formats a colour in the given report format, or returns null for
    /// <see cref="ColorReportFormat.None"/>, which means the query gets no reply.
    /// </summary>
    /// <remarks>
    /// Only an explicit None is silent. A value outside the enum gets xterm's sixteen-bit form,
    /// the default, rather than silence nobody asked for.
    /// </remarks>
    public static string? Format(int rgb, ColorReportFormat format)
    {
        switch (format)
        {
            case ColorReportFormat.None:
                return null;
            case ColorReportFormat.EightBit:
                var r = (rgb >> 16) & 0xFF;
                var g = (rgb >> 8) & 0xFF;
                var b = rgb & 0xFF;
                return string.Create(CultureInfo.InvariantCulture, $"rgb:{r:x2}/{g:x2}/{b:x2}");
            default:
                return Format(rgb);
        }
    }

    private static bool TryParseRgbForm(string body, out int rgb)
    {
        rgb = 0;
        var parts = body.Split('/');
        if (parts.Length != 3)
        {
            return false;
        }

        var channels = new int[3];
        for (var i = 0; i < 3; i++)
        {
            if (!TryParseChannel(parts[i], out channels[i]))
            {
                return false;
            }
        }

        rgb = (channels[0] << 16) | (channels[1] << 8) | channels[2];
        return true;
    }

    private static bool TryParseChannel(string text, out int value)
    {
        value = 0;
        if (text.Length is < 1 or > 4)
        {
            return false;
        }

        if (!int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var raw))
        {
            return false;
        }

        // Scale to 8 bits by the width actually written: "f" is full intensity in a 1-digit
        // channel, and truncating instead would turn it into 0x0f.
        var max = (1 << (4 * text.Length)) - 1;
        value = (int)Math.Round(raw * 255.0 / max);
        return true;
    }

    private static bool TryParseHashForm(string body, out int rgb)
    {
        rgb = 0;
        if (body.Length % 3 != 0)
        {
            return false;
        }

        var width = body.Length / 3;
        if (width is < 1 or > 4)
        {
            return false;
        }

        var channels = new int[3];
        for (var i = 0; i < 3; i++)
        {
            // X11's rule for the hash form, which differs from rgb:'s. rgb: SCALES -- "f" means
            // full intensity. # LEFT-JUSTIFIES -- the digits are the most significant bits, so
            // "#fff" is f000-per-channel territory: each channel is its first two digits (a lone
            // digit padded with zero), and deeper digits are truncated. xterm answers a query for
            // #fff with f0f0/f0f0/f0f0, and programs match against exactly that.
            var part = body.Substring(i * width, width);
            var top = width switch
            {
                1 => part + "0",
                2 => part,
                _ => part[..2],
            };
            if (!int.TryParse(top, System.Globalization.NumberStyles.HexNumber,
                              System.Globalization.CultureInfo.InvariantCulture, out channels[i]))
            {
                return false;
            }
        }

        rgb = (channels[0] << 16) | (channels[1] << 8) | channels[2];
        return true;
    }

    /// <summary>
    /// The colour names common enough to matter. Not the full X11 list, which runs to hundreds of
    /// entries almost none of which are ever sent to a terminal.
    /// </summary>
    private static readonly Dictionary<string, int> NamedColors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["black"] = 0x000000,
        ["red"] = 0xFF0000,
        ["green"] = 0x008000,
        ["yellow"] = 0xFFFF00,
        ["blue"] = 0x0000FF,
        ["magenta"] = 0xFF00FF,
        ["cyan"] = 0x00FFFF,
        ["white"] = 0xFFFFFF,
        ["gray"] = 0x808080,
        ["grey"] = 0x808080,
        ["darkgray"] = 0xA9A9A9,
        ["darkgrey"] = 0xA9A9A9,
        ["lightgray"] = 0xD3D3D3,
        ["lightgrey"] = 0xD3D3D3,
        ["maroon"] = 0x800000,
        ["olive"] = 0x808000,
        ["navy"] = 0x000080,
        ["purple"] = 0x800080,
        ["teal"] = 0x008080,
        ["silver"] = 0xC0C0C0,
        ["lime"] = 0x00FF00,
        ["aqua"] = 0x00FFFF,
        ["fuchsia"] = 0xFF00FF,
        ["orange"] = 0xFFA500,
        ["pink"] = 0xFFC0CB,
        ["brown"] = 0xA52A2A,
    };
}

/// <summary>
/// How the terminal answers an OSC colour query (OSC 4, 5, 10, 11 and 12 with <c>?</c>).
/// </summary>
public enum ColorReportFormat
{
    /// <summary>Queries get no reply at all.</summary>
    None,

    /// <summary>Two hex digits per channel: <c>rgb:rr/gg/bb</c>.</summary>
    EightBit,

    /// <summary>Four hex digits per channel, <c>rgb:rrrr/gggg/bbbb</c>, as xterm answers.</summary>
    SixteenBit,
}
