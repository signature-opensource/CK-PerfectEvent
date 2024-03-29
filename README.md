# Perfect Event

These events mimics standard .Net events but offer the support of asynchronous handlers.

> Be sure to understand the [standard .Net event pattern](https://docs.microsoft.com/en-us/dotnet/csharp/event-pattern) 
> before reading this.

Perfect events come in two flavors:
 - Events with a single event argument `PerfectEvent<TEvent>` that accepts the following callback signature:
 ```csharp
public delegate void SequentialEventHandler<TEvent>( IActivityMonitor monitor, TEvent e );
```
 - Events with the sender and event argument (like the .Net standard one) `PerfectEvent<TSender, TEvent>`:
```csharp
public delegate void SequentialEventHandler<TSender, TEvent>( IActivityMonitor monitor, TSender sender, TEvent e );
```

As it appears in the signatures above, a monitor is provided: the callee uses it so that its own actions naturally belong to
the calling activity.

## Subscribing and unsubscribing to a Perfect Event

### Synchronous handlers
Perfect events looks like regular events and support `+=` and `-=` operators. Given the fact that things can talk:
```csharp
public interface IThing
{
  string Name { get; }
  PerfectEvent<string> Talk { get; }
}
```
This is a typical listener code:
```csharp
void ListenTo( IThing o )
{
    o.Talk.Sync += ThingTalked;
}

void StopListeningTo( IThing o )
{
    o.Talk.Sync -= ThingTalked;
}

void ThingTalked( IActivityMonitor monitor, string e )
{
    monitor.Info( $"A thing said: '{e}'." );
}
```
### Asynchronous handlers

Let's say that this very first version is not enough:
 - We now want to know who is talking: we'll use the `PerfectEvent<TSender, TEvent>` event that includes the sender.
The `IThing` definition becomes:
```csharp
public interface IThing
{
  string Name { get; }
  PerfectEvent<IThing,string> Talk { get; }
}
```
 - We want to persist the talk in a database: it's better to use an asynchronous API to interact with the database.
The listener becomes:
```csharp
void ListenTo( IThing o )
{
    o.Talk.Async += ThingTalkedAsync;
}

void StopListeningTo( IThing o )
{
    o.Talk.Async -= ThingTalkedAsync;
}

async Task ThingTalkedAsync( IActivityMonitor monitor, IThing thing, string e )
{
    monitor.Info( $"Thing {thing.Name} said: '{e}'." );
    await _database.RecordAsync( monitor, thing.Name, e );
}
```

### Parallel handlers

Parallel handlers are a little bit more complex to implement and also more dangerous: as alway, concurrency must be
handled carefully.
The parallel handlers is not called with the origin monitor but with a `IParallelLogger` that can be used directly:

```csharp
void ListenTo( IThing o )
{
    o.Talk.ParallelAsync += ThingTalkedAsync;
}

void StopListeningTo( IThing o )
{
    o.Talk.ParallelAsync -= ThingTalkedAsync;
}

async Task ThingTalkedAsync( IParallelLogger logger, IThing thing, string e )
{
    logger.Info("I'm in the event !");
    // logger.OpenXXX Is not supported by the IParallelLogger.

    // If your event handler is a big process and need to have an ActivityMonitor, do the following:
    var token = logger.CreateDependentToken();

    var monitor = new ActivityMonitor( token );
    using(monitor.OpenInfo("A group!"))
    {

    }
    monitor.MonitorEnd();
}
```
A more realistic usage of Parallel handling would be to submit the event to an asynchronous worker (through a mailbox,
typically a System.Threading.Channel) and wait for its handling, the worker having its own monitor.
And if the handling of the event doesn't need to be awaited, then a Synchronous handler that synchronously pushes the
event into the worker's mailbox is the best solution.

## Implementing and raising a Perfect Event

A Perfect Event is implemented thanks to a [`PerfectEventSender<TEvent>`](CK.PerfectEvent/PerfectEventSender{TEvent}.cs)
or [`PerfectEventSender<TSender,TEvent>`](CK.PerfectEvent/SenderAndArg/PerfectEventSender{TSender,TEvent}.cs)

```csharp
class Thing : IThing
{
    // The sender must NOT be exposed: its PerfectEvent property is the external API. 
    readonly PerfectEventSender<IThing, string> _talk;

    public Thing( string name )
    {
        Name = name;
        _talk = new PerfectEventSender<IThing, string>();
    }

    public string Name { get; }

    public PerfectEvent<IThing, string> Talk => _talk.PerfectEvent;

    internal Task SaySomething( IActivityMonitor monitor, string something ) => _talk.RaiseAsync( monitor, this, something );
}
```

Calling `RaiseAsync` calls all the subscribed handlers and if any of them throws an exception, it is propagated to the caller.
Sometimes, we want to isolate the caller from any error in the handlers (handlers are "client code", they can be buggy).
`SafeRaiseAsync` protects the calls:

```csharp
/// <summary>
/// Same as <see cref="RaiseAsync"/> except that if exceptions occurred they are caught and logged
/// and a gentle false is returned.
/// <para>
/// The returned task is resolved once the parallels, the synchronous and the asynchronous event handlers have finished their jobs.
/// </para>
/// <para>
/// If exceptions occurred, they are logged and false is returned.
/// </para>
/// </summary>
/// <param name="monitor">The monitor to use.</param>
/// <param name="sender">The sender of the event.</param>
/// <param name="e">The argument of the event.</param>
/// <param name="fileName">The source filename where this event is raised.</param>
/// <param name="lineNumber">The source line number in the filename where this event is raised.</param>
/// <returns>True on success, false if an exception occurred.</returns>
public async Task<bool> SafeRaiseAsync( IActivityMonitor monitor, TSender sender, TEvent e, [CallerFilePath] string? fileName = null, [CallerLineNumber] int lineNumber = 0 )
```

## Adapting the event type

### Covariance: Usafe.As when types are compatible
The signature of the `PerfectEvent<TEvent>` locks the type to invariantly be `TEvent`.
However, a `PerfectEvent<Dog>` should be compatible with a `PerfectEvent<Animal>`: the event should be covariant, it
should be specified as `PerfectEvent<out TEvent>`. Unfortunately this is not possible because `PerfectEvent` is a struct
but even if we try to define an interface:
```csharp
public interface IPerfectEvent<out TEvent>
{
    bool HasHandlers { get; }
    event SequentialEventHandler<TEvent> Sync;
    event SequentialEventHandlerAsync<TEvent> Async;
    event ParallelEventHandlerAsync<TEvent> ParallelAsync;
}
```
This is not possible because the delegate signatures prevent it:
> `CS1961	Invalid variance: The type parameter 'TEvent' must be invariantly valid on 'IPerfectEvent<TEvent>.Sync'. 'TEvent' is covariant.`

Please note that we are talking of the `PerfectEvent<TEvent>` type here, this has nothing to do with the signature itself:
at the signature level, the rules apply and a `OnAnimal` handler can perfectly be assigned to/combined with a Dog handler:

```csharp
class Animal { }
class Dog : Animal { }

static void DemoVariance()
{
    SequentialEventHandler<Dog>? handlerOfDogs = null;
    SequentialEventHandler<Animal>? handlerOfAnimals = null;

    // Exact type match:
    handlerOfDogs += OnDog;
    handlerOfAnimals += OnAnimal;

    // This is possible: the delegate that accepts an Animal can be called with a Dog.
    handlerOfDogs += OnAnimal;

    // Of course, this is not possible: one cannot call a Dog handler with a Cat!
    // handlerOfAnimals += OnDog;
}

static void OnAnimal( IActivityMonitor monitor, Animal e ) { }

static void OnDog( IActivityMonitor monitor, Dog e ) { }
```
This is precisely what we would like to express at the type level: a `PerfectEvent<Dog>` **is a** `PerfectEvent<Animal>`
just like a `IEnumerable<Dog>` **is a** `IEnumerable<Animal>`.

The workaround is to provide an explicit way to adapt the type. A typical usage is to use explicit implementation and/or
the `new` masking operator to expose these adaptations:
```csharp
PerfectEventSender<object, Dog> _dogKilled = new();

PerfectEvent<Animal> IAnimalGarden.Killed => _dogKilled.PerfectEvent.Adapt<Animal>();

PerfectEvent<Dog> Killed => _dogKilled.PerfectEvent;
```

This `Adapt` method allows the event type to be adapted. In a perfect world, it would be defined in the following way:
```csharp
public readonly struct PerfectEvent<TEvent>
{
  ...

  public PerfectEvent<TNewEvent> Adapt<TNewEvent>() where TEvent : TNewEvent
  {
    ...
  }
}
```
The constraint `TEvent : TNewEvent` aims to restrict the adapted type to be a base class of the actual event type.
Unfortunately (again), generic constraints don't support this (and anyway this 'base class constraint' should be extended
to have the `IsAssignableFrom` semantics).

