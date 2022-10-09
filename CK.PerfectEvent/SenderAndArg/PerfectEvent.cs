using CK.Core;
using System;
using System.Runtime.CompilerServices;

namespace CK.PerfectEvent
{
    /// <summary>
    /// Registration facade for <see cref="PerfectEventSender{TSender,TEvent}"/>.
    /// To subscribe and unsubscribe to this event, use the <see cref="Sync"/>, <see cref="Async"/> or <see cref="ParallelAsync"/> with
    /// <c>+=</c> and <c>-=</c> standard event operators.
    /// </summary>
    /// <typeparam name="TSender">The type of the sender.</typeparam>
    /// <typeparam name="TEvent">The type of the event argument.</typeparam>
    public readonly struct PerfectEvent<TSender, TEvent>
    {
        readonly PerfectEventSender<TSender, TEvent> _sender;

        internal PerfectEvent( PerfectEventSender<TSender, TEvent> sender )
        {
            _sender = sender;
        }

        /// <summary>
        /// Gets whether at least one handler is registered.
        /// </summary>
        public bool HasHandlers => _sender.HasHandlers;

        /// <summary>
        /// Gets the Synchronous event registration point.
        /// <para>
        /// Signature is <c>Action&lt;IActivityMonitor, TSender, TEvent&gt;</c>
        /// </para>
        /// </summary>
        public event SequentialEventHandler<TSender, TEvent> Sync
        {
            add => _sender.AddSeq( value );
            remove => _sender.RemoveSeq( value );
        }

#pragma warning disable VSTHRD200 // Use "Async" suffix for async methods
        /// <summary>
        /// Gets the Asynchronous event registration point.
        /// <para>
        /// Signature is <c>Action&lt;IActivityMonitor, TSender, TEvent, CancellationToken&gt;</c>
        /// </para>
        /// </summary>
        public event SequentialEventHandlerAsync<TSender, TEvent> Async
        {
            add => _sender.AddAsyncSeq( value );
            remove => _sender.RemoveAsyncSeq( value );
        }

        /// <summary>
        /// Gets the Parallel Asynchronous event registration point.
        /// <para>
        /// Signature is <c>Action&lt;ActivityMonitor.DependentToken, TSender, TEvent, CancellationToken&gt;</c>
        /// </para>
        /// </summary>
        public event ParallelEventHandlerAsync<TSender, TEvent> ParallelAsync
        {
            add => _sender.AddAsyncParallel( value );
            remove => _sender.RemoveAsyncParallel( value );
        }
#pragma warning restore VSTHRD200 // Use "Async" suffix for async methods

        /// <summary>
        /// Returns a PerfectEvent that can register handlers for base classes of this <typeparamref name="TEvent"/>.
        /// <para>
        /// See <see cref="PerfectEvent{TEvent}.Adapt{TEventBase}"/> for current limitations.
        /// </para>
        /// </summary>
        /// <typeparam name="TEventBase">The base event type.</typeparam>
        /// <returns>A perfect event for <typeparamref name="TEventBase"/>.</returns>
        public PerfectEvent<TSender, TEventBase> Adapt<TEventBase>() where TEventBase : class
        {
            Throw.CheckState( !typeof( TEvent ).IsValueType && typeof( TEventBase ).IsAssignableFrom( typeof( TEvent ) ) );
            var @this = this;
            return Unsafe.As<PerfectEvent<TSender, TEvent>, PerfectEvent<TSender, TEventBase>>( ref @this );
        }
    }
}
