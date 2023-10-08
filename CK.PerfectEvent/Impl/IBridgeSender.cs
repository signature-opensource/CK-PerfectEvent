using CK.Core;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CK.PerfectEvent
{
    interface IBridgeSender
    {
        IBridge Bridge { get; }

        void StartRaise( ref StartRaiseParams p );

        void RaiseSync( IActivityMonitor monitor, CancellationToken cancel );

        Task RaiseAsync( IActivityMonitor monitor, CancellationToken cancel );
    }

    /// <summary>
    /// Captures all the parameters of the StartRaise except the event: this is
    /// not (and must not be) generic.
    /// </summary>
    struct StartRaiseParams
    {
        public readonly IActivityMonitor Monitor;
        public readonly IPerfectEventSender Source;
        public readonly CancellationToken Cancel;
        public List<Task>? ParallelTasks;
        public List<IBridgeSender>? BridgeSenders;

        public StartRaiseParams( IActivityMonitor monitor, IPerfectEventSender source, CancellationToken cancel )
        {
            Monitor = monitor;
            Source = source;
            Cancel = cancel;
            ParallelTasks = null;
            BridgeSenders = null;
        }
    }


}
