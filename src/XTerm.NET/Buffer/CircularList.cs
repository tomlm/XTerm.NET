namespace XTerm.Buffer;

/// <summary>
/// Circular list implementation for terminal buffer lines.
/// Provides efficient wraparound behavior for scrollback buffer.
/// </summary>
public class CircularList<T> where T : class
{
    private T?[] _array;
    private int _startIndex;
    private int _length;

    public int MaxLength { get; private set; }

    public int Length => _length;

    public CircularList(int maxLength)
    {
        MaxLength = maxLength;
        _array = new T?[maxLength];
        _startIndex = 0;
        _length = 0;
    }

    public T? this[int index]
    {
        get
        {
            if (index < 0 || index >= _length)
                throw new IndexOutOfRangeException();
            return _array[GetCyclicIndex(index)];
        }
        set
        {
            if (index < 0 || index >= _length)
                throw new IndexOutOfRangeException();
            _array[GetCyclicIndex(index)] = value;
        }
    }

    /// <summary>
    /// The item the next <see cref="Push"/> would overwrite, when the list is at capacity.
    /// Returns false otherwise.
    ///
    /// Exists so a caller that is about to discard the oldest entry can reuse it instead of
    /// allocating a replacement. The caller must reset whatever it takes back: the item is still
    /// in the list until Push replaces it.
    /// </summary>
    public bool TryPeekEvictionCandidate(out T item)
    {
        if (_length == MaxLength && MaxLength > 0)
        {
            item = _array[GetCyclicIndex(0)]!;
            return item is not null;
        }

        item = default!;
        return false;
    }

    /// <summary>
    /// Pushes a new item to the end of the list.
    /// </summary>
    public void Push(T item)
    {
        if (_length == MaxLength)
        {
            // Overwrite oldest item
            _array[GetCyclicIndex(_length)] = item;
            _startIndex = (_startIndex + 1) % MaxLength;
        }
        else
        {
            _array[GetCyclicIndex(_length)] = item;
            _length++;
        }
    }

    /// <summary>
    /// Removes and returns the last item.
    /// </summary>
    public T? Pop()
    {
        if (_length == 0)
            return null;

        var index = GetCyclicIndex(_length - 1);
        var item = _array[index];
        _array[index] = null;
        _length--;
        return item;
    }

    /// <summary>
    /// Inserts items at a specific index.
    /// </summary>
    public void Splice(int start, int deleteCount, params T[] items)
    {
        if (start < 0 || start > _length)
            throw new IndexOutOfRangeException();

        // Remove items
        if (deleteCount > 0)
        {
            for (int i = start; i < _length - deleteCount; i++)
            {
                this[i] = this[i + deleteCount];
            }
            _length -= deleteCount;
        }

        // Insert items
        foreach (var item in items)
        {
            if (_length < MaxLength)
            {
                // First increase length
                _length++;

                // Then shift items right from the end
                for (int i = _length - 1; i > start; i--)
                {
                    this[i] = this[i - 1];
                }

                // Now insert the item
                this[start] = item;
                start++;
            }
            else
            {
                // At capacity an insert evicts the oldest element, so the result is the last
                // MaxLength entries of the list this splice would have produced with room to
                // grow. Push alone appended to the TAIL, so a splice into the middle of a full
                // list silently became an append -- a reflowed line landed at the bottom of the
                // scrollback instead of where the text was.

                // Insert at the front and the new item IS the element that falls off the end,
                // so it never becomes visible and nothing else moves.
                if (start == 0)
                    continue;

                Push(item);

                // Push put it at the tail and dropped the oldest, which shifted every survivor
                // down one: the element the caller pointed at now sits at start - 1, and that is
                // where the item goes to stay in front of it.
                var target = start - 1;
                for (var i = _length - 1; i > target; i--)
                {
                    this[i] = this[i - 1];
                }

                this[target] = item;

                // `start` deliberately does NOT advance here. Each insert also evicts, and the
                // two shifts cancel: the next item's slot has the same index as this one's.
            }
        }
    }

    /// <summary>
    /// Trims the list to a specific length.
    /// </summary>
    public void TrimStart(int count)
    {
        if (count <= 0)
            return;

        count = Math.Min(count, _length);

        // Release the trimmed slots. Advancing the start index alone left every trimmed line
        // referenced by the backing array until a later Push happened to overwrite that slot, so
        // CSI 3 J returned the scrollback's memory only as new output arrived to displace it.
        // Pop already nulls its slot for the same reason.
        for (var i = 0; i < count; i++)
        {
            _array[(_startIndex + i) % MaxLength] = default;
        }

        _startIndex = (_startIndex + count) % MaxLength;
        _length -= count;
    }

    /// <summary>
    /// Shifts the start index by a specified amount.
    /// </summary>
    public void ShiftElements(int start, int count, int direction)
    {
        if (direction > 0)
        {
            // Shift right
            for (int i = count - 1; i >= 0; i--)
            {
                if (start + i + direction < _length)
                {
                    this[start + i + direction] = this[start + i];
                }
            }
        }
        else if (direction < 0)
        {
            // Shift left
            for (int i = 0; i < count; i++)
            {
                if (start + i + direction >= 0)
                {
                    this[start + i + direction] = this[start + i];
                }
            }
        }
    }

    /// <summary>
    /// Recycles a line from the buffer, or creates a new one.
    /// </summary>
    public T? Recycle()
    {
        if (_length >= MaxLength)
        {
            return Pop();
        }
        return null;
    }

    /// <summary>
    /// Gets the actual array index for a logical index.
    /// </summary>
    private int GetCyclicIndex(int index)
    {
        return (_startIndex + index) % MaxLength;
    }

    /// <summary>
    /// Clears the list.
    /// </summary>
    public void Clear()
    {
        Array.Clear(_array, 0, _array.Length);
        _startIndex = 0;
        _length = 0;
    }

    /// <summary>
    /// Sets the logical length of the list. Used by buffer reflow batching.
    /// </summary>
    internal void SetLength(int newLength)
    {
        if (newLength < 0 || newLength > MaxLength)
            throw new ArgumentOutOfRangeException(nameof(newLength));
        _length = newLength;
    }

    /// <summary>
    /// Resizes the maximum length of the circular list.
    /// </summary>
    public void Resize(int newMaxLength)
    {
        if (newMaxLength == MaxLength)
            return;

        var newArray = new T?[newMaxLength];
        var copyLength = Math.Min(_length, newMaxLength);

        for (int i = 0; i < copyLength; i++)
        {
            newArray[i] = this[i];
        }

        _array = newArray;
        MaxLength = newMaxLength;
        _startIndex = 0;
        _length = copyLength;
    }

    /// <summary>
    /// Gets an enumerable of all items.
    /// </summary>
    public IEnumerable<T> GetItems()
    {
        for (int i = 0; i < _length; i++)
        {
            var item = this[i];
            if (item != null)
                yield return item;
        }
    }
}
