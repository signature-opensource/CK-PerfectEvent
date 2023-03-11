using CK.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static Microsoft.IO.RecyclableMemoryStreamManager;

namespace CK.PerfectEvent
{
    /// <summary>
    /// A perfect event sender offers synchronous, asynchronous and parallel asynchronous event support.
    /// <para>
    /// Instances of this class should be kept private: only the sender object should be able to call <see cref="RaiseAsync(IActivityMonitor, TSender, TEvent, CancellationToken)"/>
    /// or <see cref="SafeRaiseAsync(IActivityMonitor, TSender, TEvent, CancellationToken, string?, int)"/>.
    /// What should be exposed is the <see cref="PerfectEvent"/> property that restricts the API to event registration and bridge management.
    /// </para>
    /// </summary>
    /// <typeparam name="TSender">The type of the event sender.</typeparam>
    /// <typeparam name="TEvent">The type of the event argument.</typeparam>
    public sealed class PerfectEventSender<TSender, TEvent> : IPerfectEventSender
    {
        DelegateListImpl _seq;
        DelegateListImpl _seqAsync;
        DelegateListImpl _parallelAsync;
        IInternalBridge[] _activeBridges = Array.Empty<IInternalBridge>();
        Action? _hasHandlersChanged;

        /// <summary>
        /// Gets the event that should be exposed to the external world: through the <see cref="PerfectEvent{TSender, TEvent}"/>,
        /// only registration/unregistration and bridging is possible.
        /// </summary>
        public PerfectEvent<TSender, TEvent> PerfectEvent => new PerfectEvent<TSender, TEvent>( this );

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
        /// <param name="sender">The sender of the event.</param>
        /// <param name="e">The argument of the event.</param>
        /// <param name="cancel">Optional cancellation token.</param>
        /// <param name="fileName">The source filename where this event is raised.</param>
        /// <param name="lineNumber">The source line number in the filename where this event is raised.</param>
        /// <returns>True on success, false if an exception occurred.</returns>
        public async Task<bool> SafeRaiseAsync( IActivityMonitor monitor,
                                                TSender sender,
                                                TEvent e,
                                                CancellationToken cancel = default,
                                                [CallerFilePath] string? fileName = null,
                                                [CallerLineNumber] int lineNumber = 0 )
        {
            try
            {
                await RaiseAsync( monitor, sender, e, cancel ).ConfigureAwait( false );
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
        /// <param name="sender">The sender of the event.</param>
        /// <param name="e">The argument of the event.</param>
        /// <param name="cancel">Optional cancellation token.</param>
        public Task RaiseAsync( IActivityMonitor monitor,
                                TSender sender,
                                TEvent e,
                                CancellationToken cancel = default )
        {
            var p = new StartRaiseParams( monitor, this, cancel );
            StartRaise( ref p, sender, e );
            _seq.RaiseSequential( monitor, sender, e, cancel );
            if( p.BridgeSenders != null )
            {
                return RaiseWithBridgesAsync( monitor, sender, e, p.BridgeSenders, p.ParallelTasks, cancel );
            }
            if( p.ParallelTasks != null )
            {
                p.ParallelTasks.Add( _seqAsync.RaiseSequentialAsync( monitor, sender, e, cancel ) );
                return Task.WhenAll( p.ParallelTasks );
            }
            return _seqAsync.RaiseSequentialAsync( monitor, sender, e, cancel );
        }

        async Task RaiseWithBridgesAsync( IActivityMonitor monitor,
                                          TSender sender,
                                          TEvent e,
                                          List<IBridgeSender> bridgeSenders,
                                          List<Task>? parallelTasks,
                                          CancellationToken cancel )
        {
            foreach( var s in bridgeSenders )
            {
                s.RaiseSync( monitor, cancel );
            }
            await _seqAsync.RaiseSequentialAsync( monitor, sender, e, cancel ).ConfigureAwait( false );
            foreach( var s in bridgeSenders )
            {
                await s.RaiseAsync( monitor, cancel ).ConfigureAwait( false );
            }
            if( parallelTasks != null ) await Task.WhenAll( parallelTasks ).ConfigureAwait( false );
        }

        void StartRaise( ref StartRaiseParams p, TSender sender, TEvent e )
        {
            _parallelAsync.CollectParallelTasks( p.Monitor, sender, e, p.Cancel, ref p.Token, ref p.ParallelTasks );
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
                    var s = b.CreateSender( sender, e );
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
        /// <param name="target">The target that must send the same events (with the same sender) as this one.</param>
        /// <param name="isActive">By default the new bridge is active.</param>
        /// <returns>A new bridge.</returns>
        public IBridge CreateRelay( PerfectEventSender<TSender,TEvent> target, bool isActive = true )
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
        public IBridge CreateBridge<T>( PerfectEventSender<TSender,T> target, Func<TEvent, T> converter, bool isActive = true )
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
        public IBridge CreateFilteredBridge<T>( PerfectEventSender<TSender,T> target,
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
        public IBridge CreateFilteredBridge<T>( PerfectEventSender<TSender, T> target,
                                                FilterConverter<TEvent, T> filterConverter,
                                                bool isActive = true )
        {
            Throw.CheckNotNullArgument( filterConverter );
            return DoCreateBridge( target, null, filterConverter, isActive );
        }

        IBridge DoCreateBridge<T>( PerfectEventSender<TSender,T> target, Func<TEvent, bool>? filter, Delegate converter, bool isActive )
        {
            Throw.CheckNotNullArgument( target );
            Throw.CheckArgument( "Creating a bridge from this to this sender is not allowed.", !ReferenceEquals( target, this ) );
            return new Bridge<T>( this, target, filter, converter, isActive );
        }

        interface IInternalBridge : IBridge
        {
            IBridgeSender? CreateSender( TSender sender, TEvent e );
        }

        sealed class Bridge<T> : IInternalBridge
        {
            readonly PerfectEventSender<TSender,T> _target;
            readonly Func<TEvent, bool>? _filter;
            readonly Delegate _converter;
            readonly PerfectEventSender<TSender,TEvent> _source;
            bool _isRegistered;
            bool _active;
            bool _onlyFromSource;
            bool _disposed;

            public Bridge( PerfectEventSender<TSender,TEvent> source,
                           PerfectEventSender<TSender,T> target,
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
                // See the discussion about this lock in PerfectEventSender<TEvent>.Bridge class.
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
                    // See the discussion about this lock in PerfectEventSender<TEvent>.Bridge class.
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
                // See the discussion about this lock in PerfectEventSender<TEvent>.Bridge class.
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
                readonly PerfectEventSender<TSender,T> _target;
                readonly TSender _sender;
                readonly T _converted;

                public IBridge Bridge { get; }

                public BridgeSender( IBridge bridge, PerfectEventSender<TSender, T> target, TSender sender, T converted )
                {
                    Bridge = bridge;
                    _target = target;
                    _converted = converted;
                    _sender = sender;
                }

                public void StartRaise( ref StartRaiseParams p )
                {
                    _target.StartRaise( ref p, _sender, _converted );
                }

                public void RaiseSync( IActivityMonitor monitor, CancellationToken cancel )
                {
                    _target._seq.RaiseSequential( monitor, _sender, _converted, cancel );
                }

                public Task RaiseAsync( IActivityMonitor monitor, CancellationToken cancel )
                {
                    return _target._seqAsync.RaiseSequentialAsync( monitor, _sender, _converted, cancel );
                }
            }

            public IBridgeSender? CreateSender( TSender sender, TEvent e )
            {
                if( _converter is FilterConverter<TEvent, T> fc )
                {
                    return fc( e, out var converted )
                            ? new BridgeSender( this, _target, sender, converted )
                            : null;
                }
                return _filter?.Invoke( e ) == false
                        ? null
                        : new BridgeSender( this, _target, sender, ((Func<TEvent, T>)_converter)( e ) );
            }

        }

    }

}
