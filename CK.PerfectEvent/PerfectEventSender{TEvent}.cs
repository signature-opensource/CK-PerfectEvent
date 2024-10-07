using CK.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;

namespace CK.PerfectEvent;


/// <summary>
/// A perfect event sender offers synchronous, asynchronous and parallel asynchronous event support.
/// <para>
/// Instances of this class should be kept private: only the sender object should be able to call <see cref="RaiseAsync(IActivityMonitor, TEvent, CancellationToken)"/>
/// or <see cref="SafeRaiseAsync(IActivityMonitor, TEvent, CancellationToken, string?, int)"/>.
/// What should be exposed is the <see cref="PerfectEvent"/> property that restricts the API to event registration and bridge management.
/// </para>
/// </summary>
/// <typeparam name="TEvent">The type of the event argument.</typeparam>
public sealed class PerfectEventSender<TEvent> : IPerfectEventSender
{
    DelegateListImpl _seq;
    DelegateListImpl _seqAsync;
    DelegateListImpl _parallelAsync;
    IInternalBridge[] _activeBridges = Array.Empty<IInternalBridge>();
    Action? _hasHandlersChanged;

    /// <summary>
    /// Gets the event that should be exposed to the external world: through the <see cref="PerfectEvent{TEvent}"/>,
    /// only registration/unregistration and bridging is possible.
    /// </summary>
    public PerfectEvent<TEvent> PerfectEvent => new PerfectEvent<TEvent>( this );

    /// <inheritdoc />
    public bool HasHandlers => _seq.HasHandlers || _seqAsync.HasHandlers || _parallelAsync.HasHandlers || _activeBridges.Length > 0;

    /// <inheritdoc />
    public bool AllowMultipleEvents { get; set; }

    /// <summary>
    /// Clears the registered handlers of this sender.
    /// </summary>
    public void RemoveAll()
    {
        if( _seq.RemoveAll() || _seqAsync.RemoveAll() || _parallelAsync.RemoveAll() ) _hasHandlersChanged?.Invoke();
    }

    void AddActiveBridge( IInternalBridge b )
    {
        if( Util.InterlockedAdd( ref _activeBridges, b ).Length == 1 )
        {
            _hasHandlersChanged?.Invoke();
        }
    }

    void RemoveActiveBridge( IInternalBridge b )
    {
        if( Util.InterlockedRemove( ref _activeBridges, b ).Length == 0 )
        {
            _hasHandlersChanged?.Invoke();
        }
    }

    internal void AddSeq( Delegate handler )
    {
        if( _seq.Add( handler ) ) _hasHandlersChanged?.Invoke();
    }

    internal void RemoveSeq( Delegate handler )
    {
        if( _seq.Remove( handler ) ) _hasHandlersChanged?.Invoke();
    }

    internal void AddAsyncSeq( Delegate handler )
    {
        if( _seqAsync.Add( handler ) ) _hasHandlersChanged?.Invoke();
    }

    internal void RemoveAsyncSeq( Delegate handler )
    {
        if( _seqAsync.Remove( handler ) ) _hasHandlersChanged?.Invoke();
    }

    internal void AddAsyncParallel( Delegate handler )
    {
        if( _parallelAsync.Add( handler ) ) _hasHandlersChanged?.Invoke();
    }

    internal void RemoveAsyncParallel( Delegate handler )
    {
        if( _parallelAsync.Remove( handler ) ) _hasHandlersChanged?.Invoke();
    }

