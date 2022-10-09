using CK.Core;
using System;
using System.Runtime.CompilerServices;

namespace CK.PerfectEvent
{
    /// <summary>
    /// Registration facade for <see cref="PerfectEventSender{TEvent}"/>.
    /// To subscribe and unsubscribe to this event, use the <see cref="Sync"/>, <see cref="Async"/> or <see cref="ParallelAsync"/> with
    /// <c>+=</c> and <c>-=</c> standard event operators.
    /// </summary>
    /// <typeparam name="TEvent">The type of the event argument.</typeparam>
    public readonly struct PerfectEvent<TEvent>
    {
        readonly PerfectEventSender<TEvent> _sender;

        internal PerfectEvent( PerfectEventSender<TEvent> sender )
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
        /// Signature is <c>Action&lt;IActivityMonitor, TEvent&gt;</c>
        /// </para>
        /// </summary>
        public event SequentialEventHandler<TEvent> Sync
        {
            add => _sender.AddSeq( value );
            remove => _sender.RemoveSeq( value );
        }

#pragma warning disable VSTHRD200 // Use "Async" suffix for async methods
        /// <summary>
        /// Gets the Asynchronous event registration point.
        /// <para>
        /// Signature is <c>Action&lt;IActivityMonitor, TEvent, CancellationToken&gt;</c>
        /// </para>
        /// </summary>
        public event SequentialEventHandlerAsync<TEvent> Async
        {
            add => _sender.AddAsyncSeq( value );
            remove => _sender.RemoveAsyncSeq( value );
        }

        /// <summary>
        /// Gets the Parallel Asynchronous event registration point.
        /// <para>
        /// Signature is <c>Action&lt;ActivityMonitor.DependentToken, TEvent, CancellationToken&gt;</c>
        /// </para>
        /// </summary>
        public event ParallelEventHandlerAsync<TEvent> ParallelAsync
        {
            add => _sender.AddAsyncParallel( value );
            remove => _sender.RemoveAsyncParallel( value );
        }
#pragma warning restore VSTHRD200 // Use "Async" suffix for async methods

        /// <summary>
        /// Returns a PerfectEvent that can register handlers for base classes of this <typeparamref name="TEvent"/>.
        /// <para>
        /// Note that this must be used with care since there is currently no way to express the
        /// constraint "where <typeparamref name="TEvent"/> : <typeparamref name="TEventBase"/>" that MUST be
        /// satisfied. This check is done at runtime (if <c>typeof(TEvent).IsValueType</c> or <c>!typeof( TEventBase ).IsAssignableFrom( typeof(TEvent) )</c>
        /// an <see cref="InvalidOperationException"/> is thrown). 
        /// </para>
        /// <para>
        /// Hopefully a code analyzer can secure this, or the language may support this revert generic constraint once. This is why,
        /// this method is named "Adapt" and not "UnsafeAdapt" or "UncheckedAdapt".
        /// </para>
        /// </summary>
        /// <typeparam name="TEventBase">The base event type.</typeparam>
        /// <returns>A perfect event for <typeparamref name="TEventBase"/>.</returns>
        public PerfectEvent<TEventBase> Adapt<TEventBase>() where TEventBase : class
        {
            Throw.CheckArgument( "The event type cannot be automatically adapted. You should use the CreateBridge to another dedicated PerfertEventSender instead.",
                                 !typeof( TEvent ).IsValueType && typeof( TEventBase ).IsAssignableFrom( typeof( TEvent ) ) );
            var @this = this;
            return Unsafe.As<PerfectEvent<TEvent>, PerfectEvent<TEventBase>>( ref @this );
        }

        /// <summary>
        /// Creates a bridge from this event to another sender, adapting the event type.
        /// </summary>
        /// <typeparam name="T">The target's event type.</typeparam>
        /// <param name="target">The sender that will receive converted events.</param>
        /// <param name="converter">The conversion function.</param>
        /// <param name="isActive">By default the new bridge is active.</param>
        /// <returns>A new bridge.</returns>
        public IBridge CreateBridge<T>( PerfectEventSender<T> target, Func<TEvent, T> converter, bool isActive = true )
        {
            return _sender.CreateBridge( target, converter, isActive );
        }
    }


}
