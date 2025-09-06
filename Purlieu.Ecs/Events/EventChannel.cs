using System.Runtime.CompilerServices;

namespace PurlieuEcs.Events;

/// <summary>
/// Interface for type-erased event channel operations.
/// </summary>
internal interface IEventChannel
{
    void Clear();
}

/// <summary>
/// Fixed-size ring buffer for high-performance event handling.
/// </summary>
public sealed class EventChannel<T> : IEventChannel where T : struct
{
    private readonly T[] _events;
    private readonly int _capacity;
    private int _writeIndex;
    private int _readIndex;
    private int _count;
    
    public EventChannel(int capacity = 1024)
    {
        _capacity = capacity;
        _events = new T[capacity];
        _writeIndex = 0;
        _readIndex = 0;
        _count = 0;
    }
    
    /// <summary>
    /// Publishes an event to the channel.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Publish(in T eventData)
    {
        _events[_writeIndex] = eventData;
        _writeIndex = (_writeIndex + 1) % _capacity;
        
        if (_count < _capacity)
        {
            _count++;
        }
        else
        {
            // Overwrite oldest event
            _readIndex = (_readIndex + 1) % _capacity;
        }
    }
    
    /// <summary>
    /// Consumes all available events.
    /// </summary>
    public void ConsumeAll(Action<T> handler)
    {
        while (_count > 0)
        {
            handler(_events[_readIndex]);
            _readIndex = (_readIndex + 1) % _capacity;
            _count--;
        }
    }
    
    /// <summary>
    /// Consumes all available events with a delegate for zero allocation.
    /// </summary>
    public void ConsumeAll<TState>(TState state, EventHandler<T, TState> handler)
        where TState : struct
    {
        while (_count > 0)
        {
            handler(in _events[_readIndex], ref state);
            _readIndex = (_readIndex + 1) % _capacity;
            _count--;
        }
    }
    
    /// <summary>
    /// Clears all events in the channel.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Clear()
    {
        _readIndex = 0;
        _writeIndex = 0;
        _count = 0;
    }
    
    /// <summary>
    /// Gets the current number of events in the channel.
    /// </summary>
    public int Count => _count;
    
    /// <summary>
    /// Gets whether the channel is empty.
    /// </summary>
    public bool IsEmpty => _count == 0;
    
    /// <summary>
    /// Gets whether the channel is full.
    /// </summary>
    public bool IsFull => _count == _capacity;
}

/// <summary>
/// Event handler delegate for zero-allocation event processing.
/// </summary>
public delegate void EventHandler<T, TState>(in T eventData, ref TState state);