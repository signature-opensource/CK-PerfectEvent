using CK.Core;
using System;
using System.Threading.Tasks;
using System.Threading;

namespace CK.PerfectEvent;

/// <summary>
/// Allows to wait for one or more event raised by a <see cref="PerfectEvent{TEvent}"/>.
/// <para>
/// This is NOT thread safe and should mainly be used in tests.
/// </para>
/// </summary>
/// <typeparam name="TEvent">The event type.</typeparam>
public sealed class PerfectEventBuffer<TEvent> : IDisposable
{
    readonly FIFOBuffer<TEvent> _collector;
    readonly PerfectEvent<TEvent> _e;
    TaskCompletionSource<TEvent[]>? _waitingTask;
    int _waitingCount;

    /// <summary>
    /// Initialize a new buffer with a fixed size.
    /// This can be changed by setting <see cref="Capacity"/> and <see cref="MaxDynamicCapacity"/>.
    /// <para>
    /// Events are immediately collected: <see cref="Dispose()"/> must be called.
    /// </para>
    /// </summary>
    /// <param name="e">The perfect event to monitor.</param>
    public PerfectEventBuffer( PerfectEvent<TEvent> e, int capacity )
    {
        _e = e;
        _collector = new FIFOBuffer<TEvent>( capacity );
        _e.Sync += OnEvent;
    }

    /// <summary>
    /// Initialize a new buffer with an initial capacity (can be 0) and a maximal dynamic capacity.
    /// This can be changed by setting <see cref="Capacity"/> and <see cref="MaxDynamicCapacity"/>.
    /// <para>
    /// Events are immediately collected: <see cref="Dispose()"/> must be called.
    /// </para>
    /// </summary>
    /// <param name="e">The perfect event to monitor.</param>
    /// <param name="capacity">Initial capacity.</param>
    /// <param name="maxDynamicCapacity">Initial maximal capacity: the <see cref="Capacity"/> will automatically grow until up to this size.</param>
    public PerfectEventBuffer( PerfectEvent<TEvent> e, int capacity = 0, int maxDynamicCapacity = 200 )
    {
        _e = e;
        _collector = new FIFOBuffer<TEvent>( capacity, maxDynamicCapacity );
        _e.Sync += OnEvent;
    }

    /// <summary>
    /// Unsubscribes from the event.
    /// </summary>
    public void Dispose()
    {
        _e.Sync -= OnEvent;
        _collector.Clear();
    }

    /// <summary>
    /// Waits for a given number of event and returns them.
    /// </summary>
    /// <param name="count">Number of events. Must be positive.</param>
    /// <returns>The <paramref name="count"/> events.</returns>
    public Task<TEvent[]> WaitForAsync( int count )
    {
        Throw.CheckArgument( count > 0 );
        lock( _collector )
        {
            Throw.CheckState( "No concurrent WaitForAsync is supported.", _waitingTask == null );
            if( _collector.Count >= count )
            {
                return Task.FromResult( Drain( count ) );
            }
            _waitingTask = new TaskCompletionSource<TEvent[]>();
            _waitingCount = count;
            return _waitingTask.Task!;
        }
    }

    /// <summary>
    /// Waits for a given number of event and returns them (or returns a canceled task).
    /// </summary>
    /// <param name="count">Number of events. Must be positive.</param>
    /// <param name="cancellation">Cancellation token.</param>
    /// <returns>The <paramref name="count"/> events.</returns>
    public Task<TEvent[]> WaitForAsync( int count, CancellationToken cancellation ) => WaitForAsync( count ).WaitAsync( cancellation );

    /// <summary>
    /// Waits for a given number of event and returns them (or returns a faulted task with a <see cref="TimeoutException"/>).
    /// </summary>
    /// <param name="count">Number of events. Must be positive.</param>
    /// <param name="timeout">
    /// The timeout after which the Task should be faulted with
    /// a <see cref="TimeoutException"/> if it hasn't otherwise completed.
    /// </param>
    /// <returns>The <paramref name="count"/> events.</returns>
    public Task<TEvent[]> WaitForAsync( int count, TimeSpan timeout ) => WaitForAsync( count ).WaitAsync( timeout );

