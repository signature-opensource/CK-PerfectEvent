using CK.Core;
using System;
using System.Collections.Generic;
using System.Data;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Channels;
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
    public sealed class PerfectEventSender<TEvent>
    {
        DelegateListImpl _seq;
        DelegateListImpl _seqAsync;
        DelegateListImpl _parallelAsync;
        Action? _hasHandlersChanged;

        /// <summary>
        /// Gets the event that should be exposed to the external world: through the <see cref="PerfectEvent{TEvent}"/>,
        /// only registration/unregistration is possible.
        /// </summary>
        public PerfectEvent<TEvent> PerfectEvent => new PerfectEvent<TEvent>( this );

        /// <summary>
        /// Gets whether at least one handler is registered.
        /// </summary>
        public bool HasHandlers => _seq.HasHandlers || _seqAsync.HasHandlers || _parallelAsync.HasHandlers;

        /// <summary>
        /// Clears the delegate list.
        /// </summary>
        public void RemoveAll()
        {
            if( _seq.RemoveAll() || _seqAsync.RemoveAll() || _parallelAsync.RemoveAll() ) _hasHandlersChanged?.Invoke();
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
                Task task = _parallelAsync.RaiseParallelAsync( monitor, e, cancel );
                _seq.RaiseSequential( monitor, e, cancel );
                await Task.WhenAll( task, _seqAsync.RaiseSequentialAsync( monitor, e, cancel ) ).ConfigureAwait( false );
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
            Task task = _parallelAsync.RaiseParallelAsync( monitor, e, cancel );
            _seq.RaiseSequential( monitor, e, cancel );
            return Task.WhenAll( task, _seqAsync.RaiseSequentialAsync( monitor, e, cancel ) );
        }

        /// <summary>
        /// Creates a bridge between this sender and another one, adapting the event type.
        /// </summary>
        /// <typeparam name="T">The target's event type.</typeparam>
        /// <param name="target">The target that will receive converted events.</param>
        /// <param name="converter">The conversion function.</param>
        /// <returns>A disposable that can be used to remove the bridge.</returns>
        public IDisposable BridgeTo<T>( PerfectEventSender<T> target, Func<TEvent,T> converter )
        {
            Throw.CheckNotNullArgument( target );
            Throw.CheckNotNullArgument( converter );
            return new Bridge<T>( this, target, converter );
        }

        sealed class Bridge<T> : IDisposable
        {
            readonly PerfectEventSender<T> Target;
            readonly Func<TEvent, T> Converter;
            readonly PerfectEventSender<TEvent> Source;
            bool _seqSReg;
            bool _seqAReg;
            bool _seqPReg;
            bool _diposed;

            public Bridge( PerfectEventSender<TEvent> source, PerfectEventSender<T> target, Func<TEvent, T> converter )
            {
                Source = source;
                Target = target;
                Converter = converter;
                target._hasHandlersChanged += OnTargetHandlersChanged;
                OnTargetHandlersChanged();
            }

            void OnTargetHandlersChanged()
            {
                lock( Converter )
                {
                    if( _diposed ) return;
                    if( Target._seq.HasHandlers )
                    {
                        if( !_seqSReg )
                        {
                            Source.AddSeq( OnSync );
                            _seqSReg = true;
                        }
                    }
                    else if( _seqSReg )
                    {
                        Source.RemoveSeq( OnSync );
                        _seqSReg = false;
                    }
                    if( Target._seqAsync.HasHandlers )
                    {
                        if( !_seqAReg )
                        {
                            Source.AddAsyncSeq( OnAsync );
                            _seqAReg = true;
                        }
                    }
                    else if( _seqAReg )
                    {
                        Source.RemoveAsyncSeq( OnAsync );
                        _seqAReg = false;
                    }
                    if( Target._parallelAsync.HasHandlers )
                    {
                        if( !_seqPReg )
                        {
                            Source.AddAsyncParallel( OnParallelAsync );
                            _seqPReg = true;
                        }
                    }
                    else if( _seqPReg )
                    {
                        Source.RemoveAsyncParallel( OnParallelAsync );
                        _seqPReg = false;
                    }
                }
            }

            public void Dispose()
            {
                lock( Converter )
                {
                    if( _diposed ) return;
                    _diposed = true;
                    Target._hasHandlersChanged -= OnTargetHandlersChanged;
                    if( _seqSReg ) Source.RemoveSeq( OnSync );
                    if( _seqAReg ) Source.RemoveAsyncSeq( OnAsync );
                    if( _seqPReg ) Source.RemoveAsyncParallel( OnParallelAsync );
                }
            }

            internal void OnSync( IActivityMonitor monitor, TEvent e )
            {
                Target._seq.RaiseSequential( monitor, Converter( e ), default );
            }

            internal Task OnAsync( IActivityMonitor monitor, TEvent e, CancellationToken cancel )
            {
                return Target._seqAsync.RaiseSequentialAsync( monitor, Converter( e ), cancel );
            }

            internal Task OnParallelAsync( ActivityMonitor.DependentToken token, TEvent e, CancellationToken cancel )
            {
                return Target._parallelAsync.RaiseParallelAsync( token, Converter( e ), cancel );
            }
        }

    }
}
