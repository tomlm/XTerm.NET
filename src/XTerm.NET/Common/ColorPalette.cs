using System.Threading;
using XTerm.Buffer;
using XTerm.Options;

namespace XTerm.Common;

/// <summary>
/// The terminal's 256-entry colour palette plus its foreground, background and cursor colours,
/// as read by OSC 4 and OSC 10/11/12 and reset by OSC 104 and OSC 110/111/112.
/// </summary>
/// <remarks>
/// Two layers, and the distinction is the whole point of the type. DEFAULTS come from
/// <see cref="ThemeOptions"/>, i.e. from the embedder. CURRENT values are what programs have set
/// over the top with OSC. A reset returns to the defaults, so it restores the EMBEDDER'S theme
/// rather than some factory dark palette -- otherwise any program calling OSC 104 would drag a
/// light terminal back to black, which is exactly the bug that makes light themes unusable.
/// </remarks>
public class ColorPalette
{
    /// <summary>
    /// Number of indexed colours. 0-15 are the ANSI colours, 16-231 a 6x6x6 cube, 232-255 a
    /// greyscale ramp.
    /// </summary>
    public const int Size = 256;

    private readonly int[] defaults = new int[Size];

    /// <summary>Serialises writers against each other. Readers never take it.</summary>
    private readonly object writeGate = new();

    private int defaultForeground;
    private int defaultBackground;
    private int defaultCursor;

    /// <summary>The theme's bold colour, or <see cref="ColorSnapshot.NoBold"/> when it sets none.</summary>
    private int defaultBold = ColorSnapshot.NoBold;

    /// <summary>
    /// The live colours. Replaced wholesale for bulk changes, mutated in place for single ones.
    /// </summary>
    /// <remarks>
    /// Read on a renderer's hot path -- once per cell -- so reads are lock free, and a lock here
    /// would be a worse cure than the problem. What that costs is a discipline rather than a lock:
    ///
    /// SINGLE colour changes mutate the array or a scalar in place. An aligned int write is atomic,
    /// so a reader sees the old value or the new one and never half of either.
    ///
    /// BULK changes -- ApplyTheme, ResetAllColors -- build a whole new snapshot and swap the
    /// reference. Copying into the live array instead was the actual bug: Array.Copy is not atomic,
    /// so a renderer could paint a frame with half the old theme and half the new one. A reference
    /// swap has no half.
    /// </remarks>
    private ColorSnapshot state;

    /// <summary>
    /// Initializes a palette with the built-in xterm defaults.
    /// </summary>
    public ColorPalette()
        : this(null)
    {
    }

    /// <summary>
    /// Initializes a palette, taking defaults from <paramref name="theme"/> wherever it specifies
    /// one and falling back to the xterm defaults elsewhere.
    /// </summary>
    public ColorPalette(ThemeOptions? theme)
    {
        // Seeded inline rather than through ApplyTheme, so construction does not raise ColorChanged.
        // Nothing can be subscribed yet, so the event went nowhere, but it made ApplyTheme's
        // behaviour depend on when it was called.
        SeedDefaults(theme);
        state = NewSnapshot();
    }

    /// <summary>
    /// Fired whenever a colour changes, so a renderer can repaint. Not raised when a set is a no-op.
    /// </summary>
    public event EventHandler<ColorChangedEventArgs>? ColorChanged;

    /// <summary>
    /// The current foreground colour, as 0xRRGGBB.
    /// </summary>
    public int Foreground => Volatile.Read(ref state).Foreground;

    /// <summary>
    /// The current background colour, as 0xRRGGBB.
    /// </summary>
    public int Background => Volatile.Read(ref state).Background;

    /// <summary>
    /// The current cursor colour, as 0xRRGGBB.
    /// </summary>
    public int Cursor => Volatile.Read(ref state).Cursor;

    /// <summary>
    /// The colour for bold text with the default foreground, as 0xRRGGBB, or null when the theme
    /// sets none. See <see cref="ThemeOptions.Bold"/>.
    /// </summary>
    public int? Bold => Volatile.Read(ref state).Bold;

    /// <summary>
    /// Whether the background is light enough that a program should choose dark text.
    /// </summary>
    /// <remarks>
    /// Uses the ITU-R BT.601 luma of the background against a mid threshold. Offered because every
    /// consumer that wants it would otherwise write the same formula, and would be likely to write
    /// it as a plain average of the channels -- which calls pure blue light.
    /// </remarks>
    public bool IsLightBackground => IsLight(Background);

    /// <summary>
    /// Gets the current colour for an index, as 0xRRGGBB.
    /// </summary>
    public int this[int index]
    {
        get
        {
            ValidateIndex(index);
            return Volatile.Read(ref state).Colors[index];
        }
    }

