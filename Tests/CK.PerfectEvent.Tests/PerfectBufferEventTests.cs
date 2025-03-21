using NUnit.Framework;
using Shouldly;
using CK.PerfectEvent;
using System.Threading.Tasks;
using System.Threading;
using System;
using static CK.Testing.MonitorTestHelper;

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
        multiple.ShouldBe( [0] );

        await sender.RaiseAsync( TestHelper.Monitor, 1, cancellation );
        await sender.RaiseAsync( TestHelper.Monitor, 2, cancellation );
        await sender.RaiseAsync( TestHelper.Monitor, 3, cancellation );
        await sender.RaiseAsync( TestHelper.Monitor, 4, cancellation );
        var single = await buffer.WaitForOneAsync( cancellation );
        single.ShouldBe( 1 );
        single = await buffer.WaitForOneAsync( cancellation );
        single.ShouldBe( 2 );
        single = await buffer.WaitForOneAsync( cancellation );
        single.ShouldBe( 3 );
        single = await buffer.WaitForOneAsync( cancellation );
        single.ShouldBe( 4 );
        await sender.RaiseAsync( TestHelper.Monitor, 5, cancellation );
        await sender.RaiseAsync( TestHelper.Monitor, 6, cancellation );
        await sender.RaiseAsync( TestHelper.Monitor, 7, cancellation );
        await sender.RaiseAsync( TestHelper.Monitor, 8, cancellation );
        multiple = await buffer.WaitForAsync( 3, cancellation );
        multiple.ShouldBe( [5,6,7] );
        single = await buffer.WaitForOneAsync( cancellation );
        single.ShouldBe( 8 );
    }

    [Test]
    [CancelAfter( 1000 )]
    public async Task CancellationToken_test_Async( CancellationToken cancellation )
    {
        var sender = new PerfectEventSender<int>();
        using var buffer = new PerfectEventBuffer<int>( sender.PerfectEvent, 10 );

        {
            var cts = new CancellationTokenSource( 50 );
            await Util.Awaitable( async () => await buffer.WaitForAsync( 2, cts.Token ) ).ShouldThrowAsync<OperationCanceledException>();
        }

        await sender.RaiseAsync( TestHelper.Monitor, 3712, cancellation );
        (await buffer.WaitForOneAsync( cancellation )).ShouldBe( 3712 );

        {
            var cts = new CancellationTokenSource( 50 );
            await Util.Awaitable( async () => await buffer.WaitForAsync( 2, cts.Token ) ).ShouldThrowAsync<OperationCanceledException>();
        }

        await sender.RaiseAsync( TestHelper.Monitor, 42, cancellation );
        (await buffer.WaitForOneAsync( cancellation )).ShouldBe( 42 );

    }
}
