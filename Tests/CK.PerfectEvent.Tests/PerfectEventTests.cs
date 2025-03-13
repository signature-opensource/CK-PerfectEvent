using System.Linq;
using NUnit.Framework;
using Shouldly;
using CK.PerfectEvent;
using System.Threading.Tasks;
using System.Threading;
using static CK.Testing.MonitorTestHelper;

namespace CK.Core.Tests.Monitoring;

[TestFixture]
public class PerfectEventTests
{
    [Test]
    public async Task subscribing_unsubscribing_and_raising_Async()
    {
        var sender = new PerfectEventSender<int>();

        sender.PerfectEvent.Sync += OnSync;
        sender.PerfectEvent.Async += OnAsync;
        sender.PerfectEvent.ParallelAsync += OnParallelAsync;

        using( TestHelper.Monitor.CollectEntries( out var entries, LogLevelFilter.Warn ) )
        {
            await sender.RaiseAsync( TestHelper.Monitor, 1 );
            entries.Select( e => e.Text ).Order().ShouldBe( ["Async 1", "ParallelAsync 1", "Sync 1"] );
        }

        sender.PerfectEvent.Async += OnAsync;

        using( TestHelper.Monitor.CollectEntries( out var entries, LogLevelFilter.Warn ) )
        {
            await sender.RaiseAsync( TestHelper.Monitor, 2 );
            entries.Select( e => e.Text ).Order().ShouldBe( ["Async 2", "Async 2", "ParallelAsync 2", "Sync 2"] );
        }

        sender.PerfectEvent.Sync -= OnSync;

        using( TestHelper.Monitor.CollectEntries( out var entries, LogLevelFilter.Warn ) )
        {
            await sender.RaiseAsync( TestHelper.Monitor, 3 );
            entries.Select( e => e.Text ).Order().ShouldBe( ["Async 3", "Async 3", "ParallelAsync 3"] );
        }

        sender.PerfectEvent.ParallelAsync -= OnParallelAsync;

        using( TestHelper.Monitor.CollectEntries( out var entries, LogLevelFilter.Warn ) )
        {
            await sender.RaiseAsync( TestHelper.Monitor, 4 );
            entries.Select( e => e.Text ).Order().ShouldBe( ["Async 4", "Async 4"] );
        }

        sender.PerfectEvent.Async -= OnAsync;

        using( TestHelper.Monitor.CollectEntries( out var entries, LogLevelFilter.Warn ) )
        {
            await sender.RaiseAsync( TestHelper.Monitor, 5 );
            entries.Select( e => e.Text ).ShouldHaveSingleItem().ShouldBe( "Async 5" );
        }

        sender.PerfectEvent.Async -= OnAsync;

        using( TestHelper.Monitor.CollectEntries( out var entries, LogLevelFilter.Warn ) )
        {
            await sender.RaiseAsync( TestHelper.Monitor, 5 );
            entries.Select( e => e.Text ).ShouldBeEmpty();
        }

        sender.PerfectEvent.Async -= OnAsync;

        using( TestHelper.Monitor.CollectEntries( out var entries, LogLevelFilter.Warn ) )
        {
            await sender.RaiseAsync( TestHelper.Monitor, 5 );
            entries.Select( e => e.Text ).ShouldBeEmpty();
        }

        void OnSync( IActivityMonitor monitor, int e )
        {
            TestHelper.Monitor.Warn( $"Sync {e}" );
        }

        Task OnAsync( IActivityMonitor monitor, int e, CancellationToken cancel )
        {
            TestHelper.Monitor.Warn( $"Async {e}" );
            return Task.CompletedTask;
        }

        Task OnParallelAsync( object loggerOrToken, int e, CancellationToken cancel )
        {
            // This is buggy! 
            TestHelper.Monitor.Warn( $"ParallelAsync {e}" );
            return Task.CompletedTask;
        }

    }

    [Test]
    public async Task subscribing_unsubscribing_and_raising_with_Sender_Async()
    {
        PerfectEventSender<PerfectEventTests, int> sender = new();

        sender.PerfectEvent.Sync += OnSync;
        sender.PerfectEvent.Async += OnAsync;
        sender.PerfectEvent.ParallelAsync += OnParallelAsync;

        using( TestHelper.Monitor.CollectEntries( out var entries, LogLevelFilter.Warn ) )
        {
            await sender.RaiseAsync( TestHelper.Monitor, this, 1 );
            entries.Select( e => e.Text ).Order().ShouldBe( ["Async 1", "ParallelAsync 1", "Sync 1"] );
        }
        sender.PerfectEvent.Async += OnAsync;

        using( TestHelper.Monitor.CollectEntries( out var entries, LogLevelFilter.Warn ) )
        {
            await sender.RaiseAsync( TestHelper.Monitor, this, 2 );
            entries.Select( e => e.Text ).Order().ShouldBe( ["Async 2", "Async 2", "ParallelAsync 2", "Sync 2"] );
        }

        sender.PerfectEvent.Sync -= OnSync;

        using( TestHelper.Monitor.CollectEntries( out var entries, LogLevelFilter.Warn ) )
        {
            await sender.RaiseAsync( TestHelper.Monitor, this, 3 );
            entries.Select( e => e.Text ).Order().ShouldBe( ["Async 3", "Async 3", "ParallelAsync 3"] );
        }

        sender.PerfectEvent.ParallelAsync -= OnParallelAsync;

        using( TestHelper.Monitor.CollectEntries( out var entries, LogLevelFilter.Warn ) )
        {
            await sender.RaiseAsync( TestHelper.Monitor, this, 4 );
            entries.Select( e => e.Text ).Order().ShouldBe( ["Async 4", "Async 4"] );
        }

        sender.PerfectEvent.Async -= OnAsync;

        using( TestHelper.Monitor.CollectEntries( out var entries, LogLevelFilter.Warn ) )
        {
            await sender.RaiseAsync( TestHelper.Monitor, this, 5 );
            entries.Select( e => e.Text ).ShouldHaveSingleItem().ShouldBe( "Async 5" );
        }

        sender.PerfectEvent.Async -= OnAsync;

        using( TestHelper.Monitor.CollectEntries( out var entries, LogLevelFilter.Warn ) )
        {
            await sender.RaiseAsync( TestHelper.Monitor, this, 5 );
            entries.Select( e => e.Text ).ShouldBeEmpty();
        }

        sender.PerfectEvent.Async -= OnAsync;

        using( TestHelper.Monitor.CollectEntries( out var entries, LogLevelFilter.Warn ) )
        {
            await sender.RaiseAsync( TestHelper.Monitor, this, 5 );
            entries.Select( e => e.Text ).ShouldBeEmpty();
        }

        void OnSync( IActivityMonitor monitor, PerfectEventTests sender, int e )
        {
            TestHelper.Monitor.Warn( $"Sync {e}" );
        }

        Task OnAsync( IActivityMonitor monitor, PerfectEventTests sender, int e, CancellationToken cancel )
        {
            TestHelper.Monitor.Warn( $"Async {e}" );
            return Task.CompletedTask;
        }

        Task OnParallelAsync( object loggerOrToken, PerfectEventTests sender, int e, CancellationToken cancel )
        {
            // This is buggy! 
            TestHelper.Monitor.Warn( $"ParallelAsync {e}" );
            return Task.CompletedTask;
        }

    }

}
