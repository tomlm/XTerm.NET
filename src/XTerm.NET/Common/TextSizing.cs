namespace XTerm.Common;

/// <summary>
/// Where a fractionally scaled render area sits vertically inside the block it was given
/// (the <c>v</c> key of OSC 66).
/// </summary>
public enum TextSizeVerticalAlignment
{
    /// <summary>Against the top of the block (<c>v=0</c>, the default).</summary>
    Top = 0,

    /// <summary>Against the bottom of the block (<c>v=1</c>).</summary>
    Bottom = 1,

    /// <summary>Centred in the block (<c>v=2</c>).</summary>
    Center = 2,
}

/// <summary>
/// Where a fractionally scaled render area sits horizontally inside the block it was given
/// (the <c>h</c> key of OSC 66).
/// </summary>
public enum TextSizeHorizontalAlignment
{
    /// <summary>Against the left of the block (<c>h=0</c>, the default).</summary>
    Left = 0,

    /// <summary>Against the right of the block (<c>h=1</c>).</summary>
    Right = 1,

    /// <summary>Centred in the block (<c>h=2</c>).</summary>
    Center = 2,
}

/// <summary>
/// The metadata of a Kitty text sizing sequence — <c>OSC 66 ; key=value : ... ; text ST</c>.
/// </summary>
/// <remarks>
/// <para>Two independent things live here, and a terminal may honour either without the other.
/// The WIDTH half (<see cref="Width"/>) says how many cells the text occupies, which is what fixes
/// the long-standing disagreement between a program's idea of a string's width and the terminal's.
/// The SCALE half (<see cref="Scale"/>, plus the <see cref="Numerator"/>/<see cref="Denominator"/>
/// fraction) says how large to draw it, and needs a renderer that can lay glyphs out at a multiple
/// of the cell metrics.</para>
/// <para>The emulator owns the width half completely: scaled text really does claim
/// <c>Scale * Width</c> columns, so the cursor, selection and reflow all agree with the client about
/// how much room a run took. The scale half it can only record — see
/// <see cref="Buffer.LineSizedRun"/> for what a renderer is handed and what it is expected to do
/// with it.</para>
/// </remarks>
public readonly struct TextSizing : IEquatable<TextSizing>
{
    /// <summary>Ordinary text: one cell per column, drawn at the base font size.</summary>
    public static TextSizing Default => new(1, 0, 0, 0, TextSizeVerticalAlignment.Top, TextSizeHorizontalAlignment.Left);

    /// <summary>The largest <c>s</c> the protocol allows.</summary>
    public const int MaxScale = 7;

    /// <summary>The largest <c>w</c> the protocol allows.</summary>
    public const int MaxWidth = 7;

    /// <summary>The largest <c>n</c> or <c>d</c> the protocol allows.</summary>
    public const int MaxFractionTerm = 15;

    /// <summary>
    /// The <c>s</c> key: the whole-number scale, 1 to 7. Text is drawn in a block
    /// <c>Scale * Width</c> cells wide and <see cref="Scale"/> cells tall.
    /// </summary>
    public readonly int Scale;

    /// <summary>
    /// The <c>w</c> key: how many scaled cells the whole run occupies, or 0 — the default — for
    /// "as many as the text would normally take", each of them scaled.
    /// </summary>
    public readonly int Width;

    /// <summary>The <c>n</c> key: the numerator of the fractional scale, or 0 for none.</summary>
    public readonly int Numerator;

    /// <summary>The <c>d</c> key: the denominator of the fractional scale, or 0 for none.</summary>
    public readonly int Denominator;

    /// <summary>The <c>v</c> key. Only meaningful when <see cref="IsFractional"/>.</summary>
    public readonly TextSizeVerticalAlignment VerticalAlignment;

    /// <summary>The <c>h</c> key. Only meaningful when <see cref="IsFractional"/>.</summary>
    public readonly TextSizeHorizontalAlignment HorizontalAlignment;

    public TextSizing(
        int scale,
        int width,
        int numerator,
        int denominator,
        TextSizeVerticalAlignment verticalAlignment,
        TextSizeHorizontalAlignment horizontalAlignment)
    {
        Scale = scale;
        Width = width;
        Numerator = numerator;
        Denominator = denominator;
        VerticalAlignment = verticalAlignment;
        HorizontalAlignment = horizontalAlignment;
    }

    /// <summary>
    /// Whether a fraction is in force, which is the only case the alignments apply to.
    /// </summary>
    /// <remarks>
    /// A fraction shrinks the glyphs WITHIN the cells the run already claims; it never changes how
    /// many cells that is. <c>n=d</c> is a fraction of one and so is no fraction at all.
    /// </remarks>
    public bool IsFractional => Denominator > 0 && Numerator > 0 && Numerator < Denominator;

    /// <summary>Whether this asks for anything a plain renderer does not already do.</summary>
    public bool IsDefault => Scale == 1 && Width == 0 && !IsFractional;

    /// <summary>
    /// Parses the metadata part of an OSC 66 sequence — the colon separated <c>key=value</c> list.
    /// </summary>
    /// <remarks>
    /// Returns false for anything out of range rather than clamping it. A client that asks for
    /// <c>s=99</c> has a bug, and drawing its heading at some other size hides that bug while still
    /// producing the wrong output; leaving the whole sequence unhandled at least says so.
    /// </remarks>
    public static bool TryParse(string? metadata, out TextSizing sizing)
    {
        var scale = 1;
        var width = 0;
        var numerator = 0;
        var denominator = 0;
        var vertical = TextSizeVerticalAlignment.Top;
        var horizontal = TextSizeHorizontalAlignment.Left;

        sizing = Default;

        if (!string.IsNullOrEmpty(metadata))
        {
            foreach (var pair in metadata.Split(':'))
            {
                if (pair.Length == 0)
                    continue;

                var eq = pair.IndexOf('=');
                if (eq <= 0 || !int.TryParse(pair.AsSpan(eq + 1), out var value) || value < 0)
                    return false;

                // Single-letter keys, all of them. Anything else is from a later revision of the
                // protocol than this, and is not something to guess at.
                if (eq != 1)
                    return false;

                switch (pair[0])
                {
                    case 's':
                        if (value < 1 || value > MaxScale)
                            return false;
                        scale = value;
                        break;

                    case 'w':
                        if (value > MaxWidth)
                            return false;
                        width = value;
                        break;

                    case 'n':
                        if (value > MaxFractionTerm)
                            return false;
                        numerator = value;
                        break;

                    case 'd':
                        if (value > MaxFractionTerm)
                            return false;
                        denominator = value;
                        break;

                    case 'v':
                        if (value > 2)
                            return false;
                        vertical = (TextSizeVerticalAlignment)value;
                        break;

                    case 'h':
                        if (value > 2)
                            return false;
                        horizontal = (TextSizeHorizontalAlignment)value;
                        break;

                    default:
                        return false;
                }
            }
        }

        // "Must be > n when non-zero", per the protocol. A denominator no larger than its numerator
        // is not a fraction of anything.
        if (denominator != 0 && denominator <= numerator)
            return false;

        sizing = new TextSizing(scale, width, numerator, denominator, vertical, horizontal);
        return true;
    }

    public bool Equals(TextSizing other)
        => Scale == other.Scale
           && Width == other.Width
           && Numerator == other.Numerator
           && Denominator == other.Denominator
           && VerticalAlignment == other.VerticalAlignment
           && HorizontalAlignment == other.HorizontalAlignment;

    public override bool Equals(object? obj) => obj is TextSizing other && Equals(other);

    public override int GetHashCode()
        => HashCode.Combine(Scale, Width, Numerator, Denominator, VerticalAlignment, HorizontalAlignment);

    public static bool operator ==(TextSizing left, TextSizing right) => left.Equals(right);

    public static bool operator !=(TextSizing left, TextSizing right) => !left.Equals(right);

    public override string ToString()
        => IsFractional
            ? $"s={Scale}:w={Width}:n={Numerator}:d={Denominator}:v={(int)VerticalAlignment}:h={(int)HorizontalAlignment}"
            : $"s={Scale}:w={Width}";
}
