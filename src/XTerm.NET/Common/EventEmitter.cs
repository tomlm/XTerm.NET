namespace XTerm.NET.Common;

/// <summary>
/// Generic event emitter for terminal events.
/// </summary>
public class EventEmitter<T>
{
    private readonly List<Action<T>> _listeners = new();

    /// <summary>
    /// Adds an event listener.
    /// </summary>
    public IDisposable Event(Action<T> listener)
    {
        _listeners.Add(listener);
        return new Disposable(() => _listeners.Remove(listener));
    }

    /// <summary>
    /// Fires the event to all listeners.
    /// </summary>
    public void Fire(T data)
    {
        foreach (var listener in _listeners.ToList())
        {
            listener(data);
        }
    }

    /// <summary>
    /// Clears all listeners.
    /// </summary>
    public void Clear()
    {
        _listeners.Clear();
    }

    private class Disposable : IDisposable
    {
        private readonly Action _dispose;

        public Disposable(Action dispose)
        {
            _dispose = dispose;
        }

        public void Dispose()
        {
            _dispose();
        }
    }
}

/// <summary>
/// Event emitter without data.
/// </summary>
public class EventEmitter
{
    private readonly List<Action> _listeners = new();

    /// <summary>
    /// Adds an event listener.
    /// </summary>
    public IDisposable Event(Action listener)
    {
        _listeners.Add(listener);
        return new Disposable(() => _listeners.Remove(listener));
    }

    /// <summary>
    /// Fires the event to all listeners.
    /// </summary>
    public void Fire()
    {
        foreach (var listener in _listeners.ToList())
        {
            listener();
        }
    }

    /// <summary>
    /// Clears all listeners.
    /// </summary>
    public void Clear()
    {
        _listeners.Clear();
    }

    private class Disposable : IDisposable
    {
        private readonly Action _dispose;

        public Disposable(Action dispose)
        {
            _dispose = dispose;
        }

        public void Dispose()
        {
            _dispose();
        }
    }
}
