using NUnit.Framework;
using FluentAssertions;
using CK.PerfectEvent;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;
using System;
using System.Threading;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Diagnostics;

namespace CK.Core.Tests.Monitoring
{
    [TestFixture]
    public class BridgesTests
    {
        sealed class TrackingBridgedSender
        {
            readonly string _suffix;
            bool _handleSync;
            bool _handleAsync;
            bool _handleParallelAsync;

            public TrackingBridgedSender( PerfectEventSender<string> source, string name )
            {
                Source = source;
                Name = name;
                Target = new PerfectEventSender<string>();
                _suffix = '>' + Name + '!';
                source.CreateBridge( Target, Convert );
            }

            public int CallConvertCount { get; private set; }

            public string Convert( string input )
            {
                ++CallConvertCount;
                return input + _suffix;
            }

            public PerfectEventSender<string> Source { get; }

            public PerfectEventSender<string> Target { get; }

            public string Name { get; }

            public void Reset()
            {
                CallConvertCount = 0;
                SyncCallCount = 0;
                AsyncCallCount = 0;
                ParallelAsyncCallCount = 0;
                LastToken = null;
                LastSync = null;
                LastAsync = null;
                LastParallelAsync = null;
            }

            public bool HandleSync
            {
                get => _handleSync;
                set
                {
                    if( _handleSync != value )
                    {
                        _handleSync = value;
                        if( value ) Target.PerfectEvent.Sync += OnSync;
                        else Target.PerfectEvent.Sync -= OnSync;
                    }
                }
            }

            public bool HandleAsync
            {
                get => _handleAsync;
                set
                {
                    if( _handleAsync != value )
                    {
                        _handleAsync = value;
                        if( value ) Target.PerfectEvent.Async += OnAsync;
                        else Target.PerfectEvent.Async -= OnAsync;
                    }
                }
            }

            public bool HandleParallelAsync
            {
                get => _handleParallelAsync;
                set
                {
                    if( _handleParallelAsync != value )
                    {
                        _handleParallelAsync = value;
                        if( value ) Target.PerfectEvent.ParallelAsync += OnParallelAsync;
                        else Target.PerfectEvent.ParallelAsync -= OnParallelAsync;
                    }
                }
            }

            public (int S, int A, int P) Counts => (SyncCallCount, AsyncCallCount, ParallelAsyncCallCount);

            public (string? S, string? A, string? P) LastOnes => (LastSync, LastAsync, LastParallelAsync);

            public int SyncCallCount { get; private set; }

            public string? LastSync { get; private set; }

            void OnSync( IActivityMonitor monitor, string e )
            {
                e.EndsWith( _suffix ).Should().BeTrue();
                ++SyncCallCount;
                LastSync = e;
            }

            public int AsyncCallCount { get; private set; }

            public string? LastAsync { get; private set; }

            Task OnAsync( IActivityMonitor monitor, string e, CancellationToken cancel )
            {
                e.EndsWith( _suffix ).Should().BeTrue();
                ++AsyncCallCount;
                LastAsync = e;
                return Task.CompletedTask;
            }

            public int ParallelAsyncCallCount { get; private set; }

            public string? LastParallelAsync { get; private set; }

            public ActivityMonitor.DependentToken? LastToken { get; private set; }

            Task OnParallelAsync( ActivityMonitor.DependentToken token, string e, CancellationToken cancel )
            {
                e.EndsWith( _suffix ).Should().BeTrue();
                LastToken = token;
                ++ParallelAsyncCallCount;
                LastParallelAsync = e;
                return Task.CompletedTask;
            }

        }

