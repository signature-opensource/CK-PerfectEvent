Perfect events are .Net events that support 3 kind of callbacks: synchronous, sequential asynchronous
and parallel asynchronous.

A sender raises once and every kind of handler is honoured. Parallel handlers receive the monitor
`IParallelLogger` rather than the monitor itself, which is what makes them safe to run concurrently.

An event can also be relayed: a bridge connects a sender to another sender, optionally converting or
filtering the event on the way, so a handler on the target knows nothing of where the event came from.
