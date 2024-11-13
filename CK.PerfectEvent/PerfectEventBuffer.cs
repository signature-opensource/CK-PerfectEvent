using CK.Core;
using System;
using System.Threading.Tasks;
using System.Threading;
using System.Runtime.CompilerServices;

namespace CK.PerfectEvent;

/// <summary>
/// Allows to wait for one or more event raised by a <see cref="PerfectEvent{TEvent}"/>.
/// <para>
/// This does NOT handle concurrency: <see cref="WaitForAsync(int, CancellationToken)"/> or <see cref="WaitForOneAsync(CancellationToken)"/>
/// must be called sequentially. This should mainly be used in tests.
/// </para>
/// </summary>
/// <typeparam name="TEvent">The event type.</typeparam>
public sealed class PerfectEventBuffer<TEvent> : IDisposable
{
    readonly FIFOBuffer<TEvent> _collector;
    readonly PerfectEvent<TEvent> _e;
    TaskCompletionSource<TEvent[]>? _waitingTask;
    int _waitingCount;
    private CancellationTokenRegistration _cancellationRegistration;

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
    /// Waits for a given number of event and returns them (or a canceled task if <paramref name="cancellation"/> has been signaled).
    /// <para>
    /// This throws a <see cref="InvalidOperationException"/> if a wait is already pending.
    /// </para>
    /// </summary>
    /// <param name="count">Number of events. Must be positive.</param>
    /// <param name="cancellation">Optional cancellation token.</param>
    /// <returns>The <paramref name="count"/> events.</returns>
    public Task<TEvent[]> WaitForAsync( int count, CancellationToken cancellation = default )
    {
        Throw.CheckArgument( count > 0 );
        lock( _collector )
        {
            Throw.CheckState( "No concurrent WaitForAsync is supported.", _waitingTask == null );
            if( _collector.Count >= count )
            {
                return Task.FromResult( Drain( count ) );
            }
            _waitingTask = new TaskCompletionSource<TEvent[]>( TaskCreationOptions.RunContinuationsAsynchronously );
            if( cancellation.CanBeCanceled )
            {
                _cancellationRegistration = cancellation.UnsafeRegister( OnCancel, _waitingTask );
                if( _waitingTask == null )
                {
                    Throw.DebugAssert( cancellation.IsCancellationRequested );
                    return Task.FromCanceled<TEvent[]>( cancellation );
                }
            }
            _waitingCount = count;
            return _waitingTask.Task!;
        }
    }

    void OnCancel( object? task, CancellationToken token )
    {
        if( _waitingTask != null && _waitingTask == task )
        {
            TaskCompletionSource<TEvent[]>? w = null;
            lock( _collector )
            {
                if( _waitingTask != null && _waitingTask == task )
                {
                    w = _waitingTask;
                    _waitingCount = 0;
                    _waitingTask = null;
                }
            }
            w?.TrySetCanceled( token );
        }
    }

    /// <summary>
    /// Waits for a single event and returns it (or a canceled task if <paramref name="cancellation"/> has been signaled).
    /// <para>
    /// This throws a <see cref="InvalidOperationException"/> if a wait is already pending.
    /// </para>
    /// </summary>
    /// <param name="cancellation">Optional cancellation token.</param>
    /// <returns>The single event.</returns>
    public async Task<TEvent> WaitForOneAsync( CancellationToken cancellation = default ) => (await WaitForAsync( 1, cancellation ).ConfigureAwait( false ))[0];

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
        CancellationTokenRegistration cancellationRegistration = default;
        lock( _collector )
        {
            _collector.Push( e );
            if( _waitingTask != null && _collector.Count >= _waitingCount )
            {
                r = Drain( _waitingCount );
                w = _waitingTask;
                _waitingTask = null;
                cancellationRegistration = _cancellationRegistration;
            }
        }
        if( w != null )
        {
            cancellationRegistration.Dispose();
            Throw.DebugAssert( r != null );
            w.TrySetResult( r );
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