        [Test]
        public async Task single_converter_call_and_single_DependentToken_and_breadth_first_traversal_Async()
        {
            using var gLog = TestHelper.Monitor.OpenInfo( nameof( single_converter_call_and_single_DependentToken_and_breadth_first_traversal_Async ) );

            var root = new PerfectEventSender<string>();
            var tA = new TrackingBridgedSender( root, "A" );
            var tA1 = new TrackingBridgedSender( tA.Target, "A1" );
            var tA1a = new TrackingBridgedSender( tA1.Target, "A1a" );
            var tA1a1 = new TrackingBridgedSender( tA1a.Target, "A1a1" );
            var tA1b = new TrackingBridgedSender( tA1.Target, "A1b" );
            var tB = new TrackingBridgedSender( root, "B" );

            root.HasHandlers.Should().BeFalse();
            tA1a1.HandleSync = true;
            root.HasHandlers.Should().BeTrue();
            await root.RaiseAsync( TestHelper.Monitor, "Test" );

            tA1a1.Counts.Should().Be( (1, 0, 0) );
            tA1a1.LastOnes.Should().Be( ("Test>A!>A1!>A1a!>A1a1!", null, null) );
            tA.CallConvertCount.Should().Be( 1 );
            tA1.CallConvertCount.Should().Be( 1 );
            tA1a.CallConvertCount.Should().Be( 1 );
            tA1a1.CallConvertCount.Should().Be( 1 );
            tA1a1.HandleSync = false;
            root.HasHandlers.Should().BeFalse();

            tA1a1.HandleSync = true;
            tA1a1.HandleAsync = true;
            await root.RaiseAsync( TestHelper.Monitor, "Hop" );

            tA1a1.Counts.Should().Be( (2, 1, 0) );
            tA1a1.LastOnes.Should().Be( ("Hop>A!>A1!>A1a!>A1a1!", "Hop>A!>A1!>A1a!>A1a1!", null) );
            // Converters have been called only once even if Sync and Async have been raised.
            tA.CallConvertCount.Should().Be( 2 );
            tA1.CallConvertCount.Should().Be( 2 );
            tA1a.CallConvertCount.Should().Be( 2 );
            tA1a1.CallConvertCount.Should().Be( 2 );

            tA1a1.HandleParallelAsync = true;
            await root.RaiseAsync( TestHelper.Monitor, "Hip" );
            tA1a1.Counts.Should().Be( (3, 2, 1) );
            tA1a1.LastOnes.Should().Be( ("Hip>A!>A1!>A1a!>A1a1!", "Hip>A!>A1!>A1a!>A1a1!", "Hip>A!>A1!>A1a!>A1a1!") );
            // Converters have been called only once even if Sync, Async and ParallelAync have been raised.
            tA.CallConvertCount.Should().Be( 3 );
            tA1.CallConvertCount.Should().Be( 3 );
            tA1a.CallConvertCount.Should().Be( 3 );
            tA1a1.CallConvertCount.Should().Be( 3 );
            tA1a1.LastToken.Should().NotBeNull();

            tA1a1.HandleAsync = false;
            tA1a1.HandleParallelAsync = false;
            root.HasHandlers.Should().BeTrue();
            tA1a1.HandleSync = false;
            root.HasHandlers.Should().BeFalse();

            tA.Reset();
            tA1.Reset();
            tA1a.Reset();
            tA1a1.Reset();
            tA1a1.Reset();

            var ordered = new List<string>();
            void SafeAddSync( IActivityMonitor monitor, string msg )
            {
                lock( ordered )
                {
                    ordered.Add( "-Sync-" + msg );
                }
            }
            async Task SafeAddASync( IActivityMonitor monitor, string msg, CancellationToken cancel )
            {
                await Task.Delay( Random.Shared.Next( 30 ), cancel );
                lock( ordered ) { ordered.Add( $">-Async-{msg}" ); }
                await Task.Delay( Random.Shared.Next( 30 ), cancel );
                lock( ordered ) { ordered.Add( $"<-Async-{msg}" ); }
            }
            async Task SafeAddParallelASync( ActivityMonitor.DependentToken token, string msg, CancellationToken cancel )
            {
                await Task.Delay( Random.Shared.Next( 30 ), cancel );
                lock( ordered ) { ordered.Add( $">-ParallelAsync-{msg}" ); }
                await Task.Delay( Random.Shared.Next( 30 ), cancel );
                lock( ordered ) { ordered.Add( $"<-ParallelAsync-{msg}" ); }
            }
            tA.Target.PerfectEvent.Sync += SafeAddSync;
            tA.Target.PerfectEvent.Async += SafeAddASync;
            tA.Target.PerfectEvent.ParallelAsync += SafeAddParallelASync;
            tA1.Target.PerfectEvent.Sync += SafeAddSync;
            tA1.Target.PerfectEvent.Async += SafeAddASync;
            tA1.Target.PerfectEvent.ParallelAsync += SafeAddParallelASync;
            tA1a.Target.PerfectEvent.Sync += SafeAddSync;
            tA1a.Target.PerfectEvent.Async += SafeAddASync;
            tA1a.Target.PerfectEvent.ParallelAsync += SafeAddParallelASync;
            tB.Target.PerfectEvent.Sync += SafeAddSync;
            tB.Target.PerfectEvent.Async += SafeAddASync;
            tB.Target.PerfectEvent.ParallelAsync += SafeAddParallelASync;

            tA1.HandleAsync = true;
            tA1a.HandleAsync = true;
            tB.HandleAsync = true;

            tA.HandleParallelAsync = true;
            tA1.HandleParallelAsync = true;
            tA1a.HandleParallelAsync = true;
            tB.HandleParallelAsync = true;

            await root.RaiseAsync( TestHelper.Monitor, "Hop" );
            tA.Counts.Should().Be( (0, 0, 1) );
            tA.LastOnes.Should().Be( (null, null, "Hop>A!") );
            tA1.Counts.Should().Be( (0, 1, 1) );
            tA1.LastOnes.Should().Be( (null, "Hop>A!>A1!", "Hop>A!>A1!") );
            tA1a.Counts.Should().Be( (0, 1, 1) );
            tA1a.LastOnes.Should().Be( (null, "Hop>A!>A1!>A1a!", "Hop>A!>A1!>A1a!") );
            tA1a1.Counts.Should().Be( (0, 0, 0) );
            tA1a1.LastOnes.Should().Be( (null, null, null) );
            tB.Counts.Should().Be( (0, 1, 1) );
            tB.LastOnes.Should().Be( (null, "Hop>B!", "Hop>B!") );
            tA.CallConvertCount.Should().Be( 1 );
            tA1.CallConvertCount.Should().Be( 1 );
            tA1a.CallConvertCount.Should().Be( 1 );
            tA1a1.CallConvertCount.Should().Be( 0 );
            tB.CallConvertCount.Should().Be( 1 );
            tA.LastToken.Should().NotBeNull().And.BeSameAs( tA1.LastToken ).And.BeSameAs( tA1a.LastToken ).And.BeSameAs( tB.LastToken );
            tA1a1.LastToken.Should().BeNull();

            ordered.Should().HaveCount( 4 + 2 * 4 + 2 * 4, "4 Sync, 2 x 4 Async and 2 x 4 ParallelAsync." );
            // Parallels are started first and do what they want (in parallel) while
            // Sync and then Async are executed.
            var noParallel = ordered.Where( t => !t.Contains( "-ParallelAsync-" ) );
            noParallel.Should().StartWith( new[]
            {
                // Synchronous callbacks are always called first (regardless of their depth in the bridges).
                // The order is deterministic: it is a breadth-first traversal of the bridges.
                "-Sync-Hop>A!",
                "-Sync-Hop>B!",
                "-Sync-Hop>A!>A1!",
                "-Sync-Hop>A!>A1!>A1a!",
                // Then all Asynchronous handlers are called (same breadth-first traversal order).
                ">-Async-Hop>A!",
                "<-Async-Hop>A!",
                ">-Async-Hop>B!",
                "<-Async-Hop>B!",
                ">-Async-Hop>A!>A1!",
                "<-Async-Hop>A!>A1!",
                ">-Async-Hop>A!>A1!>A1a!",
                "<-Async-Hop>A!>A1!>A1a!",
            } );
        }

