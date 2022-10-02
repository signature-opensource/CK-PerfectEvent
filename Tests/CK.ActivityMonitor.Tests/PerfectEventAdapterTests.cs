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

namespace CK.Core.Tests.Monitoring
{
    [TestFixture]
    public class PerfectEventAdapterTests
    {
        class Sound
        {
            public int Volume { get; init; }
        }

        class Talk : Sound
        {
            public string Speech { get; init; }
        }

        interface ISoundEmitter
        {
            public PerfectEvent<Sound> Sound { get; }
        }

        class Parrot : ISoundEmitter
        {
            readonly PerfectEventSender<Talk> _sender = new();

            public PerfectEvent<Talk> Speaking => _sender.PerfectEvent;

            PerfectEvent<Sound> ISoundEmitter.Sound => _sender.PerfectEvent.Adapt<Sound>();

            public Task TalkAsync( IActivityMonitor monitor, int volume, string speech )
            {
                return _sender.RaiseAsync( monitor, new Talk { Volume = volume, Speech = speech } );
            }
        }

        [Test]
        public async Task test_with_adapter_Async()
        {
            Parrot parrot = new Parrot();
            ISoundEmitter emitter = parrot;
            var receiver = new Receiver();

            emitter.Sound.Sync += receiver.OnSoundSync;
            emitter.Sound.Async += receiver.OnSoundAsync;
            emitter.Sound.ParallelAsync += receiver.OnSoundParallelAsync;

            parrot.Speaking.Sync += receiver.OnTalkSync;
            parrot.Speaking.Async += receiver.OnTalkAsync;
            parrot.Speaking.ParallelAsync += receiver.OnTalkParallelAsync;

            await parrot.TalkAsync( TestHelper.Monitor, 42, "Hip!" );
            await parrot.TalkAsync( TestHelper.Monitor, 3712, "Hop!" );

            receiver.Result.Should().HaveCount( 2 * 6 );
            receiver.Result.Where( s => s.Contains( "42." ) ).Should().HaveCount( 3 );
            receiver.Result.Where( s => s.Contains( "42, Hip!." ) ).Should().HaveCount( 3 );
            receiver.Result.Where( s => s.Contains( "3712." ) ).Should().HaveCount( 3 );
            receiver.Result.Where( s => s.Contains( "3712, Hop!." ) ).Should().HaveCount( 3 );
        }

        [Test]
        public void adapters_are_unfornately_checked_at_runtime_and_throw_on_value_type()
        {
            PerfectEventSender<int> sender = new();

            FluentActions.Invoking( () =>
                {
                    sender.PerfectEvent.Adapt<object>().Sync += ( monitor, e ) => Throw.NotSupportedException( "Never called since Adapt throws." );
                    Throw.NotSupportedException( "Never called since Adapt throws." );
                }
            ).Should()
            .Throw<InvalidOperationException>();
        }

        [Test]
        public void adapters_are_unfornately_checked_at_runtime_and_throw_on_non_assignable_types()
        {
            PerfectEventSender<Action<int>> sender = new();

            FluentActions.Invoking( () =>
            {
                sender.PerfectEvent.Adapt<Action>().Sync += ( monitor, e ) => Throw.NotSupportedException( "Never called since Adapt throws." );
                Throw.NotSupportedException( "Never called since Adapt throws." );
            }
            ).Should()
            .Throw<InvalidOperationException>();

        }

        class Receiver
        {
            List<string> _strings = new();

            void Add( string v )
            {
                lock( _strings ) _strings.Add( v );
            }

            public IReadOnlyList<string> Result => _strings;

            public void OnSoundSync( IActivityMonitor monitor, Sound e )
            {
                Add( $"SoundSync {e.Volume}." );
            }

            public Task OnSoundAsync( IActivityMonitor monitor, Sound e, CancellationToken cancel )
            {
                Add( $"SoundAsync {e.Volume}." );
                return Task.CompletedTask;
            }

            public Task OnSoundParallelAsync( ActivityMonitor.DependentToken token, Sound e, CancellationToken cancel )
            {
                Add( $"SoundParallelAsync {e.Volume}." );
                return Task.CompletedTask;
            }

            public void OnTalkSync( IActivityMonitor monitor, Talk e )
            {
                Add( $"TalkSync {e.Volume}, {e.Speech}." );
            }

            public Task OnTalkAsync( IActivityMonitor monitor, Talk e, CancellationToken cancel )
            {
                Add( $"TalkAsync {e.Volume}, {e.Speech}." );
                return Task.CompletedTask;
            }

            public Task OnTalkParallelAsync( ActivityMonitor.DependentToken token, Talk e, CancellationToken cancel )
            {
                Add( $"TalkParallelAsync {e.Volume}, {e.Speech}." );
                return Task.CompletedTask;
            }

        }

        [Test]
        public async Task no_adapter_required_for_signatures_Async()
        {
            PerfectEventSender<Action<int>> sender = new();

            sender.PerfectEvent.Sync += OnDelegateSync;
            sender.PerfectEvent.Sync += OnActionSync;

            using( TestHelper.Monitor.CollectEntries( out var entries, LogLevelFilter.Info ) )
            {
                await sender.RaiseAsync( TestHelper.Monitor, i => TestHelper.Monitor.Info( $"Action {i}" ) );
                entries.Select( e => e.Text ).Should().BeEquivalentTo( new[]
                {
                    "Received Action and executing it.",
                    "Action 3712",
                    "Received Delegate."
                } );
            }

            static void OnDelegateSync( IActivityMonitor monitor, Delegate e )
            {
                TestHelper.Monitor.Info( "Received Delegate." );
            }

            static void OnActionSync( IActivityMonitor monitor, Action<int> a )
            {
                TestHelper.Monitor.Info( "Received Action and executing it." );
                a( 3712 );
            }
        }

    }
}