    /// <summary>
    /// Same as <see cref="RaiseAsync"/> except that if exceptions occurred they are caught and logged
    /// and a gentle false is returned.
    /// <para>
    /// The returned task is resolved once the parallels, the synchronous and the asynchronous event handlers have finished their jobs.
    /// </para>
    /// <para>
    /// If exceptions occurred, they are logged and false is returned.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="e">The argument of the event.</param>
    /// <param name="cancel">Optional cancellation token.</param>
    /// <param name="fileName">The source filename where this event is raised.</param>
    /// <param name="lineNumber">The source line number in the filename where this event is raised.</param>
    /// <returns>True on success, false if an exception occurred.</returns>
    public async Task<bool> SafeRaiseAsync( IActivityMonitor monitor,
                                            TEvent e,
                                            CancellationToken cancel = default,
                                            [CallerFilePath] string? fileName = null,
                                            [CallerLineNumber] int lineNumber = 0 )
    {
        try
        {
            await RaiseAsync( monitor, e, cancel ).ConfigureAwait( false );
            return true;
        }
        catch( Exception ex )
        {
            if( monitor.ShouldLogLine( LogLevel.Error, null, out var finalTags ) )
            {
                monitor.UnfilteredLog( LogLevel.Error | LogLevel.IsFiltered, finalTags, $"While raising event '{e}'.", ex, fileName, lineNumber );
            }
            return false;
        }
    }

    /// <summary>
    /// Raises this event: <see cref="PerfectEvent{TEvent}.ParallelAsync"/> handlers are called (but not immediately awaited), then
    /// the <see cref="PerfectEvent{TEvent}.Sync"/> handlers are called and then the <see cref="PerfectEvent{TEvent}.Async"/>
    /// handlers are called (one after the other).
    /// <para>
    /// The returned task is resolved once the parallels, the synchronous and the asynchronous event handlers have finished their jobs.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="e">The argument of the event.</param>
    /// <param name="cancel">Optional cancellation token.</param>
    public Task RaiseAsync( IActivityMonitor monitor, TEvent e, CancellationToken cancel = default )
    {
        var p = new StartRaiseParams( monitor, this, cancel );
        StartRaise( ref p, e );
        _seq.RaiseSequential( monitor, e, cancel );
        if( p.BridgeSenders != null )
        {
            return RaiseWithBridgesAsync( monitor, e, p.BridgeSenders, p.ParallelTasks, cancel );
        }
        if( p.ParallelTasks != null )
        {
            p.ParallelTasks.Add( _seqAsync.RaiseSequentialAsync( monitor, e, cancel ) );
            return Task.WhenAll( p.ParallelTasks );
        }
        return _seqAsync.RaiseSequentialAsync( monitor, e, cancel );
    }

    async Task RaiseWithBridgesAsync( IActivityMonitor monitor,
                                      TEvent e,
                                      List<IBridgeSender> bridgeSenders,
                                      List<Task>? parallelTasks,
                                      CancellationToken cancel )
    {
        foreach( var s in bridgeSenders )
        {
            s.RaiseSync( monitor, cancel );
        }
        await _seqAsync.RaiseSequentialAsync( monitor, e, cancel ).ConfigureAwait( false );
        foreach( var s in bridgeSenders )
        {
            await s.RaiseAsync( monitor, cancel ).ConfigureAwait( false );
        }
        if( parallelTasks != null ) await Task.WhenAll( parallelTasks ).ConfigureAwait( false );
    }

    void StartRaise( ref StartRaiseParams p, TEvent e )
    {
        _parallelAsync.CollectParallelTasks( p.Monitor, e, p.Cancel, ref p.ParallelTasks );
        var bridges = _activeBridges;
        if( bridges.Length > 0 )
        {
            int firstCall = 0;
            int callCount = 0;
            foreach( var b in bridges )
            {
                if( b.OnlyFromSource && p.Source != b.Source ) continue;
                if( !p.Source.AllowMultipleEvents && b.Target == p.Source ) continue;
                if( p.BridgeSenders != null )
                {
                    var skippedTarget = b.Target.AllowMultipleEvents ? null : b.Target;
                    if( p.BridgeSenders.Any( already => already.Bridge == b || already.Bridge.Target == skippedTarget ) ) continue;
                }
                var s = b.CreateSender( e );
                if( s != null )
                {
                    p.BridgeSenders ??= new List<IBridgeSender>();
                    if( callCount++ == 0 ) firstCall = p.BridgeSenders.Count;
                    p.BridgeSenders.Add( s );
                    // Depth-fist is not a good idea (although the initial one).
                    // sender.StartRaise( ref p );
                }
            }
            // Breadth-first is far less surprising when multiple bridges exist.
            while( --callCount >= 0 )
            {
                Debug.Assert( p.BridgeSenders != null );
                p.BridgeSenders[firstCall++].StartRaise( ref p );
            }
        }
    }

