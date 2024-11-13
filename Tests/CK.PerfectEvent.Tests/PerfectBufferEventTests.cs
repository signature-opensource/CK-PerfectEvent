using NUnit.Framework;
using FluentAssertions;
using CK.PerfectEvent;
using System.Threading.Tasks;
using System.Threading;
using static CK.Testing.MonitorTestHelper;
using System;

namespace CK.Core.Tests.Monitoring;

[TestFixture]
public class PerfectBufferEventTests
{
    [Test]
    [CancelAfter( 1000 )]
    public async Task waiting_for_single_or_multiple_events_Async( CancellationToken cancellation )
    {
        var sender = new PerfectEventSender<int>();
        using var buffer = new PerfectEventBuffer<int>( sender.PerfectEvent, 10 );

        await sender.RaiseAsync( TestHelper.Monitor, 0, cancellation );
        var multiple = await buffer.WaitForAsync( 1, cancellation );
        multiple.Should().BeEquivalentTo( [0] );

        await sender.RaiseAsync( TestHelper.Monitor, 1, cancellation );
        await sender.RaiseAsync( TestHelper.Monitor, 2, cancellation );
        await sender.RaiseAsync( TestHelper.Monitor, 3, cancellation );
        await sender.RaiseAsync( TestHelper.Monitor, 4, cancellation );
        var single = await buffer.WaitForOneAsync( cancellation );
        single.Should().Be( 1 );
        single = await buffer.WaitForOneAsync( cancellation );
        single.Should().Be( 2 );
        single = await buffer.WaitForOneAsync( cancellation );
        single.Should().Be( 3 );
        single = await buffer.WaitForOneAsync( cancellation );
        single.Should().Be( 4 );
        await sender.RaiseAsync( TestHelper.Monitor, 5, cancellation );
        await sender.RaiseAsync( TestHelper.Monitor, 6, cancellation );
        await sender.RaiseAsync( TestHelper.Monitor, 7, cancellation );
        await sender.RaiseAsync( TestHelper.Monitor, 8, cancellation );
        multiple = await buffer.WaitForAsync( 3, cancellation );
        multiple.Should().BeEquivalentTo( [5,6,7] );
        single = await buffer.WaitForOneAsync( cancellation );
        single.Should().Be( 8 );
    }

    [Test]
    [CancelAfter( 1000 )]
    public async Task CancellationToken_and_Timeout_Async( CancellationToken cancellation )
    {
        var sender = new PerfectEventSender<int>();
        using var buffer = new PerfectEventBuffer<int>( sender.PerfectEvent, 10 );

        await FluentActions.Awaiting( async () => await buffer.WaitForAsync( 2, TimeSpan.FromMilliseconds( 2 ) ) ).Should().ThrowAsync<TimeoutException>();
        await FluentActions.Awaiting( async () => await buffer.WaitForAsync( 2, TimeSpan.FromMilliseconds( 2 ), cancellation ) ).Should().ThrowAsync<TimeoutException>();

        {
            var cts = new CancellationTokenSource( 50 );
            await FluentActions.Awaiting( async () => await buffer.WaitForAsync( 2, cts.Token ) ).Should().ThrowAsync<OperationCanceledException>();
        }
        {
            var cts = new CancellationTokenSource( 50 );
            await FluentActions.Awaiting( async () => await buffer.WaitForAsync( 2, TimeSpan.FromMilliseconds( 1000 ), cts.Token ) ).Should().ThrowAsync<OperationCanceledException>();
        }

        await FluentActions.Awaiting( async () => await buffer.WaitForOneAsync( TimeSpan.FromMilliseconds( 2 ) ) ).Should().ThrowAsync<TimeoutException>();
        await FluentActions.Awaiting( async () => await buffer.WaitForOneAsync( TimeSpan.FromMilliseconds( 2 ), cancellation ) ).Should().ThrowAsync<TimeoutException>();

        {
            var cts = new CancellationTokenSource( 50 );
            await FluentActions.Awaiting( async () => await buffer.WaitForOneAsync( cts.Token ) ).Should().ThrowAsync<OperationCanceledException>();
        }
        {
            var cts = new CancellationTokenSource( 50 );
            await FluentActions.Awaiting( async () => await buffer.WaitForOneAsync( TimeSpan.FromMilliseconds( 1000 ), cts.Token ) ).Should().ThrowAsync<OperationCanceledException>();
        }

    }
}
