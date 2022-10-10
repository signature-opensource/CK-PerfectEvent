namespace CK.PerfectEvent
{
    /// <summary>
    /// Non generic interface of all senders.
    /// </summary>
    public interface IPerfectEventSender
    {
        /// <summary>
        /// Gets whether at least one handler is registered (or at least one bridge that has handlers).
        /// </summary>
        bool HasHandlers { get; }

        /// <summary>
        /// Gets or sets whether this target can emit multiple events transformed by upstream bridges for
        /// the same initial call to RaiseEvent or SafeRaiseEvent.
        /// <para>
        /// Defaults to false.
        /// </para>
        /// </summary>
        bool AllowMultipleEvents { get; set; }
    }


}