    /// <summary>
    /// Creates a relay (non transformer and non filtering bridge) between this sender and another one.
    /// The relay is a bridge: it can be <see cref="IBridge.IsActive"/> or not and <see cref="IBridge.OnlyFromSource"/>
    /// can be changed, and must be disposed once done with it.
    /// </summary>
    /// <param name="target">The target that must send the same events as this one.</param>
    /// <param name="isActive">By default the new bridge is active.</param>
    /// <returns>A new bridge.</returns>
    public IBridge CreateRelay( PerfectEventSender<TEvent> target, bool isActive = true )
    {
        return CreateBridge( target, Util.FuncIdentity, isActive );
    }

    /// <summary>
    /// Creates a bridge between this sender and another one, adapting the event type.
    /// </summary>
    /// <typeparam name="T">The target's event type.</typeparam>
    /// <param name="target">The target that will receive converted events.</param>
    /// <param name="converter">The conversion function.</param>
    /// <param name="isActive">By default the new bridge is active.</param>
    /// <returns>A new bridge.</returns>
    public IBridge CreateBridge<T>( PerfectEventSender<T> target, Func<TEvent, T> converter, bool isActive = true )
    {
        return DoCreateBridge( target, null, converter, isActive );
    }

    /// <summary>
    /// Creates a bridge between this sender and another one that can filter the event before
    /// adapting the event type and raising the event on the target.
    /// </summary>
    /// <typeparam name="T">The target's event type.</typeparam>
    /// <param name="target">The target that will receive converted events.</param>
    /// <param name="filter">The filter that must be satisfied for the event to be raised on the target.</param>
    /// <param name="converter">The conversion function.</param>
    /// <param name="isActive">By default the new bridge is active.</param>
    /// <returns>A new bridge.</returns>
    public IBridge CreateFilteredBridge<T>( PerfectEventSender<T> target,
                                            Func<TEvent, bool> filter,
                                            Func<TEvent, T> converter,
                                            bool isActive = true )
    {
        Throw.CheckNotNullArgument( filter );
        Throw.CheckNotNullArgument( converter );
        return DoCreateBridge( target, filter, converter, isActive );
    }

    /// <summary>
    /// Creates a bridge between this sender and another one with a function that filters and converts at once
    /// (think <see cref="int.TryParse(string?, out int)"/>).
    /// </summary>
    /// <typeparam name="T">The target's event type.</typeparam>
    /// <param name="target">The target that will receive converted events.</param>
    /// <param name="filterConverter">The filter and conversion function.</param>
    /// <param name="isActive">By default the new bridge is active.</param>
    /// <returns>A new bridge.</returns>
    public IBridge CreateFilteredBridge<T>( PerfectEventSender<T> target,
                                            FilterConverter<TEvent, T> filterConverter,
                                            bool isActive = true )
    {
        Throw.CheckNotNullArgument( filterConverter );
        return DoCreateBridge( target, null, filterConverter, isActive );
    }

    IBridge DoCreateBridge<T>( PerfectEventSender<T> target, Func<TEvent, bool>? filter, Delegate converter, bool isActive )
    {
        Throw.CheckNotNullArgument( target );
        Throw.CheckArgument( "Creating a bridge from this to this sender is not allowed.", !ReferenceEquals( target, this ) );
        return new Bridge<T>( this, target, filter, converter, isActive );
    }

    interface IInternalBridge : IBridge
    {
        IBridgeSender? CreateSender( TEvent e );
    }