    /// <summary>
    /// Waits for a given number of event and returns them (or returns a canceled or faulted task with a <see cref="TimeoutException"/>).
    /// </summary>
    /// <param name="count">Number of events. Must be positive.</param>
    /// <param name="timeout">
    /// The timeout after which the Task should be faulted with
    /// a <see cref="TimeoutException"/> if it hasn't otherwise completed.
    /// </param>
    /// <param name="cancellation">Cancellation token.</param>
    /// <returns>The <paramref name="count"/> events.</returns>
    public Task<TEvent[]> WaitForAsync( int count, TimeSpan timeout, CancellationToken cancellation ) => WaitForAsync( count, cancellation ).WaitAsync( timeout, cancellation );

    /// <summary>
    /// Waits for a single event and returns it.
    /// </summary>
    /// <returns>The single event.</returns>
    public async Task<TEvent> WaitForOneAsync() => (await WaitForAsync( 1 ).ConfigureAwait( false ))[0];

    /// <summary>
    /// Waits for a single event and returns it (or returns a canceled task).
    /// </summary>
    /// <param name="cancellation">Cancellation token.</param>
    /// <returns>The single event.</returns>
    public async Task<TEvent> WaitForOneAsync( CancellationToken cancellation ) => (await WaitForAsync( 1, cancellation ).ConfigureAwait( false ))[0];

    /// <summary>
    /// Waits for a single event and returns it (or returns a faulted task with a <see cref="TimeoutException"/>).
    /// </summary>
    /// <param name="timeout">
    /// The timeout after which the Task should be faulted with
    /// a <see cref="TimeoutException"/> if it hasn't otherwise completed.
    /// </param>
    /// <returns>The single event.</returns>
    public async Task<TEvent> WaitForOneAsync( TimeSpan timeout ) => (await WaitForAsync( 1, timeout ).ConfigureAwait( false ))[0];

    /// <summary>
    /// Waits for a single event and returns it (or returns a canceled or faulted task with a <see cref="TimeoutException"/>).
    /// </summary>
    /// <param name="timeout">
    /// The timeout after which the Task should be faulted with
    /// a <see cref="TimeoutException"/> if it hasn't otherwise completed.
    /// </param>
    /// <param name="cancellation">Cancellation token.</param>
    /// <returns>The single event.</returns>
    public async Task<TEvent> WaitForOneAsync( TimeSpan timeout, CancellationToken cancellation ) => (await WaitForAsync( 1, timeout, cancellation ).ConfigureAwait( false ))[0];

    /// <summary>
    /// Gets or sets the capacity (internal buffer will be resized). If the new explicit
    /// capacity is greater than the <see cref="MaxDynamicCapacity"/>, then the
    /// MaxDynamicCapacity is forgotten (set to 0): the buffer is no more dynamic.
    /// </summary>
    public int Capacity
    {
        get => _collector.Capacity;
        set => _collector.Capacity = value;
    }

    /// <summary>
    ///  Gets or sets whether the <see cref="Capacity"/> is dynamic thanks to a
    ///  non zero maximal capacity. <see cref="Array.MaxLength"/> is the maximal size.
    ///  Defaults to 0 (fixed capacity).
    /// </summary>
    public int MaxDynamicCapacity
    {
        get => _collector.MaxDynamicCapacity;
        set => _collector.MaxDynamicCapacity = value;
    }

    void OnEvent( IActivityMonitor monitor, TEvent e )
    {
        TaskCompletionSource<TEvent[]>? w = null;
        TEvent[]? r = null;
        lock( _collector )
        {
            _collector.Push( e );
            if( _waitingTask != null && _collector.Count >= _waitingCount )
            {
                r = Drain( _waitingCount );
                w = _waitingTask;
                _waitingTask = null;
            }
        }

        if( w != null )
        {
            Throw.DebugAssert( r != null );
            w?.SetResult( r );
        }
    }

    TEvent[] Drain( int count )
    {
        TEvent[]? r = new TEvent[count];
        _collector.PopRange( r );
        _waitingCount = 0;
        return r;
    }
}

