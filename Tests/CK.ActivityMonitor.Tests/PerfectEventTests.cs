using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using System.Xml.Linq;
using System.Collections.Generic;
using FluentAssertions;
using System.Diagnostics;
using CK.PerfectEvent;
using System.Threading.Tasks;
using System.Threading;
using System.ComponentModel;
using static CK.Testing.MonitorTestHelper;
using Newtonsoft.Json.Linq;
using static CK.Monitoring.MultiLogReader;

namespace CK.Core.Tests.Monitoring
{
    [TestFixture]
    public class PerfectEventTests
    {
        [Test]
        public async Task subscribing_unsubscribing_and_raising_Async()
        {
            PerfectEventSender<int> sender = new();

            sender.PerfectEvent.Sync += OnSync;
            sender.PerfectEvent.Async += OnAsync;
            sender.PerfectEvent.ParallelAsync += OnParallelAsync;

            using( TestHelper.Monitor.CollectEntries( out var entries, LogLevelFilter.Warn ) )
            {
                await sender.RaiseAsync( TestHelper.Monitor, 1 );
                entries.Select( e => e.Text ).Should().BeEquivalentTo( new[] { "Sync 1", "Async 1", "ParallelAsync 1" } );
            }

            sender.PerfectEvent.Async += OnAsync;

            using( TestHelper.Monitor.CollectEntries( out var entries, LogLevelFilter.Warn ) )
            {
                await sender.RaiseAsync( TestHelper.Monitor, 2 );
                entries.Select( e => e.Text ).Should().BeEquivalentTo( new[] { "Sync 2", "Async 2", "Async 2", "ParallelAsync 2" }, o => o.WithoutStrictOrdering() );
            }

            sender.PerfectEvent.Sync -= OnSync;

            using( TestHelper.Monitor.CollectEntries( out var entries, LogLevelFilter.Warn ) )
            {
                await sender.RaiseAsync( TestHelper.Monitor, 3 );
                entries.Select( e => e.Text ).Should().BeEquivalentTo( new[] { "Async 3", "Async 3", "ParallelAsync 3" }, o => o.WithoutStrictOrdering() );
            }

            sender.PerfectEvent.ParallelAsync -= OnParallelAsync;

            using( TestHelper.Monitor.CollectEntries( out var entries, LogLevelFilter.Warn ) )
            {
                await sender.RaiseAsync( TestHelper.Monitor, 4 );
                entries.Select( e => e.Text ).Should().BeEquivalentTo( new[] { "Async 4", "Async 4" }, o => o.WithoutStrictOrdering() );
            }

            sender.PerfectEvent.Async -= OnAsync;

            using( TestHelper.Monitor.CollectEntries( out var entries, LogLevelFilter.Warn ) )
            {
                await sender.RaiseAsync( TestHelper.Monitor, 5 );
                entries.Select( e => e.Text ).Should().BeEquivalentTo( new[] { "Async 5" }, o => o.WithoutStrictOrdering() );
            }

            sender.PerfectEvent.Async -= OnAsync;

            using( TestHelper.Monitor.CollectEntries( out var entries, LogLevelFilter.Warn ) )
            {
                await sender.RaiseAsync( TestHelper.Monitor, 5 );
                entries.Select( e => e.Text ).Should().BeEmpty();
            }

            sender.PerfectEvent.Async -= OnAsync;

            using( TestHelper.Monitor.CollectEntries( out var entries, LogLevelFilter.Warn ) )
            {
                await sender.RaiseAsync( TestHelper.Monitor, 5 );
                entries.Select( e => e.Text ).Should().BeEmpty();
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
                if( loggerOrToken is IParallelLogger logger ) logger.Warn( $"ParallelAsync {e}" );
                return Task.CompletedTask;
            }

        }

        [Test]
        public async Task subscribing_unsubscribing_and_raising_with_Sender_Async()
        {
            PerfectEventSender<PerfectEventTests,int> sender = new();

            sender.PerfectEvent.Sync += OnSync;
            sender.PerfectEvent.Async += OnAsync;
            sender.PerfectEvent.ParallelAsync += OnParallelAsync;

            using( TestHelper.Monitor.CollectEntries( out var entries, LogLevelFilter.Warn ) )
            {
                await sender.RaiseAsync( TestHelper.Monitor, this, 1 );
                entries.Select( e => e.Text ).Should().BeEquivalentTo( new[] { "Sync 1", "Async 1", "ParallelAsync 1" } );
            }
            sender.PerfectEvent.Async += OnAsync;

            using( TestHelper.Monitor.CollectEntries( out var entries, LogLevelFilter.Warn ) )
            {
                await sender.RaiseAsync( TestHelper.Monitor, this, 2 );
                entries.Select( e => e.Text ).Should().BeEquivalentTo( new[] { "Sync 2", "Async 2", "Async 2", "ParallelAsync 2" }, o => o.WithoutStrictOrdering() );
            }

            sender.PerfectEvent.Sync -= OnSync;

            using( TestHelper.Monitor.CollectEntries( out var entries, LogLevelFilter.Warn ) )
            {
                await sender.RaiseAsync( TestHelper.Monitor, this, 3 );
                entries.Select( e => e.Text ).Should().BeEquivalentTo( new[] { "Async 3", "Async 3", "ParallelAsync 3" }, o => o.WithoutStrictOrdering() );
            }

            sender.PerfectEvent.ParallelAsync -= OnParallelAsync;

            using( TestHelper.Monitor.CollectEntries( out var entries, LogLevelFilter.Warn ) )
            {
                await sender.RaiseAsync( TestHelper.Monitor, this, 4 );
                entries.Select( e => e.Text ).Should().BeEquivalentTo( new[] { "Async 4", "Async 4" }, o => o.WithoutStrictOrdering() );
            }

            sender.PerfectEvent.Async -= OnAsync;

            using( TestHelper.Monitor.CollectEntries( out var entries, LogLevelFilter.Warn ) )
            {
                await sender.RaiseAsync( TestHelper.Monitor, this, 5 );
                entries.Select( e => e.Text ).Should().BeEquivalentTo( new[] { "Async 5" }, o => o.WithoutStrictOrdering() );
            }

            sender.PerfectEvent.Async -= OnAsync;

            using( TestHelper.Monitor.CollectEntries( out var entries, LogLevelFilter.Warn ) )
            {
                await sender.RaiseAsync( TestHelper.Monitor, this, 5 );
                entries.Select( e => e.Text ).Should().BeEmpty();
            }

            sender.PerfectEvent.Async -= OnAsync;

            using( TestHelper.Monitor.CollectEntries( out var entries, LogLevelFilter.Warn ) )
            {
                await sender.RaiseAsync( TestHelper.Monitor, this, 5 );
                entries.Select( e => e.Text ).Should().BeEmpty();
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
                if( loggerOrToken is IParallelLogger logger ) logger.Warn( $"ParallelAsync {e}" );
                return Task.CompletedTask;
            }

        }

    }
}
