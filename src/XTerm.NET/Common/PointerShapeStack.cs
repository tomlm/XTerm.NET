namespace XTerm.Common;

/// <summary>
/// The pointer shapes one screen has asked for, most recent last (OSC 22).
/// </summary>
/// <remarks>
/// A stack rather than a single value because the point of the protocol is that a program can set a
/// shape and put back whatever was there before without knowing what that was: a pager that shows a
/// <c>wait</c> pointer while it loads pops back to the <c>text</c> pointer its host had set.
///
/// Bounded, and it drops from the BOTTOM when full. A program that pushes in a loop and never pops
/// is a leak otherwise, and of the two ends the oldest entry is the one nobody is going to pop back
/// to -- refusing the new push would instead leave the screen showing a shape the program thinks it
/// has replaced.
/// </remarks>
internal sealed class PointerShapeStack
{
    /// <summary>
    /// How many shapes are kept. The protocol requires at least 16.
    /// </summary>
    public const int MaxDepth = 16;

    private readonly List<string> _shapes = new();

    /// <summary>
    /// The shape at the top of the stack, or null when nothing is set and the terminal may choose.
    /// </summary>
    public string? Current => _shapes.Count > 0 ? _shapes[^1] : null;

    /// <summary>
    /// Pushes a shape, evicting the oldest when the stack is full.
    /// </summary>
    public void Push(string shape)
    {
        _shapes.Add(shape);
        if (_shapes.Count > MaxDepth)
            _shapes.RemoveAt(0);
    }

    /// <summary>
    /// Replaces the current shape, leaving the rest of the stack alone.
    /// </summary>
    public void Set(string shape)
    {
        if (_shapes.Count == 0)
            Push(shape);
        else
            _shapes[^1] = shape;
    }

    /// <summary>
    /// Removes the current shape, restoring the one beneath it. Popping an empty stack does nothing.
    /// </summary>
    public void Pop()
    {
        if (_shapes.Count > 0)
            _shapes.RemoveAt(_shapes.Count - 1);
    }

    /// <summary>
    /// Empties the stack, so the terminal is free to use its own pointer again.
    /// </summary>
    public void Clear() => _shapes.Clear();
}
