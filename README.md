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
The parallel handlers is not called with the origin monitor but with a `ActivityMonitor.DependentToken` that is a correlation
identifier (actually a string that identifies its creation monitor and instant):

```csharp
void ListenTo( IThing o )
{
    o.Talk.ParallelAsync += ThingTalkedAsync;
}

void StopListeningTo( IThing o )
{
    o.Talk.ParallelAsync -= ThingTalkedAsync;
}

async Task ThingTalkedAsync( ActivityMonitor.DependentToken token, IThing thing, string e )
{
    var monitor = new ActivityMonitor();
    using( TestHelper.Monitor.StartDependentActivity( token ) )
    {
      monitor.DependentActivity().Launch( token );
      //...
    }
    monitor.MonitorEnd();
}
```
A more realistic usage of Parallel handling would be to submit the event to an asynchronous worker (through a mailbox,
typically a System.Threading.Channel) and wait for its handling, the worker having its own monitor.
And if the handling of the event doesn't need to be awaited, then a Synchronous handler that synchronously pushes the
event into the worker's mailbox is the best solution.

## Implementing and raising a Perfect Event

A Perfect Event is implemented thanks to a [PerfectEventSender](CK.PerfectEvent/PerfectEventSender.cs). 

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









