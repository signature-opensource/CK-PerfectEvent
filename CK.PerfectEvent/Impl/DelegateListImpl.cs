using CK.Core;

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Threading;
using System.Collections.Generic;

namespace CK.PerfectEvent;

struct DelegateListImpl
{
    object? _handler;

    public bool HasHandlers => _handler != null;

    public bool Add( Delegate handler )
    {
        Throw.CheckNotNullArgument( handler );
        return Util.InterlockedNullableSet( ref _handler, handler, static ( current, handler ) =>
        {
            if( current == null ) return handler;
            if( current is Delegate d ) return new Delegate[] { d, handler };
            var ah = (Delegate[])current;
            int len = ah.Length;
            Array.Resize( ref ah, len + 1 );
            ah[len] = handler;
            return ah;
        } ) is Delegate;
    }

    public bool Remove( Delegate handler )
    {
        Throw.CheckNotNullArgument( handler );
        return Util.InterlockedNullableSet( ref _handler, handler, static ( current, handler ) =>
        {
            if( current == null ) return null;
            if( current is Delegate d ) return d == handler ? null : current;
            var a = (Delegate[])current;
            int idx = Array.IndexOf( a, handler );
            if( idx < 0 ) return a;
            Debug.Assert( a.Length > 1 );
            if( a.Length == 2 ) return a[1 - idx];
            var ah = new Delegate[a.Length - 1];
            Array.Copy( a, 0, ah, 0, idx );
            Array.Copy( a, idx + 1, ah, idx, ah.Length - idx );
            return ah;
        } ) == null;
    }

    public bool RemoveAll() => Interlocked.Exchange( ref _handler, null ) != null;

    public void RaiseSequential<TEvent>( IActivityMonitor monitor, TEvent e, CancellationToken cancel )
    {
        var h = _handler;
        if( h == null || cancel.IsCancellationRequested ) return;
        if( h is Delegate d ) Unsafe.As<SequentialEventHandler<TEvent>>( d ).Invoke( monitor, e );
        else
        {
            var all = Unsafe.As<SequentialEventHandler<TEvent>[]>( h );
            foreach( var x in all )
            {
                if( cancel.IsCancellationRequested ) break;
                x.Invoke( monitor, e );
            }
        }
    }

    public Task RaiseSequentialAsync<TEvent>( IActivityMonitor monitor, TEvent e, CancellationToken cancel )
    {
        var h = _handler;
        if( h == null || cancel.IsCancellationRequested ) return Task.CompletedTask;
        if( h is Delegate d ) return Unsafe.As<AsyncSequentialEventHandler<TEvent>>( d ).Invoke( monitor, e, cancel );
        return RaiseSequentialAsync( monitor, Unsafe.As<AsyncSequentialEventHandler<TEvent>[]>( h ), e, cancel );
    }

    static async Task RaiseSequentialAsync<TEvent>( IActivityMonitor monitor, AsyncSequentialEventHandler<TEvent>[] all, TEvent e, CancellationToken cancel )
    {
        foreach( var h in all ) await h.Invoke( monitor, e, cancel ).ConfigureAwait( false );
    }

    public void CollectParallelTasks<TEvent>( IActivityMonitor monitor,
                                              TEvent e,
                                              CancellationToken cancel,
                                              ref List<Task>? tasks )
    {
        var h = _handler;
        if( h == null || cancel.IsCancellationRequested ) return;
        tasks ??= new List<Task>();
        if( h is Delegate d )
        {
            tasks.Add( Unsafe.As<ParallelAsyncEventHandler<TEvent>>( d ).Invoke( monitor.ParallelLogger, e, cancel ) );
        }
        else
        {
            var all = Unsafe.As<ParallelAsyncEventHandler<TEvent>[]>( h );
            foreach( var a in all )
            {
                tasks.Add( a.Invoke( monitor.ParallelLogger, e, cancel ) );
            }
        }
    }

    #region With Sender
    public void RaiseSequential<TSender, TEvent>( IActivityMonitor monitor, TSender sender, TEvent e, CancellationToken cancel )
    {
        var h = _handler;
        if( h == null || cancel.IsCancellationRequested ) return;
        if( h is Delegate d ) Unsafe.As<SequentialEventHandler<TSender, TEvent>>( d ).Invoke( monitor, sender, e );
        else
        {
            var all = Unsafe.As<SequentialEventHandler<TSender, TEvent>[]>( h );
            foreach( var x in all )
            {
                if( cancel.IsCancellationRequested ) break;
                x.Invoke( monitor, sender, e );
            }
        }
    }

    public Task RaiseSequentialAsync<TSender, TEvent>( IActivityMonitor monitor, TSender sender, TEvent e, CancellationToken cancel )
    {
        var h = _handler;
        if( h == null || cancel.IsCancellationRequested ) return Task.CompletedTask;
        if( h is Delegate d ) return Unsafe.As<AsyncSequentialEventHandler<TSender, TEvent>>( d ).Invoke( monitor, sender, e, cancel );
        return RaiseSequentialAsync( monitor, Unsafe.As<AsyncSequentialEventHandler<TSender, TEvent>[]>( h ), sender, e, cancel );
    }

    static async Task RaiseSequentialAsync<TSender, TEvent>( IActivityMonitor monitor,
                                                            AsyncSequentialEventHandler<TSender, TEvent>[] all,
                                                            TSender sender,
                                                            TEvent e,
                                                            CancellationToken cancel )
    {
        foreach( var h in all ) await h.Invoke( monitor, sender, e, cancel ).ConfigureAwait( false );
    }

    public void CollectParallelTasks<TSender, TEvent>( IActivityMonitor monitor,
                                                      TSender sender,
                                                      TEvent e,
                                                      CancellationToken cancel,
                                                      ref List<Task>? tasks )
    {
        var h = _handler;
        if( h == null || cancel.IsCancellationRequested ) return;
        tasks ??= new List<Task>();
        if( h is Delegate d )
        {
            tasks.Add( Unsafe.As<ParallelAsyncEventHandler<TSender, TEvent>>( d ).Invoke( monitor.ParallelLogger, sender, e, cancel ) );
        }
        else
        {
            var all = Unsafe.As<ParallelAsyncEventHandler<TSender, TEvent>[]>( h );
            foreach( var a in all )
            {
                tasks.Add( a.Invoke( monitor.ParallelLogger, sender, e, cancel ) );
            }
        }
    }

    #endregion
}
