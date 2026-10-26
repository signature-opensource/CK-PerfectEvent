Perfect events are .Net events that support 3 kinds of handlers: synchronous, sequential asynchronous
and parallel asynchronous. The event is exposed by `PerfectEvent<TEvent>` (or `PerfectEvent<TSender, TEvent>`) and
raised by its `PerfectEventSender`; senders can be bridged to relay, convert or filter events, and
`PerfectEventBuffer<TEvent>` collects events so that they can be awaited.