    sealed class Bridge<T> : IInternalBridge
    {
        // We need a lock to synchronize work of Dispose, OnTargetHandlersChanged and IsActive.set.
        // Among the object available here, we should not use:
        // - the _source or the _target since these are public objects outside of our control,
        // - the _filter since it is nullable.
        // We are left with:
        // - this bridge instance.
        // - the converter (that is a delegate is an object).
        // Using the converter was the first idea (up to v19.0.0) since most often this is an
        // independent instance (a lambda or anonymous delegate). But if the lambda has no closure,
        // the compiler generates a static reusable one. With the introduction of
        // static anonymous functions in C#9, this should happen more often (developers are more and
        // more aware of this). Using the converter in these conditions creates a high contention point
        // (this is particularly true for the static x => x relay). This happens only when Activating/Deactivating or
        // Disposing a Bridge (not often) OR when a target happens to have a null/not null change of its handlers...
        // this happen quite often.
        // We are left with this bridge instance or instantiate a dedicated lock object.
        // Even if this Bridge is publicly exposed, locking it during these state transitions seems not that dangerous.
        // Probability is low that a developer takes a lock on a Bridge.
        // So we take the risk and avoid an allocation.
        readonly PerfectEventSender<T> _target;
        readonly Func<TEvent, bool>? _filter;
        readonly Delegate _converter;
        readonly PerfectEventSender<TEvent> _source;
        bool _isRegistered;
        bool _active;
        bool _onlyFromSource;
        bool _disposed;

        public Bridge( PerfectEventSender<TEvent> source,
                       PerfectEventSender<T> target,
                       Func<TEvent, bool>? filter,
                       Delegate converter,
                       bool active )
        {
            _source = source;
            _target = target;
            _filter = filter;
            _converter = converter;
            if( _active = active )
            {
                target._hasHandlersChanged += OnTargetHandlersChanged;
                OnTargetHandlersChanged();
            }
        }

        public IPerfectEventSender Target => _target;

        public IPerfectEventSender Source => _source;

        void OnTargetHandlersChanged()
        {
            lock( this )
            {
                if( !_active ) return;
                if( _target.HasHandlers )
                {
                    if( !_isRegistered )
                    {
                        // Sets the flag before: in case of cycles (reentrancy):
                        // the bridge is registered only once.
                        _isRegistered = true;
                        _source.AddActiveBridge( this );
                    }
                }
                else if( _isRegistered )
                {
                    _isRegistered = false;
                    _source.RemoveActiveBridge( this );
                }
            }
        }

        public bool IsActive
        {
            get => _active;
            set
            {
                lock( this )
                {
                    if( value )
                    {
                        if( _active || _disposed ) return;
                        _active = true;
                        _target._hasHandlersChanged += OnTargetHandlersChanged;
                        OnTargetHandlersChanged();
                    }
                    else
                    {
                        if( !_active || _disposed ) return;
                        _active = false;
                        _target._hasHandlersChanged -= OnTargetHandlersChanged;
                        if( _isRegistered )
                        {
                            _isRegistered = false;
                            _source.RemoveActiveBridge( this );
                        }
                    }
                }
            }
        }

        public bool IsDisposed => _disposed;

        public bool OnlyFromSource
        {
            get => _onlyFromSource;
            set => _onlyFromSource = value;
        }

        public void Dispose()
        {
            lock( this )
            {
                if( !_disposed )
                {
                    IsActive = false;
                    _disposed = true;
                }
            }
        }

        sealed class BridgeSender : IBridgeSender
        {
            readonly PerfectEventSender<T> _target;
            readonly T _converted;

            public IBridge Bridge { get; }

            public BridgeSender( IBridge bridge, PerfectEventSender<T> target, T converted )
            {
                Bridge = bridge;
                _target = target;
                _converted = converted;
            }

            public void StartRaise( ref StartRaiseParams p )
            {
                _target.StartRaise( ref p, _converted );
            }

            public void RaiseSync( IActivityMonitor monitor, CancellationToken cancel )
            {
                _target._seq.RaiseSequential( monitor, _converted, cancel );
            }

            public Task RaiseAsync( IActivityMonitor monitor, CancellationToken cancel )
            {
                return _target._seqAsync.RaiseSequentialAsync( monitor, _converted, cancel );
            }
        }

        public IBridgeSender? CreateSender( TEvent e )
        {
            if( _converter is FilterConverter<TEvent, T> fc )
            {
                return fc( e, out var converted )
                        ? new BridgeSender( this, _target, converted )
                        : null;
            }
            return _filter?.Invoke( e ) == false
                    ? null
                    : new BridgeSender( this, _target, ((Func<TEvent, T>)_converter)( e ) );
        }
    }

}
