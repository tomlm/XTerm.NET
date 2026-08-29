namespace XTerm.Graphics;

/// <summary>
/// The images a client has transmitted and can ask to see again, kept by the id it gave them.
/// </summary>
/// <remarks>
/// <para>This exists because of one difference between Kitty and Sixel. A Sixel is drawn where it
/// arrives and is alive exactly as long as some cell shows it, which needs no bookkeeping at all --
/// the last cell to be overwritten or scrolled away takes the pixels with it. Kitty transmits a
/// picture under an id and may show it later, or never. An image with no placement is unreachable
/// from the cells, so something has to hold it, and something has to decide when to stop.</para>
/// <para>The rule here is a byte budget with the oldest going first. It is not the same question as
/// the on-screen budget in <c>Terminal</c>: this bounds what is held on the client's promise to use
/// it, that bounds what is actually being shown.</para>
/// </remarks>
internal sealed class ImageRegistry
{
    private readonly Dictionary<uint, TerminalImage> _byId = new();

    /// <summary>Insertion order, oldest first, so eviction has something to go on.</summary>
    private readonly LinkedList<uint> _order = new();

    private readonly Dictionary<uint, LinkedListNode<uint>> _nodes = new();

    private long _bytes;

    /// <summary>Total size of everything held.</summary>
    public long ByteCount => _bytes;

    /// <summary>How many images are held.</summary>
    public int Count => _byId.Count;

    /// <summary>The ids currently held, oldest first.</summary>
    public IEnumerable<uint> Ids => _order;

    /// <summary>The images currently held.</summary>
    public IEnumerable<TerminalImage> Images => _byId.Values;

    /// <summary>
    /// A client's image *number* mapped to the id the terminal handed out for it.
    /// </summary>
    /// <remarks>
    /// A number is not an id. A client that does not want to manage an id space sends
    /// <c>I=&lt;number&gt;</c>, and the terminal picks an id and reports it back. Sending the same
    /// number again makes a new image, and the number then refers to the newest -- which is why this
    /// overwrites rather than refusing.
    /// </remarks>
    private readonly Dictionary<uint, uint> _idByNumber = new();

    /// <summary>
    /// Stores an image under an id, replacing anything already there.
    /// </summary>
    /// <param name="number">The client's image number, or 0 if it named none.</param>
    public void Store(uint id, TerminalImage image, long budget, uint number = 0)
    {
        Remove(id);

        _byId[id] = image;
        _nodes[id] = _order.AddLast(id);
        _bytes += image.ByteCount;

        if (number != 0)
            _idByNumber[number] = id;

        Trim(budget);
    }

    /// <summary>
    /// Charges <paramref name="delta"/> bytes to an image already stored, and trims to budget.
    /// </summary>
    /// <returns>False if the addition would not fit, in which case nothing was charged.</returns>
    /// <remarks>
    /// An animation frame is the one thing that grows an image AFTER it was stored, so it is the
    /// one thing Store's accounting never saw: _bytes stayed at the size the image was registered
    /// at. Two consequences, both real -- several animations could each grow to the whole budget,
    /// and removing a grown image later subtracted its CURRENT size from a counter that had never
    /// been credited it, driving the total negative and disabling trimming entirely.
    /// </remarks>
    public bool TryCharge(long delta, long budget)
    {
        if (budget > 0 && _bytes + delta > budget)
            return false;

        _bytes += delta;
        Trim(budget);
        return true;
    }

    public bool TryGet(uint id, out TerminalImage image) => _byId.TryGetValue(id, out image!);

    /// <summary>Resolves a client image number to the newest image stored under it.</summary>
    public bool TryGetByNumber(uint number, out uint id, out TerminalImage image)
    {
        image = null!;
        return _idByNumber.TryGetValue(number, out id) && TryGet(id, out image);
    }

    /// <summary>
    /// Forgets an image found by its pixels rather than by its id.
    /// </summary>
    /// <remarks>
    /// A positional delete finds placements, and a placement knows its image but not the id it was
    /// stored under. Nothing indexes that direction, so this is a scan -- acceptable because a
    /// registry holds tens of images, not thousands, and deletes are a user-scale event.
    /// </remarks>
    public bool RemoveImage(TerminalImage image)
    {
        uint? found = null;
        foreach (var pair in _byId)
        {
            if (ReferenceEquals(pair.Value, image))
            {
                found = pair.Key;
                break;
            }
        }

        return found is { } id && Remove(id);
    }

    /// <summary>Forgets an image. The pixels survive as long as some cell still shows them.</summary>
    public bool Remove(uint id)
    {
        if (!_byId.TryGetValue(id, out var existing))
            return false;

        _bytes -= existing.ByteCount;
        _byId.Remove(id);

        if (_nodes.TryGetValue(id, out var node))
        {
            _order.Remove(node);
            _nodes.Remove(id);
        }

        // A number pointing at an id that no longer exists would resolve to nothing on the next
        // lookup, which is correct but leaks an entry per removed image over a long session.
        uint? staleNumber = null;
        foreach (var pair in _idByNumber)
        {
            if (pair.Value == id)
            {
                staleNumber = pair.Key;
                break;
            }
        }

        if (staleNumber is { } number)
            _idByNumber.Remove(number);

        return true;
    }

    public void Clear()
    {
        _byId.Clear();
        _order.Clear();
        _nodes.Clear();
        _idByNumber.Clear();
        _bytes = 0;
    }

    /// <summary>
    /// Drops the oldest images until the total fits the budget.
    /// </summary>
    /// <remarks>
    /// Dropping an id does not destroy the picture: cells showing it hold their own reference, so
    /// what is lost is the ability to place it again, not what is already on screen.
    /// </remarks>
    private void Trim(long budget)
    {
        if (budget <= 0)
            return;

        while (_bytes > budget && _order.First is { } oldest)
            Remove(oldest.Value);
    }

    /// <summary>
    /// The next id the terminal will hand out for a client that sent an image number rather than
    /// an id.
    /// </summary>
    /// <remarks>
    /// Counts down from the top of the range so it cannot collide with the small ids clients pick
    /// for themselves.
    /// </remarks>
    public uint NextAssignedId()
    {
        var candidate = _nextAssigned;
        while (_byId.ContainsKey(candidate) && candidate > 1)
            candidate--;

        _nextAssigned = candidate > 1 ? candidate - 1 : uint.MaxValue;
        return candidate;
    }

    private uint _nextAssigned = uint.MaxValue;
}
