using CK.Core;
using System;
using System.Collections.Generic;
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
    /// Instances of this class should be kept private: only the sender object should be able to call <see cref="RaiseAsync(IActivityMonitor, TSender, TEvent)"/>
    /// or <see cref="SafeRaiseAsync(IActivityMonitor, TSender, TEvent, string?, int)"/>.
    /// What should be exposed is the <see cref="PerfectEvent"/> property that restricts the API to event registration.
    /// </para>
    /// </summary>
    /// <typeparam name="TSender">The type of the event sender.</typeparam>
    /// <typeparam name="TEvent">The type of the event argument.</typeparam>
    public sealed class PerfectEventSender<TSender, TEvent>
    {
        DelegateListImpl _seq;
        DelegateListImpl _seqAsync;
        DelegateListImpl _parallelAsync;

        /// <summary>
        /// Gets the event that should be exposed to the external world: through the <see cref="PerfectEvent{TSender, TEvent}"/>,
        /// only registration/unregistration is possible.
        /// </summary>
        public PerfectEvent<TSender, TEvent> PerfectEvent => new PerfectEvent<TSender, TEvent>( this );

        /// <summary>
        /// Gets whether at least one handler is registered.
        /// </summary>
        public bool HasHandlers => _seq.HasHandlers || _seqAsync.HasHandlers || _parallelAsync.HasHandlers;

        /// <summary>
        /// Clears the delegate list.
        /// </summary>
        public void RemoveAll()
        {
            _seq.RemoveAll();
            _seqAsync.RemoveAll();
            _parallelAsync.RemoveAll();
        }

        internal void AddSeq( Delegate handler ) => _seq.Add( handler );

        internal void RemoveSeq( Delegate handler ) => _seq.Remove( handler );

        internal void AddAsyncSeq( Delegate handler ) => _seqAsync.Add( handler );

        internal void RemoveAsyncSeq( Delegate handler ) => _seqAsync.Remove( handler );

        internal void AddAsyncParallel( Delegate handler ) => _parallelAsync.Add( handler );

        internal void RemoveAsyncParallel( Delegate handler ) => _parallelAsync.Remove( handler );

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
                Task task = _parallelAsync.RaiseParallelAsync( monitor, sender, e, cancel );
                _seq.RaiseSequential( monitor, sender, e, cancel );
                await Task.WhenAll( task, _seqAsync.RaiseSequentialAsync( monitor, sender, e, cancel ) ).ConfigureAwait( false );
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
        public Task RaiseAsync( IActivityMonitor monitor, TSender sender, TEvent e, CancellationToken cancel = default )
        {
            Task task = _parallelAsync.RaiseParallelAsync( monitor, sender, e, cancel );
            _seq.RaiseSequential( monitor, sender, e, cancel );
            return Task.WhenAll( task, _seqAsync.RaiseSequentialAsync( monitor, sender, e, cancel ) );
        }

    }

}