We cannot constrain the type this way, we cannot constrain it in any manner except the fact that the adapted type must be
a reference type (the `class` constraint).

So, the bad news is that there is as of today no compile time check for this `Adapt` method, but the good (or not so bad) news
is that safety is nevertheless checked at runtime when `Adapt` is called: adapters are forbidden when the event is a value type
(boxing is not handled) and the adapted type must be a reference type that is assignable from the event type.

When this is the case, an explicit conversion and another `PerfectEventSender` are required.

### Bridges: when types are not compatible
The `Adapt` method uses [Unsafe.As](http://unsafe.as). For this to work the types must be compliant, "true covariance"
is required: `IsAssignableFrom`, no conversion, no implicit boxing (precisely what is checked at runtime by `Adapt`).

Unfortunately sometimes we need to express a more "logical" covariance, typically to expose read only facade like
a `Dictionary<string,List<int>>` exposed as a `IReadOnlyDictionary<string,IReadOnlyList<int>>`.

This is not valid in .Net because the dictionary value is not defined as covariant: `IDictionary<TKey,TValue>` should
be `IDictionary<TKey,out TValue>` but it's not: the out parameter of `bool TryGetValue( TKey k, out TValue v)`
ironically "locks" the type of the value (under the hood, `out` is just a `ref`).


#### Creating and controlling bridges
To handle this and any other projections, a converter function must be used. `PerfectEventSender` can be bridged
to other ones:

```csharp
PerfectEventSender<Dictionary<string, List<string>>> mutableEvent = new();
PerfectEventSender<IReadOnlyDictionary<string, IReadOnlyList<string>>> readonlyEvent = new();
PerfectEventSender<int> stringCountEvent = new();

var bReadOnly = mutableEvent.CreateBridge( readonlyEvent, e => e.AsIReadOnlyDictionary<string, List<string>, IList<string>>() );
var bCount = mutableEvent.CreateBridge( stringCountEvent, e => e.Values.Select( l => l.Count ).Sum() );
```
Note: `AsIReadOnlyDictionary` is a helper available in CK.Core ([here](https://github.com/Invenietis/CK-Core/blob/master/CK.Core/Extension/DictionaryExtension.cs)).

`CreateBridge` returns a [`IBridge : IDisposable`](CK.PerfectEvent/IBridge.cs): if needed a bridge can
be activated or deactivated and definitely removed at any time.

The cool side effect of this "logical covariance" solution is that conversion can be done between any kind of types (string
to int, User to DomainEvent, etc.).

#### Bridges are safe

An important aspect of this feature is that bridge underlying implementation guaranties that:

- Existing bridges has no impact on the source `HasHandlers` property as long as their targets don't have handlers. 
This property can then be confidently used on a potential source of multiple events to totally skip raising events
(typically avoiding the event object instantiation).
- When raising an event, the converter is called once and only if the target has registered handlers or is itself bridged to 
other senders that have handlers.
- A dependent activity token is obtained once and only if at least one parallel handler exists on the source or
in any subsequent targets. This token is then shared by all the parallel events across all targets.
- Parallel, sequential and then asynchronous sequential handlers are called uniformly and in deterministic order 
(breadth-first traversal) across the source and all its bridged targets.
- Bridges can safely create cycles: bridges are triggered only once by their first occurrence in the 
breadth-first traversal of the bridges.
- All this stuff (raising events, adding/removing handlers, bridging and disposing bridges) is thread-safe and can 
be safely called concurrently.

There is no way to obtain these capabilities "from the outside": such bridges must be implemented "behind" the senders.

**The "cycle safe" capability is crucial**: bridges can be freely established between senders without any knowledge of
existing bridges. However, cycles are hard to figure out (even if logically sound) and the fact that more than one
event can be raised for a single call to `RaisAsync` or `SafeRaisAsync` can be annoying. By default, a sender raises
only one event per `RaisAsync` (the first it receives). This can be changed thanks to the
`bool AllowMultipleEvents { get; set; }` exposed by senders.

On the bridge side, the `bool OnlyFromSource { get; set; }` can restrict a bridge to its Source:
only events raised on the Source will be considered, events coming from other bridges
are ignored.

> Playing with "graph of senders" is not easy. Bridges should be used primarily as type
> adapters (or simple relays as described below) but bridges are always safe and can be used freely.

#### Bridges can also filter the events

The `CreateFilteredBridge` methods available on the `PerfectEventSender` and on the `PerfectEvent` enables event
filtering:
```csharp
/// <summary>
/// Creates a bridge from this event to another sender that can filter the event before
/// adapting the event type and raising the event on the target.
/// </summary>
/// <typeparam name="T">The target's event type.</typeparam>
/// <param name="target">The target that will receive converted events.</param>
/// <param name="filter">The filter that must be satisfied for the event to be raised on the target.</param>
/// <param name="converter">The conversion function.</param>
/// <param name="isActive">By default the new bridge is active.</param>
/// <returns>A new bridge.</returns>
public IBridge CreateFilteredBridge<T>( PerfectEventSender<T> target,
                                        Func<TEvent, bool> filter,
                                        Func<TEvent, T> converter,
                                        bool isActive = true );

/// <summary>
/// Creates a bridge from this event to another sender with a function that filters and converts at once
/// (think <see cref="int.TryParse(string?, out int)"/>).
/// </summary>
/// <typeparam name="T">The target's event type.</typeparam>
/// <param name="target">The target that will receive converted events.</param>
/// <param name="filterConverter">The filter and conversion function.</param>
/// <param name="isActive">By default the new bridge is active.</param>
/// <returns>A new bridge.</returns>
public IBridge CreateFilteredBridge<T>( PerfectEventSender<T> target,
                                        FilterConverter<TEvent,T> filterConverter,
                                        bool isActive = true );
```

This is easy and powerful:
```csharp
var strings = new PerfectEventSender<string>();
var integers = new PerfectEventSender<int>();

// This can also be written like this, but this is NOT efficient:
// var sToI = strings.CreateFilteredBridge( integers, ( string s, out int i ) => int.TryParse( s, out i ) );

// The static keyword helps you to ensure that there is no (costly) closure:
// var sToI = strings.CreateFilteredBridge( integers, static ( string s, out int i ) => int.TryParse( s, out i ) );

// But this is the simplest and most efficient form:
var sToI = strings.CreateFilteredBridge( integers, int.TryParse );

var intReceived = new List<int>();
integers.PerfectEvent.Sync += ( monitor, i ) => intReceived.Add( i );

await strings.RaiseAsync( TestHelper.Monitor, "not an int" );
intReceived.Should().BeEmpty( "integers didn't receive the not parseable string." );

// We now raise a valid int string.
await strings.RaiseAsync( TestHelper.Monitor, "3712" );
intReceived.Should().BeEquivalentTo( new[] { 3712 }, "string -> int done." );
```

## Simple relay of events
Relays are Bridges that don't adapt the event type. These are convenient very simple methods (the same exists
for `PerfectEventSender<TSource,TEvent>`):
```csharp
/// <summary>
/// Creates a relay (non transformer and non filtering bridge) between this sender and another one.
/// The relay is a bridge: it can be <see cref="IBridge.IsActive"/> or not and <see cref="IBridge.OnlyFromSource"/>
/// can be changed, and must be disposed once done with it.
/// </summary>
/// <param name="target">The target that must send the same events as this one.</param>
/// <param name="isActive">By default the new bridge is active.</param>
/// <returns>A new bridge.</returns>
public IBridge CreateRelay( PerfectEventSender<TEvent> target, bool isActive = true )
{
    return CreateBridge( target, Util.FuncIdentity, isActive );
}
```
Note the use of `CK.Core.Util.FuncIdentity` that reuses the same `static x => x` lambda.