    /// <summary>
    /// Takes a coherent, immutable view of every colour at this instant.
    /// </summary>
    /// <remarks>
    /// What to use when reading MORE THAN ONE colour, which for a renderer painting a frame is
    /// always. The individual properties on this class each read the current state separately, so
    /// eight reads can straddle a theme change and return a mix -- measured at three and a half
    /// million mixed reads per three seconds under a tight toggle, so this is routine rather than a
    /// rare race. One snapshot cannot: it is a single reference, and nothing mutates it afterwards.
    /// </remarks>
    public ColorSnapshot Take() => Volatile.Read(ref state);

    /// <summary>
    /// Replaces the defaults from a theme and discards any colours programs had set.
    /// </summary>
    /// <remarks>
    /// The runtime path for an embedder following the OS light/dark setting: call this when the
    /// system theme flips. Everything is re-seeded, because a palette half in the old theme and
    /// half in the new one is not a theme.
    /// </remarks>
    public void ApplyTheme(ThemeOptions? theme)
    {
        bool changed;

        lock (writeGate)
        {
            SeedDefaults(theme);

            var live = state;
            changed = live.Foreground != defaultForeground
                || live.Background != defaultBackground
                || live.Cursor != defaultCursor
                || live.BoldOrNone != defaultBold
                || !live.Colors.AsSpan().SequenceEqual(defaults);

            if (changed)
            {
                // One swap, so no reader ever sees a partly applied theme.
                Volatile.Write(ref state, NewSnapshot());
            }
        }

        // Raised outside the lock: a handler is consumer code and may call back in.
        if (changed)
        {
            ColorChanged?.Invoke(this, new ColorChangedEventArgs(ColorTarget.All, -1, 0));
        }
    }

    /// <summary>
    /// Sets one indexed colour, as OSC 4 does.
    /// </summary>
    public void SetColor(int index, int rgb)
    {
        ValidateIndex(index);

        lock (writeGate)
        {
            var live = state;
            if (live.Colors[index] == rgb)
            {
                return;
            }

            // Copy on write, rather than storing into the live array. An int store is atomic, so
            // mutating in place would be safe for the VALUE -- but it would also change a snapshot
            // somebody is already holding, and the whole point of handing one out is that it does
            // not move underneath them. A palette is 1KB and colour changes are rare.
            var colors = new int[Size];
            Array.Copy(live.Colors, colors, Size);
            colors[index] = rgb;
            Volatile.Write(ref state, new ColorSnapshot(colors, live.Foreground, live.Background, live.Cursor, live.BoldOrNone));
        }

        ColorChanged?.Invoke(this, new ColorChangedEventArgs(ColorTarget.Indexed, index, rgb));
    }

    /// <summary>
    /// Sets the foreground colour, as OSC 10 does.
    /// </summary>
    public void SetForeground(int rgb) => Set(ColorTarget.Foreground, rgb);

    /// <summary>
    /// Sets the background colour, as OSC 11 does.
    /// </summary>
    public void SetBackground(int rgb) => Set(ColorTarget.Background, rgb);

    /// <summary>
    /// Sets the cursor colour, as OSC 12 does.
    /// </summary>
    public void SetCursor(int rgb) => Set(ColorTarget.Cursor, rgb);

    /// <summary>
    /// Restores one indexed colour to its default, as OSC 104 with a parameter does.
    /// </summary>
    public void ResetColor(int index)
    {
        ValidateIndex(index);
        SetColor(index, defaults[index]);
    }

    /// <summary>
    /// Restores every indexed colour to its default, as bare OSC 104 does.
    /// </summary>
    public void ResetAllColors()
    {
        lock (writeGate)
        {
            var live = state;
            if (live.Colors.AsSpan().SequenceEqual(defaults))
            {
                return;
            }

            var colors = new int[Size];
            Array.Copy(defaults, colors, Size);
            Volatile.Write(ref state, new ColorSnapshot(colors, live.Foreground, live.Background, live.Cursor, live.BoldOrNone));
        }

        ColorChanged?.Invoke(this, new ColorChangedEventArgs(ColorTarget.Indexed, -1, 0));
    }

    /// <summary>
    /// Restores the foreground colour, as OSC 110 does.
    /// </summary>
    public void ResetForeground() => SetForeground(defaultForeground);

    /// <summary>
    /// Restores the background colour, as OSC 111 does.
    /// </summary>
    public void ResetBackground() => SetBackground(defaultBackground);

    /// <summary>
    /// Restores the cursor colour, as OSC 112 does.
    /// </summary>
    public void ResetCursor() => SetCursor(defaultCursor);

    internal static bool IsLight(int rgb) => Luma(rgb) > 0.5;

    private static double Luma(int rgb)
    {
        var r = ((rgb >> 16) & 0xFF) / 255.0;
        var g = ((rgb >> 8) & 0xFF) / 255.0;
        var b = (rgb & 0xFF) / 255.0;
        return (0.299 * r) + (0.587 * g) + (0.114 * b);
    }

