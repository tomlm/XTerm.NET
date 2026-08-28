namespace XTerm.Input;

/// <summary>
/// The terminal-side state of the Kitty keyboard protocol: which enhancement flags are active,
/// and the stack an application pushes them to on entry and pops on exit.
/// </summary>
/// <remarks>
/// <para>The model is kitty's own (kitty/screen.c): the active flags ARE the top of the stack.
/// <c>CSI = flags u</c> writes into the top entry — creating one when the stack is empty, exactly
/// as <c>screen_set_key_encoding_flags</c> does — and a push appends the NEW flags rather than
/// saving the old ones. That one-tier shape is what makes the ordinary lifecycle safe: a shell
/// that set base flags, then an application that pushes on entry and pops on exit, leaves the
/// shell's flags active, because they were on the stack all along. A save/restore model with the
/// base state outside the stack pops one level too early and zeroes them.</para>
/// <para>Kept PER SCREEN. A full-screen application sets its flags on the alternate screen, and
/// they must not leak: if vim crashes without popping, switching back to the main screen still
/// restores the shell's flags, because the shell's screen kept its own stack. This is the same
/// rule the protocol's designers wrote for exactly that failure.</para>
/// <para>Which screen's stack a sequence lands on follows the active buffer, tracked here by
/// <see cref="SwitchScreen"/> so the CSI handlers do not each re-derive it.</para>
/// </remarks>
public sealed class KittyKeyboardState
{
    /// <summary>
    /// The stack depth an application can accumulate before the oldest entry is dropped.
    /// A push beyond it evicts from the bottom rather than failing, per the spec's advice that
    /// terminals bound the stack — an application looping on push must not grow memory forever.
    /// </summary>
    private const int MaxStackDepth = 16;

    /// <summary>
    /// The five bits the protocol defines. kitty masks to these on both set and push
    /// (<c>val &amp; 0x7f</c> less its private stack-occupied marker), so an out-of-range value
    /// can neither echo back through the query nor hold the protocol "active" with no bit the
    /// encoder understands.
    /// </summary>
    private const KittyKeyboardFlags DefinedFlags = (KittyKeyboardFlags)0b11111;

    private readonly List<KittyKeyboardFlags> _mainStack = new();
    private readonly List<KittyKeyboardFlags> _altStack = new();
    private bool _onAltScreen;

    private List<KittyKeyboardFlags> Stack => _onAltScreen ? _altStack : _mainStack;

    /// <summary>
    /// The enhancement flags active on the current screen: the top of its stack, or
    /// <see cref="KittyKeyboardFlags.None"/> when the stack is empty (protocol off).
    /// </summary>
    public KittyKeyboardFlags Flags => Stack.Count > 0 ? Stack[^1] : KittyKeyboardFlags.None;

    /// <summary>
    /// Sets the flags per <c>CSI = flags ; mode u</c>: mode 1 assigns, mode 2 sets only the
    /// given bits, mode 3 clears only the given bits. Any other mode does nothing — kitty takes
    /// no branch for an unknown mode, and an explicit 0 is unknown, not an alias for 1.
    /// </summary>
    internal void Set(KittyKeyboardFlags flags, int mode)
    {
        if (mode is < 1 or > 3)
            return;

        flags &= DefinedFlags;
        var stack = Stack;
        if (stack.Count == 0)
            stack.Add(KittyKeyboardFlags.None);

        stack[^1] = mode switch
        {
            1 => flags,
            2 => stack[^1] | flags,
            _ => stack[^1] & ~flags,
        };
    }

    /// <summary>Pushes the given flags onto this screen's stack, making them the active ones.</summary>
    internal void Push(KittyKeyboardFlags flags)
    {
        var stack = Stack;
        if (stack.Count >= MaxStackDepth)
            stack.RemoveAt(0);
        stack.Add(flags & DefinedFlags);
    }

    /// <summary>
    /// Pops entries from this screen's stack per <c>CSI &lt; count u</c>. No special case: the
    /// spec's "a pop that empties the stack resets all flags" falls out of <see cref="Flags"/>
    /// reading an empty stack as <see cref="KittyKeyboardFlags.None"/>.
    /// </summary>
    internal void Pop(int count)
    {
        var stack = Stack;
        for (var i = 0; i < count && stack.Count > 0; i++)
            stack.RemoveAt(stack.Count - 1);
    }

    /// <summary>
    /// Moves to the other screen, whose stack — and therefore whose flags — then answers for
    /// everything. Called from the buffer switch itself so every path that changes screens
    /// carries the flags with it.
    /// </summary>
    internal void SwitchScreen(bool toAltScreen) => _onAltScreen = toAltScreen;

    /// <summary>
    /// Returns everything to protocol-off. RIS is how a user recovers from an application that
    /// set flags and died, so reset must clear both screens' stacks.
    /// </summary>
    internal void Reset()
    {
        _mainStack.Clear();
        _altStack.Clear();
        _onAltScreen = false;
    }
}
