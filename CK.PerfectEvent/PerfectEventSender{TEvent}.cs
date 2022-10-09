using CK.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;

namespace CK.PerfectEvent
{
    /// <summary>
    /// A perfect event sender offers synchronous, asynchronous and parallel asynchronous event support.
    /// <para>
    /// Instances of this class should be kept private: only the sender object should be able to call <see cref="RaiseAsync(IActivityMonitor, TEvent)"/>
    /// or <see cref="SafeRaiseAsync(IActivityMonitor, TEvent, string?, int)"/>.
    /// What should be exposed is the <see cref="PerfectEvent"/> property that restricts the API to event registration.
    /// </para>
    /// </summary>
    /// <typeparam name="TEvent">The type of the event argument.</typeparam>
    public sealed class PerfectEventSender<TEvent> : IPerfectEventSender
    {
        DelegateListImpl _seq;
        DelegateListImpl _seqAsync;
        DelegateListImpl _parallelAsync;
        IInternalBridge[] _bridges = Array.Empty<IInternalBridge>();
        Action? _hasHandlersChanged;

        /// <summary>
        /// Gets the event that should be exposed to the external world: through the <see cref="PerfectEvent{TEvent}"/>,
        /// only registration/unregistration is possible.
        /// </summary>
        public PerfectEvent<TEvent> PerfectEvent => new PerfectEvent<TEvent>( this );

        /// <inheritdoc />
        public bool HasHandlers => _seq.HasHandlers || _seqAsync.HasHandlers || _parallelAsync.HasHandlers || _bridges.Length > 0;

        /// <summary>
        /// Clears the registered handlers of this sender.
        /// </summary>
        public void RemoveAll()
        {
            if( _seq.RemoveAll() || _seqAsync.RemoveAll() || _parallelAsync.RemoveAll() ) _hasHandlersChanged?.Invoke();
        }

        void AddBridge( IInternalBridge b )
        {
            if( Util.InterlockedAdd( ref _bridges, b ).Length == 1 )
            {
                _hasHandlersChanged?.Invoke();
            }
        }

        void RemoveBridge( IInternalBridge b )
        {
            if( Util.InterlockedRemove( ref _bridges, b ).Length == 0 )
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
                    monitor.UnfilteredLog( LogLevel.Error|LogLevel.IsFiltered, finalTags, $"While raising event '{e}'.", ex, fileName, lineNumber );
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
            _parallelAsync.CollectParallelTasks( p.Monitor, e, p.Cancel, ref p.Token, ref p.ParallelTasks );
            var bridges = _bridges;
            if( bridges.Length > 0 )
            {
                p.BridgeSenders ??= new List<IBridgeSender>();
                foreach( var b in bridges )
                {
                    Debug.Assert( p.BridgeSenders != null );
                    if( p.Primary == b.Target || p.BridgeSenders.Any( s => s.Bridge == b ) ) continue;
                    var sender = b.CreateSender( e );
                    p.BridgeSenders.Add( sender );
                    sender.StartRaise( ref p );
                }
            }
        }

        /// <summary>
        /// Creates a bridge between this sender and another one, adapting the event type.
        /// </summary>
        /// <typeparam name="T">The target's event type.</typeparam>
        /// <param name="target">The target that will receive converted events.</param>
        /// <param name="converter">The conversion function.</param>
        /// <param name="isActive">By default the new bridge is active.</param>
        /// <returns>A new bridge.</returns>
        public IBridge CreateBridge<T>( PerfectEventSender<T> target, Func<TEvent,T> converter, bool isActive = true )
        {
            Throw.CheckNotNullArgument( target );
            Throw.CheckNotNullArgument( converter );
            return new Bridge<T>( this, target, converter, isActive );
        }

        interface IInternalBridge : IBridge
        {
            IBridgeSender CreateSender( TEvent e );
        }

        sealed class Bridge<T> : IInternalBridge
        {
            readonly PerfectEventSender<T> _target;
            readonly Func<TEvent, T> _converter;
            readonly PerfectEventSender<TEvent> _source;
            bool _isRegistered;
            bool _active;
            bool _disposed;

            public Bridge( PerfectEventSender<TEvent> source, PerfectEventSender<T> target, Func<TEvent, T> converter, bool active )
            {
                _source = source;
                _target = target;
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
                lock( _converter )
                {
                    if( !_active ) return;
                    if( _target.HasHandlers )
                    {
                        if( !_isRegistered )
                        {
                            // Sets the flag before: in case of cycles (reentrancy):
                            // the bridge is registered only once.
                            _isRegistered = true;
                            _source.AddBridge( this );
                        }
                    }
                    else if( _isRegistered )
                    {
                        _isRegistered = false;
                        _source.RemoveBridge( this );
                    }
                }
            }

            public bool IsActive
            {
                get => _active;
                set
                {
                    lock( _converter )
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
                                _source.RemoveBridge( this );
                            }
                        }
                    }
                }
            }

            public bool IsDisposed => _disposed;

            public void Dispose()
            {
                lock( _converter )
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

            public IBridgeSender CreateSender( TEvent e ) => new BridgeSender( this, _target, _converter( e ) );

        }

    }

}