    /// <summary>
    /// Rejects an index outside the palette.
    /// </summary>
    /// <remarks>
    /// Throws rather than clamping. Clamping is the one response that produces a plausible wrong
    /// answer: SetColor(999, ...) quietly rewrote entry 255, and the indexer answered for entry 0
    /// when asked about -1. A caller with an off-by-one got no signal and a corrupted palette. The
    /// OSC path never sees this, because InputHandler range-checks before calling -- which is
    /// exactly why it went unnoticed.
    /// </remarks>
    private static void ValidateIndex(int index)
    {
        if (index < 0 || index >= Size)
        {
            throw new ArgumentOutOfRangeException(
                nameof(index), index, $"Palette index must be between 0 and {Size - 1}.");
        }
    }

    private void Set(ColorTarget target, int rgb)
    {
        lock (writeGate)
        {
            var live = state;
            int foreground = live.Foreground, background = live.Background, cursor = live.Cursor;

            switch (target)
            {
                case ColorTarget.Foreground when foreground != rgb: foreground = rgb; break;
                case ColorTarget.Background when background != rgb: background = rgb; break;
                case ColorTarget.Cursor when cursor != rgb: cursor = rgb; break;
                default: return;
            }

            Volatile.Write(ref state, new ColorSnapshot(live.Colors, foreground, background, cursor, live.BoldOrNone));
        }

        ColorChanged?.Invoke(this, new ColorChangedEventArgs(target, -1, rgb));
    }

    /// <summary>
    /// Builds a snapshot holding the current defaults.
    /// </summary>
    private ColorSnapshot NewSnapshot()
    {
        var colors = new int[Size];
        Array.Copy(defaults, colors, Size);
        return new ColorSnapshot(colors, defaultForeground, defaultBackground, defaultCursor, defaultBold);
    }

    private void SeedDefaults(ThemeOptions? theme)
    {
        // 0-15: the xterm ANSI defaults, each overridable by the theme.
        var ansi = new[]
        {
            0x000000, 0xCD0000, 0x00CD00, 0xCDCD00, 0x0000EE, 0xCD00CD, 0x00CDCD, 0xE5E5E5,
            0x7F7F7F, 0xFF0000, 0x00FF00, 0xFFFF00, 0x5C5CFF, 0xFF00FF, 0x00FFFF, 0xFFFFFF,
        };

        var overrides = theme is null
            ? new string?[16]
            : new[]
            {
                theme.Black, theme.Red, theme.Green, theme.Yellow,
                theme.Blue, theme.Magenta, theme.Cyan, theme.White,
                theme.BrightBlack, theme.BrightRed, theme.BrightGreen, theme.BrightYellow,
                theme.BrightBlue, theme.BrightMagenta, theme.BrightCyan, theme.BrightWhite,
            };

        for (var i = 0; i < 16; i++)
        {
            defaults[i] = ColorSpec.TryParse(overrides[i], out var themed) ? themed : ansi[i];
        }

        // 16-231: the 6x6x6 cube. Levels are xterm's, which are not evenly spaced -- the step from
        // 0 to 95 is larger than the rest so that the darkest cell is properly black.
        var levels = new[] { 0, 95, 135, 175, 215, 255 };
        for (var r = 0; r < 6; r++)
        {
            for (var g = 0; g < 6; g++)
            {
                for (var b = 0; b < 6; b++)
                {
                    defaults[16 + (36 * r) + (6 * g) + b] =
                        (levels[r] << 16) | (levels[g] << 8) | levels[b];
                }
            }
        }

        // 232-255: the greyscale ramp, deliberately excluding both pure black and pure white,
        // which already exist in the cube.
        for (var i = 0; i < 24; i++)
        {
            var v = 8 + (i * 10);
            defaults[232 + i] = (v << 16) | (v << 8) | v;
        }

        // Defaults chosen to match what this terminal previously answered colour queries with, so
        // an embedder that sets no theme sees no change from this.
        defaultForeground = ColorSpec.TryParse(theme?.Foreground, out var fg) ? fg : 0xFFFFFF;
        defaultBackground = ColorSpec.TryParse(theme?.Background, out var bg) ? bg : 0x000000;
        defaultCursor = ColorSpec.TryParse(theme?.Cursor, out var cur) ? cur : 0xFFFFFF;
        defaultBold = ColorSpec.TryParse(theme?.Bold, out var bold) ? bold : ColorSnapshot.NoBold;
    }


}

/// <summary>
/// An immutable view of every terminal colour at one instant.
/// </summary>
/// <remarks>
/// Handed out by <see cref="ColorPalette.Take"/> and never modified afterwards, so a renderer can
/// paint a whole frame from one and know the colours belong to each other.
/// </remarks>
public sealed class ColorSnapshot
{
    /// <summary>Stands for "no bold colour" in <see cref="BoldOrNone"/>; no 0xRRGGBB value is negative.</summary>
    internal const int NoBold = -1;

