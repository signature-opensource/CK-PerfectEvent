using System;
using System.Linq;
using NUnit.Framework;
using System.Collections.Generic;
using FluentAssertions;
using CK.PerfectEvent;
using System.Threading.Tasks;
using System.Threading;
using System.Runtime.CompilerServices;
using static CK.Testing.MonitorTestHelper;

namespace CK.Core.Tests.Monitoring;

[TestFixture]
public partial class PerfectEventAdapterTests
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
        var receiver = new SoundReceiver();

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
        .Throw<ArgumentException>();
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
        .Throw<ArgumentException>();

    }

    sealed class SoundReceiver
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

        public Task OnSoundParallelAsync( object loggerOrToken, Sound e, CancellationToken cancel )
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

        public Task OnTalkParallelAsync( object loggerOrToken, Talk e, CancellationToken cancel )
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

    [Test]
    public void Dictionary_to_IReadOnlyDictionary_adaptation_fails_because_TValue_is_not_covariant()
    {
        PerfectEventSender<Dictionary<string, List<string>>> sender = new();
        var sameArguments = sender.PerfectEvent.Adapt<IReadOnlyDictionary<string, List<string>>>();

        FluentActions.Invoking( () => sender.PerfectEvent.Adapt<IReadOnlyDictionary<string, IList<string>>>() )
            .Should().Throw<ArgumentException>( @"The out modifier is just a ref: TryGetValue ""locks"" the TValue to be invariant..." );
    }

    static readonly Dictionary<string, List<string>> SampleDictionary = new()
    {
        { "One", new List<string>{ "A", "B" } },
        { "Two", new List<string>{ "C", "D" } }
    };

    static void HandleReadOnlyEvent( IActivityMonitor monitor, IReadOnlyDictionary<string, IReadOnlyList<string>> e )
    {
        e["One"][0].Should().Be( "A" );
        e["Two"][1].Should().Be( "D" );
        e.TryGetValue( "One", out var first ).Should().BeTrue();
        first![1].Should().Be( "B" );
    }

    static Task HandleReadOnlyEventAsync( IActivityMonitor monitor, IReadOnlyDictionary<string, IReadOnlyList<string>> e, CancellationToken cancel )
    {
        HandleReadOnlyEvent( monitor, e );
        return Task.CompletedTask;
    }

    static Task HandleReadOnlyEventAsync( object loggerOrToken, IReadOnlyDictionary<string, IReadOnlyList<string>> e, CancellationToken cancel )
    {
        HandleReadOnlyEvent( new ActivityMonitor(), e );
        return Task.CompletedTask;
    }

    [Test]
    public async Task There_is_NO_WAY_to_solve_Dictionary_to_IReadOnlyDictionary_via_Adapt_Async()
    {
        PerfectEventSender<Dictionary<string, List<string>>> sender = new();

        var original = sender.PerfectEvent;
        var totallyBuggy = Unsafe.As<PerfectEvent<Dictionary<string, List<string>>>,
                                     PerfectEvent<IReadOnlyDictionary<string, IReadOnlyList<string>>>>( ref original );

        totallyBuggy.Sync += HandleReadOnlyEvent;

        await FluentActions.Awaiting( () => sender.RaiseAsync( TestHelper.Monitor, SampleDictionary ) )
                           .Should().ThrowAsync<EntryPointNotFoundException>();
    }

    [Test]
    public async Task Bridge_with_a_converter_can_adapt_anything_Async()
    {
        PerfectEventSender<Dictionary<string, List<string>>> mutableEvent = new();
        PerfectEventSender<IReadOnlyDictionary<string, IReadOnlyList<string>>> readonlyEvent = new();
        PerfectEventSender<int> stringCountEvent = new();

        mutableEvent.HasHandlers.Should().BeFalse( "Nobody subscribed to anything." );

        mutableEvent.CreateBridge( readonlyEvent, d => d.AsIReadOnlyDictionary<string, List<string>, IReadOnlyList<string>>() );
        mutableEvent.CreateBridge( stringCountEvent, e => e.Values.Select( l => l.Count ).Sum() );

        mutableEvent.HasHandlers.Should().BeFalse( "A bridge is optimal: it doesn't register any handler until any of its downstream targets has handlers." );

        readonlyEvent.PerfectEvent.Sync += HandleReadOnlyEvent;

        mutableEvent.HasHandlers.Should().BeTrue( "A handler has been registered on the target: the source is aware." );

        await mutableEvent.RaiseAsync( TestHelper.Monitor, SampleDictionary );

        readonlyEvent.PerfectEvent.Sync -= HandleReadOnlyEvent;

        mutableEvent.HasHandlers.Should().BeFalse( "The source is aware that the target has no more handlers." );
    }

    [Test]
    public void Bridge_can_be_disposed_or_IsActive_set_to_false_to_remove_the_adaptation()
    {
        PerfectEventSender<Dictionary<string, List<string>>> mutableEvent = new();
        PerfectEventSender<IReadOnlyDictionary<string, IReadOnlyList<string>>> readonlyEvent = new();

        {
            var b = mutableEvent.CreateBridge( readonlyEvent, d => d.AsIReadOnlyDictionary<string, List<string>, IReadOnlyList<string>>() );
            readonlyEvent.PerfectEvent.Sync += HandleReadOnlyEvent;
            mutableEvent.HasHandlers.Should().BeTrue();
            b.IsActive = false;
            mutableEvent.HasHandlers.Should().BeFalse();
            readonlyEvent.PerfectEvent.Sync -= HandleReadOnlyEvent;
        }
        {
            var b = mutableEvent.CreateBridge( readonlyEvent, d => d.AsIReadOnlyDictionary<string, List<string>, IReadOnlyList<string>>() );
            readonlyEvent.PerfectEvent.Async += HandleReadOnlyEventAsync;
            mutableEvent.HasHandlers.Should().BeTrue();
            b.IsActive = false;
            mutableEvent.HasHandlers.Should().BeFalse();
            readonlyEvent.PerfectEvent.Async -= HandleReadOnlyEventAsync;
        }
        {
            var b = mutableEvent.CreateBridge( readonlyEvent, d => d.AsIReadOnlyDictionary<string, List<string>, IReadOnlyList<string>>() );
            readonlyEvent.PerfectEvent.ParallelAsync += HandleReadOnlyEventAsync;
            mutableEvent.HasHandlers.Should().BeTrue();
            b.Dispose();
            mutableEvent.HasHandlers.Should().BeFalse();
            readonlyEvent.PerfectEvent.ParallelAsync -= HandleReadOnlyEventAsync;
        }
    }

    class GoodGuy
    {
        readonly IActivityMonitor _monitor;
        readonly PerfectEventSender<object> _source;
        PerfectEventSender<bool> _adapted;
        bool _received;

        public GoodGuy( int i, PerfectEventSender<object> source )
        {
            _monitor = new ActivityMonitor( $"Guy n°{i}" );
            _source = source;
            _adapted = new PerfectEventSender<bool>();
        }

        public async Task LoopAsync( CancellationToken stop )
        {
            var bridge = _source.CreateBridge( _adapted, o => o == this );
            int loopCount = 0;
            while( !stop.IsCancellationRequested )
            {
                switch( loopCount % 3 )
                {
                    case 0: _adapted.PerfectEvent.Sync += OnSync; break;
                    case 1: _adapted.PerfectEvent.Async += OnAsync; break;
                    case 2: _adapted.PerfectEvent.ParallelAsync += OnParallelAsync; break;
                }
                _received = false;
                // Do not pass the stop token here since we don't want an OperationCanceledException here.
                await _source.RaiseAsync( _monitor, this, default ).ConfigureAwait( false );
                _received.Should().BeTrue();
                switch( loopCount % 3 )
                {
                    case 0: _adapted.PerfectEvent.Sync -= OnSync; break;
                    case 1: _adapted.PerfectEvent.Async -= OnAsync; break;
                    case 2: _adapted.PerfectEvent.ParallelAsync -= OnParallelAsync; break;
                }
            }
        }

        void OnSync( IActivityMonitor monitor, bool b )
        {
            if( b ) _received = true;
        }

        Task OnAsync( IActivityMonitor monitor, bool b, CancellationToken cancel )
        {
            if( b ) _received = true;
            return Task.CompletedTask;
        }

        Task OnParallelAsync( object loggerOrToken, bool b, CancellationToken cancel )
        {
            if( b ) _received = true;
            return Task.CompletedTask;
        }

    }

    [Test]
    public async Task concurrent_stress_Bridge_Async()
    {
        using var gLog = TestHelper.Monitor.OpenInfo( nameof( concurrent_stress_Bridge_Async ) );

        var source = new PerfectEventSender<object>();
        var guys = Enumerable.Range( 0, 70 ).Select( i => new GoodGuy( i, source ) );
        var stop = new CancellationTokenSource( 10000 );
        var tasks = guys.Select( g => Task.Run( () => g.LoopAsync( stop.Token ) ) ).ToArray();

        await Task.WhenAll( tasks );
        source.HasHandlers.Should().BeFalse();
    }

}
