using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CK.PerfectEvent
{
    /// <summary>
    /// Delegate definition used by <see cref="PerfectEvent{TSender}.CreateFilteredBridge{T}(PerfectEventSender{T}, FilterConverter{TSender, T}, bool)"/>.
    /// </summary>
    /// <typeparam name="TEvent">The source event type.</typeparam>
    /// <typeparam name="T">The target event type.</typeparam>
    /// <param name="e">The event.</param>
    /// <param name="converted">The converted value. Typically <c>default</c> when this returns false.</param>
    /// <returns>True if the event must be sent to the target, false otherwise.</returns>
    public delegate bool FilterConverter<TEvent, T>( TEvent e, out T converted );

}