        [Test]
        public async Task synchronizing_2_events_Async()
        {
            var strings = new PerfectEventSender<string>();
            var numbers = new PerfectEventSender<double>();

            var sTon = strings.CreateBridge( numbers, s => { double.TryParse( s, out var n ); return n; } );
            var nTos = numbers.CreateBridge( strings, n => n.ToString() );

            var received = new List<object>();
            numbers.PerfectEvent.Sync += ( monitor, o ) => received.Add( o );
            strings.PerfectEvent.Sync += ( monitor, o ) => received.Add( o );

            await numbers.RaiseAsync( TestHelper.Monitor, 1.0 );
            received.Should().BeEquivalentTo( new object[] { 1.0, "1" }, o => o.WithStrictOrdering() );

            received.Clear();
            await strings.RaiseAsync( TestHelper.Monitor, "3712" );
            received.Should().BeEquivalentTo( new object[] { "3712", 3712.0 }, o => o.WithStrictOrdering() );

            sTon.IsActive = false;

            received.Clear();
            await strings.RaiseAsync( TestHelper.Monitor, "3712" );
            received.Should().BeEquivalentTo( new object[] { "3712" }, o => o.WithStrictOrdering() );

            received.Clear();
            await numbers.RaiseAsync( TestHelper.Monitor, 42 );
            received.Should().BeEquivalentTo( new object[] { 42.0, "42" }, o => o.WithStrictOrdering() );

        }

