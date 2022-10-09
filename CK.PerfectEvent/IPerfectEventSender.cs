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
    }


}
