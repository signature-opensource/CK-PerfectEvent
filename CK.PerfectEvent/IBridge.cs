using System;

namespace CK.PerfectEvent
{
    /// <summary>
    /// Bridge between a <see cref="Source"/> and a <see cref="Target"/>.
    /// A bridge can transform (project) and/or filter the event or be a simple relay.
    /// </summary>
    public interface IBridge : IDisposable
    {
        /// <summary>
        /// Gets the source <see cref="PerfectEvent{TEvent}"/> of this bridge.
        /// </summary>
        IPerfectEventSender Source { get; }

        /// <summary>
        /// Gets the target <see cref="PerfectEvent{TEvent}"/> of this bridge.
        /// </summary>
        IPerfectEventSender Target { get; }

        /// <summary>
        /// Gets or sets whether this bridge is active.
        /// Defaults to true: a bridge is active as soon as it is created.
        /// </summary>
        bool IsActive { get; set; }

        /// <summary>
        /// Gets or sets whether this bridge is restricted to its <see cref="Source"/>:
        /// only events raised on the Source will be considered, events coming from other bridges
        /// are ignored.
        /// <para>
        /// Defaults to false.
        /// </para>
        /// </summary>
        bool OnlyFromSource { get; set; }

        /// <summary>
        /// Gets whether this bridge has been disposed.
        /// </summary>
        bool IsDisposed { get; }
    }
}