        [Test]
        public async Task bridge_triggers_only_once_by_default_Async()
        {
            var strings = new PerfectEventSender<string>() { AllowMultipleEvents = true };
            var objects = new PerfectEventSender<object>() { AllowMultipleEvents = true };

            var sToO = strings.CreateBridge( objects, s => s );
            var sToOLen = strings.CreateBridge( objects, s => s.Length );

            var receivedByObjects = new List<object>();
            objects.PerfectEvent.Sync += ( monitor, o ) => receivedByObjects.Add( o );
            var receivedByStrings = new List<object>();
            strings.PerfectEvent.Sync += ( monitor, s ) => receivedByStrings.Add( s );

            await strings.RaiseAsync( TestHelper.Monitor, "Hop!" );
            receivedByObjects.Should().BeEquivalentTo( new object[] { "Hop!", 4 }, o => o.WithStrictOrdering(),
                "First bridge is the string unchanged, second is the length: objects received both." );
            receivedByObjects.Clear();
            receivedByStrings.Should().BeEquivalentTo( new [] { "Hop!" }, o => o.WithStrictOrdering(),
                "The strings received its own raising." );
            receivedByStrings.Clear();

            // Now, we bridge objects back to strings:
            var backBridge = objects.CreateBridge( strings, o => "From Objects: " + o.ToString() );

            // When we send a string, the "back bridge" works.
            await strings.RaiseAsync( TestHelper.Monitor, "Hop!" );
            receivedByStrings.Should().BeEquivalentTo( new[] { "Hop!", "From Objects: Hop!" }, o => o.WithStrictOrdering(),
                "The back bridge did its job." );
            receivedByStrings.Clear();
            receivedByObjects.Should().BeEquivalentTo( new object[] { "Hop!", 4 }, o => o.WithStrictOrdering(),
                "The sToOLen 's => s.Length' bridge triggered only once (on the initial string)." );
            receivedByObjects.Clear();

            // Raising from preStrings leads to the same result. This is coherent.
            var preStrings = new PerfectEventSender<string>();
            preStrings.CreateBridge( strings, s => "From PreStrings: " + s );

            await preStrings.RaiseAsync( TestHelper.Monitor, "Hip!" );
            receivedByObjects.Should().BeEquivalentTo( new object[] { "From PreStrings: Hip!", 21 }, o => o.WithStrictOrdering(),
                "Not a big surprise." );
            receivedByObjects.Clear();
            receivedByStrings.Should().BeEquivalentTo( new[] { "From PreStrings: Hip!", "From Objects: From PreStrings: Hip!" }, o => o.WithStrictOrdering(),
                "No big surprise again." );
            receivedByStrings.Clear();

            // This deactivate the backbridge (when sending through preStrings or strings).
            backBridge.OnlyFromSource = true;
            await preStrings.RaiseAsync( TestHelper.Monitor, "Hip!" );
            receivedByObjects.Should().BeEquivalentTo( new object[] { "From PreStrings: Hip!", 21 }, o => o.WithStrictOrdering(),
                "Not a big surprise." );
            receivedByObjects.Clear();
            receivedByStrings.Should().BeEquivalentTo( new[] { "From PreStrings: Hip!" }, o => o.WithStrictOrdering(),
                "No more back bridge." );
            receivedByStrings.Clear();
        }