    private readonly int[] colors;

    /// <summary>The bold colour, or <see cref="NoBold"/>. An int rather than an int? so the
    /// per-cell resolve reads one field.</summary>
    private readonly int bold;

    internal ColorSnapshot(int[] colors, int foreground, int background, int cursor, int bold)
    {
        this.colors = colors;
        this.bold = bold;
        Foreground = foreground;
        Background = background;
        Cursor = cursor;
    }

    /// <summary>Gets the foreground colour, as 0xRRGGBB.</summary>
    public int Foreground { get; }

    /// <summary>Gets the background colour, as 0xRRGGBB.</summary>
    public int Background { get; }

    /// <summary>Gets the cursor colour, as 0xRRGGBB.</summary>
    public int Cursor { get; }

    /// <summary>
    /// Gets the colour for bold text with the default foreground, as 0xRRGGBB, or null when the
    /// theme sets none. See <see cref="ThemeOptions.Bold"/>.
    /// </summary>
    public int? Bold => bold == NoBold ? null : bold;

    /// <summary>
    /// Resolves the colour a cell's text is drawn in, from its foreground attribute, as 0xRRGGBB.
    /// </summary>
    /// <param name="attributes">The cell's attributes.</param>
    /// <param name="drawBoldTextInBrightColors">
    /// <see cref="TerminalOptions.DrawBoldTextInBrightColors"/>: whether a bold cell coloured with
    /// palette index 0-7 is drawn with index 8-15 instead.
    /// </param>
    /// <remarks>
    /// <para>The one place the bold rules live, so every host applies them alike: a bold cell
    /// with the DEFAULT foreground takes <see cref="Bold"/> when the theme sets one; a bold cell
    /// with palette index 0-7 moves to the bright half when
    /// <paramref name="drawBoldTextInBrightColors"/> says so; every other colour -- 24-bit RGB, a
    /// palette index from 8 up -- comes through as the program named it. A cell that is not
    /// bold resolves exactly as it would with no bold settings at all.</para>
    /// <para>It resolves the foreground ATTRIBUTE. Inverse, dim and invisible are the host's to
    /// apply on top, since what each means depends on how the host paints.</para>
    /// </remarks>
    public int ResolveForeground(in AttributeData attributes, bool drawBoldTextInBrightColors)
    {
        if (attributes.GetFgColorMode() == 1)
            return attributes.GetFgColor();

        var color = attributes.GetFgColor();
        if (color < ColorPalette.Size)
        {
            if (color < 8 && drawBoldTextInBrightColors && attributes.IsBold())
                color += 8;
            return colors[color];
        }

        // Default foreground. The bold colour is checked first because unset is one compare.
        return bold != NoBold && attributes.IsBold() ? bold : Foreground;
    }

    /// <summary>Gets whether the background is light enough that dark text belongs on it.</summary>
    public bool IsLightBackground => ColorPalette.IsLight(Background);

    /// <summary>Gets the colour for an index, as 0xRRGGBB.</summary>
    public int this[int index]
    {
        get
        {
            if (index < 0 || index >= ColorPalette.Size)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(index), index, $"Palette index must be between 0 and {ColorPalette.Size - 1}.");
            }

            return colors[index];
        }
    }

    internal int[] Colors => colors;

    internal int BoldOrNone => bold;
}

/// <summary>
/// Which colour a <see cref="ColorPalette.ColorChanged"/> notification is about.
/// </summary>
public enum ColorTarget
{
    /// <summary>
    /// An indexed palette colour. Index -1 means every entry changed at once.
    /// </summary>
    Indexed,

    /// <summary>
    /// The foreground colour.
    /// </summary>
    Foreground,

    /// <summary>
    /// The background colour.
    /// </summary>
    Background,

    /// <summary>
    /// The cursor colour.
    /// </summary>
    Cursor,

    /// <summary>
    /// Everything was re-seeded, typically by a theme change.
    /// </summary>
    All,
}

/// <summary>
/// Describes a colour change.
/// </summary>
public class ColorChangedEventArgs : EventArgs
{
    public ColorChangedEventArgs(ColorTarget target, int index, int rgb)
    {
        Target = target;
        Index = index;
        Rgb = rgb;
    }

    /// <summary>
    /// Which colour changed.
    /// </summary>
    public ColorTarget Target { get; }

    /// <summary>
    /// The palette index for <see cref="ColorTarget.Indexed"/>, or -1 when the change was wholesale.
    /// </summary>
    public int Index { get; }

    /// <summary>
    /// The new colour as 0xRRGGBB. Not meaningful when <see cref="Index"/> is -1.
    /// </summary>
    public int Rgb { get; }
}
