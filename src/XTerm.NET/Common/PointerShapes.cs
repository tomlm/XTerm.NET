namespace XTerm.Common;

/// <summary>
/// The shape names an application may ask for with OSC 22, from Kitty's pointer shape protocol.
/// </summary>
/// <remarks>
/// The names are the CSS <c>cursor</c> values, which is what makes the protocol portable: the host
/// maps a name onto whatever its toolkit calls the same pointer, rather than onto an X11 cursor font
/// index that means nothing off X11.
///
/// Anything not in this table is refused rather than passed along. A host is expected to switch on
/// the name, and a query has to answer honestly whether a name is supported, so an unknown name is
/// worth nothing to either -- and refusing it also keeps arbitrary application bytes out of the
/// reply the terminal writes back.
/// </remarks>
public static class PointerShapes
{
    /// <summary>
    /// The shape a terminal uses when no application has asked for one.
    /// </summary>
    public const string Default = "default";

    /// <summary>
    /// The shape a terminal uses while the pointer is grabbed, i.e. dragging.
    /// </summary>
    public const string Grabbed = "grabbing";

    private static readonly HashSet<string> Names = new(StringComparer.Ordinal)
    {
        "alias",
        "cell",
        "copy",
        "crosshair",
        "default",
        "e-resize",
        "ew-resize",
        "grab",
        "grabbing",
        "help",
        "move",
        "n-resize",
        "ne-resize",
        "nesw-resize",
        "no-drop",
        "not-allowed",
        "ns-resize",
        "nw-resize",
        "nwse-resize",
        "pointer",
        "progress",
        "s-resize",
        "se-resize",
        "sw-resize",
        "text",
        "vertical-text",
        "w-resize",
        "wait",
        "zoom-in",
        "zoom-out",
    };

    /// <summary>
    /// Every shape name this terminal accepts.
    /// </summary>
    public static IReadOnlyCollection<string> All => Names;

    /// <summary>
    /// Whether <paramref name="name"/> is one of the shapes this terminal accepts.
    /// </summary>
    public static bool IsKnown(string? name) => name is not null && Names.Contains(name);
}
