using CK.Core;
using System.Threading;
using System.Threading.Tasks;

namespace CK.PerfectEvent;

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
/// <param name="parallelLogger">The <see cref="IParallelLogger"/> to use.</param>
/// <param name="e">The event argument.</param>
/// <param name="cancel">Cancellation token.</param>
public delegate Task ParallelEventHandlerAsync<TEvent>( IParallelLogger parallelLogger, TEvent e, CancellationToken cancel );
