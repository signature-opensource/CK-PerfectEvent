using CK.Core;

using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CK.PerfectEvent
{
    /// <summary>
    /// Sequential synchronous event handler.
    /// </summary>
    /// <typeparam name="TEvent">The type of the event.</typeparam>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="e">The event argument.</param>
    public delegate void SequentialEventHandler<TEvent>( IActivityMonitor monitor, TEvent e );

    /// <summary>
    /// Sequential asynchronous event handler.
    /// </summary>
    /// <typeparam name="TEvent">The type of the event.</typeparam>
    /// <param name="monitor">The monitor that must be used to log activities.</param>
    /// <param name="e">The event argument.</param>
    /// <param name="cancel">Cancellation token.</param>
    public delegate Task SequentialEventHandlerAsync<TEvent>( IActivityMonitor monitor, TEvent e, CancellationToken cancel );

    /// <summary>
    /// Parallel asynchronous event handler.
    /// </summary>
    /// <typeparam name="TEvent">The type of the event.</typeparam>
    /// <param name="token">The activity token to use in any other monitor.</param>
    /// <param name="e">The event argument.</param>
    /// <param name="cancel">Cancellation token.</param>
    public delegate Task ParallelEventHandlerAsync<TEvent>( ActivityMonitor.DependentToken token, TEvent e, CancellationToken cancel );

}
