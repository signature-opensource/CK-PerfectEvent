using CK.Core;
using System;
using System.Collections;
using System.Collections.Generic;
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
        public readonly IPerfectEventSender Primary;
        public readonly CancellationToken Cancel;
        public ActivityMonitor.DependentToken? Token;
        public List<Task>? ParallelTasks;
        public List<IBridgeSender>? BridgeSenders;

        public StartRaiseParams( IActivityMonitor monitor, IPerfectEventSender primary, CancellationToken cancel )
        {
            Monitor = monitor;
            Primary = primary;
            Cancel = cancel;
            Token = null;
            ParallelTasks = null;
            BridgeSenders = null;
        }
    }


}