        [Test]
        public async Task bridge_can_filter_the_events_Async()
        {
            var integers = new PerfectEventSender<int>();
            var bigIntegers = new PerfectEventSender<int>();

            var onlyBig = integers.CreateFilteredBridge( bigIntegers, i => i > 1000, i => i );
            var bigToInt = bigIntegers.CreateBridge( integers, i => i );

            var bigReceived = new List<int>();
            bigIntegers.PerfectEvent.Sync += ( monitor, i ) => bigReceived.Add( i );
            var intReceived = new List<int>();
            integers.PerfectEvent.Sync += ( monitor, i ) => intReceived.Add( i );

            await integers.RaiseAsync( TestHelper.Monitor, 1 );
            bigReceived.Should().BeEmpty();
            intReceived.Should().BeEquivalentTo( new[] { 1 }, o => o.WithStrictOrdering() );
            intReceived.Clear();

            await integers.RaiseAsync( TestHelper.Monitor, 2000 );
            bigReceived.Should().BeEquivalentTo( new[] { 2000 }, o => o.WithStrictOrdering() );
            bigReceived.Clear();
            intReceived.Should().BeEquivalentTo( new[] { 2000 }, o => o.WithStrictOrdering() );
            intReceived.Clear();

            await bigIntegers.RaiseAsync( TestHelper.Monitor, 3000 );
            bigReceived.Should().BeEquivalentTo( new[] { 3000 }, o => o.WithStrictOrdering() );
            bigReceived.Clear();
            intReceived.Should().BeEquivalentTo( new[] { 3000 }, o => o.WithStrictOrdering() );
            intReceived.Clear();

            // Now we add a bridge from integers to bigIntegers that multiplies the integer.
            var intMultiplier = integers.CreateBridge( bigIntegers, i => i * 100 );

            // The filter is on the bridge, not on the target!
            // Adding an optional filter to the target itself may be useful one day...
            await integers.RaiseAsync( TestHelper.Monitor, 2 );
            // The filter and the multiplier did their job: 2 is filtered out and multiplied has been received.
            bigReceived.Should().BeEquivalentTo( new[] { 200 }, o => o.WithStrictOrdering() );
            bigReceived.Clear();
            intReceived.Should().BeEquivalentTo( new[] { 2 }, o => o.WithStrictOrdering() );
            intReceived.Clear();

            await integers.RaiseAsync( TestHelper.Monitor, 2000 );
            // The filter let the initial big flow.
            // The multiplied came later but AllowMultipleEvents is false.
            bigReceived.Should().BeEquivalentTo( new[] { 2000 }, o => o.WithStrictOrdering() );
            bigReceived.Clear();
            intReceived.Should().BeEquivalentTo( new[] { 2000 }, o => o.WithStrictOrdering() );
            intReceived.Clear();

            // Same as above but bigIntegers.AllowMultipleEvents is true now.
            bigIntegers.AllowMultipleEvents = true;
            await integers.RaiseAsync( TestHelper.Monitor, 2000 );
            bigReceived.Should().BeEquivalentTo( new[] { 2000, 2000*100 }, o => o.WithStrictOrdering() );
            bigReceived.Clear();
            intReceived.Should().BeEquivalentTo( new[] { 2000 }, o => o.WithStrictOrdering() );
            intReceived.Clear();

            // Same as above but integers.AllowMultipleEvents is also true.
            integers.AllowMultipleEvents = true;
            await integers.RaiseAsync( TestHelper.Monitor, 2000 );
            bigReceived.Should().BeEquivalentTo( new[] { 2000, 2000 * 100 }, o => o.WithStrictOrdering() );
            bigReceived.Clear();
            // integers received its initial value and the first big from bigIntegers.
            // To receive also the 2000 * 100, the bigToInt bridge should allow more than one call.
            // If this happens to be useful, a IBridge.MaxCallCount { get; set; } (defaults to 1) can
            // be added (and the code in StartRaise adapted to handle this).
            intReceived.Should().BeEquivalentTo( new[] { 2000, 2000 }, o => o.WithStrictOrdering() );
            intReceived.Clear();

        }

        [Test]
        public async Task bridge_can_filter_the_events_with_a_single_FilterConverter_method_Async()
        {
            var strings = new PerfectEventSender<string>();
            var integers = new PerfectEventSender<int>();

            // This can also be written like this:
            // var sToI = strings.CreateFilteredBridge( integers, ( string s, out int i ) => int.TryParse( s, out i ) );
            var sToI = strings.CreateFilteredBridge( integers, int.TryParse );

            var intReceived = new List<int>();
            integers.PerfectEvent.Sync += ( monitor, i ) => intReceived.Add( i );

            await strings.RaiseAsync( TestHelper.Monitor, "not an int" );
            intReceived.Should().BeEmpty( "integers didn't receive the not parsable string." );

            // We now raise a valid int string.
            await strings.RaiseAsync( TestHelper.Monitor, "3712" );
            intReceived.Should().BeEquivalentTo( new[] { 3712 }, "string -> int" );
        }

    }
}
