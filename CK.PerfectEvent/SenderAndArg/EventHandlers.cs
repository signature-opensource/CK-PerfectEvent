using CK.Core;

using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CK.PerfectEvent
{
    /// <summary>
    /// Sequential synchronous event handler with sender argument.
    /// </summary>
    /// <typeparam name="TSender">Type of the sender.</typeparam>
    /// <typeparam name="TEvent">The type of the event.</typeparam>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="sender">The source of the event.</param>
    /// <param name="e">The event argument.</param>
    public delegate void SequentialEventHandler<TSender, TEvent>( IActivityMonitor monitor, TSender sender, TEvent e );

    /// <summary>
    /// Sequential asynchronous event handler with sender argument.
    /// </summary>
    /// <typeparam name="TSender">Type of the sender.</typeparam>
    /// <typeparam name="TEvent">The type of the event.</typeparam>
    /// <param name="monitor">The monitor that must be used to log activities.</param>
    /// <param name="sender">The source of the event.</param>
    /// <param name="e">The event argument.</param>
    /// <param name="cancel">Cancellation token.</param>
    public delegate Task SequentialEventHandlerAsync<TSender, TEvent>( IActivityMonitor monitor, TSender sender, TEvent e, CancellationToken cancel );

    /// <summary>
    /// Parallel asynchronous event handler with sender argument.
    /// </summary>
    /// <typeparam name="TSender">Type of the sender.</typeparam>
    /// <typeparam name="TEvent">The type of the event.</typeparam>
    /// <param name="loggerOrToken">
    /// The <see cref="IParallelLogger"/> to use or a <see cref="ActivityMonitor.DependentToken"/> if the source of
    /// the activity's monitor has no <see cref="IActivityMonitor.ParallelLogger"/>.
    /// </param>
    /// <param name="sender">The source of the event.</param>
    /// <param name="e">The event argument.</param>
    /// <param name="cancel">Cancellation token.</param>
    public delegate Task ParallelEventHandlerAsync<TSender, TEvent>( object loggerOrToken, TSender sender, TEvent e, CancellationToken cancel );

}
