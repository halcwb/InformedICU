namespace InformedICU.EventSourced

open System
open System.Diagnostics

type EventId = System.Guid

type StreamId = string

type EventVersion = int

/// Store te event metadata like the
/// aggregate id and the creation time
type EventMetadata =
    {
        EventId : EventId
        StreamId : StreamId
        RecordedAtUtc : System.DateTime
        EventVersion : EventVersion
    }

/// Wrapper for an event with meta data
type Event<'Event> =
    {
        Metadata : EventMetadata
        Event : 'Event
    }

/// Process a list of events in a 
/// asynchronous way.
type EventHandler<'Event> =
    Event<'Event> list -> Async<unit>


/// The event result is either a list of 
/// event envelopes if successfull or an
/// string with the error.
type EventResult<'Event> =
    Result<Event<'Event> list, string>


/// The event storage takes care of the persistence of
/// of event envelopes.
type EventStorage<'Event> =
    {
        Get : unit -> Async<EventResult<'Event>>
        GetStream : StreamId -> Async<EventResult<'Event>>
        AppendStream : StreamId -> Event<'Event> list -> Async<Result<unit, string>>
    }


/// The event store, stores the event envelopes
/// in streams identified by the `EventSource`.
/// It can have error listeners and/or event envelope
/// list listeners (for appended lists).
type EventStore<'Event> =
    {
        Get : unit -> Async<EventResult<'Event>>
        GetStream : StreamId -> Async<EventResult<'Event>>
        AppendStream : StreamId -> Event<'Event> list -> Async<Result<unit, string>>
        OnError : IEvent<exn>
        OnEvents : IEvent<Event<'Event> list>
    }

/// An event listener can act upon each appended list
/// of event envelopes to the event store.
type EventListener<'Event> =
    {
        Subscribe : EventHandler<'Event> -> unit
        Notify : Event<'Event> list -> unit
    }


/// A projection calculates the current state from
/// the update with an event with the previous state
type Projection<'State,'Event> =
        'State -> 'Event list -> 'State

type QueryResult =
    | Handled of obj
    | NotHandled
    | QueryError of string


type QueryHandler<'Query> =
    'Query -> Async<QueryResult>


type ReadModel<'Event, 'State> =
    {
    EventHandler : EventHandler<'Event>
    State : unit -> Async<'State>
    }


type CommandHandler<'Command> =
    {
    Handle : StreamId -> 'Command -> Async<Result<unit,string>>
    OnError : IEvent<exn>
    }


type Workflow<'Command,'Event> =
    'Command -> 'Event list -> 'Event list


type DB_Connection_String = DB_Connection_String of string


