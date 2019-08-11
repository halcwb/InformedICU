/// Scripting the entire ICU app
///
#I __SOURCE_DIRECTORY__
#load ".paket/load/netcoreapp2.2/Npgsql.fsx"
#load ".paket/load/netcoreapp2.2/main.group.fsx"

#load "src/Extensions/List.fs"
#load "src/Extensions/Option.fs"
#load "src/Extensions/Result.fs"

#time

module Memoization =

    /// Memoize a function `f` according
    /// to its parameter
    let memoize f =
        let cache = ref Map.empty
        fun x ->
            match (!cache).TryFind(x) with
            | Some r -> r
            | None ->
                let r = f x
                cache := (!cache).Add(x, r)
                r

            
module Infrastructure =

    open System
    open System.Diagnostics
    
    open InformedICU.Extensions

    type Logger = string -> Stopwatch -> obj Option -> unit

    module Logger =

        let noLog : Logger = fun _ _ _ -> ()
        
        let logPerf header : Logger =
            fun s t _ -> 
                printfn "%s" header
                printfn "%s: %f" s (t.Elapsed.TotalSeconds)

        let logPrint header : Logger =
            fun s t o -> 
                printfn "header"
                printfn "%s: %f" s (t.Elapsed.TotalSeconds)
                printfn "%A" o

    /// A wrapper for MailboxProcessor that catches all unhandled exceptions
    /// and reports them via the 'OnError' event. Otherwise, the API
    /// is the same as the API of 'MailboxProcessor'
    type Agent<'T>(f: Agent<'T> -> Async<unit>, logger : Logger) as self =

        let timer = Stopwatch.StartNew ()

        let logAsync s x =
            timer.Restart ()
            async { 
                let! v = x
                do 
                    v
                    |> box
                    |> Some
                    |> logger s timer 
                return v }
            
        
        // Create an event for reporting errors
        let errorEvent = Event<_>()

        // Start standard MailboxProcessor
        let inbox = new MailboxProcessor<'T>(fun _ ->
            async {
                // Run the user-provided function & handle exceptions
                try return! f self
                with e -> errorEvent.Trigger(e)
            })

        /// Triggered when an unhandled exception occurs
        member __.OnError = errorEvent.Publish

        member __.Trigger exn = errorEvent.Trigger exn

        /// Starts the mailbox processor
        member __.Start() =
            timer.Restart ()
            logger "start"  timer None
            inbox.Start()

        /// Receive a message from the mailbox processor
        member __.Receive() = 
                inbox.Receive()
                |> logAsync "receive"

        /// Post a message to the mailbox processor
        member __.Post(value:'T) = 
            timer.Restart ()

            box value
            |> Some
            |> logger "post" timer

            inbox.Post value
     
        member __.PostAndReply(f: AsyncReplyChannel<'a> -> 'T) = 
            inbox.PostAndReply f

        member __.PostAndAsyncReply(f: AsyncReplyChannel<'a> -> 'T) = 
            inbox.PostAndAsyncReply f
            |> logAsync "reply"


        module Agent =

            /// Start the mailbox processor
            let start f =
                let agent = new Agent<_>(f, fun _ _ _ -> ())
                agent.Start()
                agent

            /// Start the mailbox processor
            let startWithLogging log f =
                let agent = new Agent<_>(f, log)
                agent.Start()
                agent


    type EventId = System.Guid

    type StreamId = string

    /// Store te event metadata like the
    /// aggregate id and the creation time
    type EventMetadata =
        {
            EventId : EventId
            StreamId : StreamId
            RecordedAtUtc : System.DateTime
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


    /// The event store, stores the event envelopes
    /// in streams identified by the `EventSource`.
    /// It can have error listeners and/or event envelope
    /// list listeners (for appended lists).
    type EventStore<'Event> =
        {
            Get : unit -> Async<EventResult<'Event>>
            GetStream : StreamId -> Async<EventResult<'Event>>
            Append : Event<'Event> list -> Async<Result<unit, string>>
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


    /// The event storage takes care of the persistence of
    /// of event envelopes.
    type EventStorage<'Event> =
        {
            Get : unit -> Async<EventResult<'Event>>
            GetStream : StreamId -> Async<EventResult<'Event>>
            Append : Event<'Event> list -> Async<unit>
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

    type Behaviour<'Command,'Event> =
      'Command -> 'Event list

    type DB_Connection_String = DB_Connection_String of string
    

    module Helper =

        open System

        let waitForAnyKey () =
            Console.ReadKey() |> ignore

        let inline printError message details =
            Console.ForegroundColor <- ConsoleColor.Red
            printfn "\n%s" message
            Console.ResetColor()
            printfn "%A" details

        let printUl list =
            list
            |> List.iteri (fun i item -> printfn " %i: %A" (i+1) item)

        let printEvents header events =
            match events with
            | Ok events ->
                events
                |> List.length
                |> printfn "\nHistory for %s (Length: %i)" header

                events |> printUl

            | Error error ->
                let s = (sprintf "Error when retrieving events: %s" error)
                printError s ""

        let runAsync asnc =
            asnc |> Async.RunSynchronously

        let printQueryResults header result =
            result
            |> runAsync
            |> function
            | QueryResult.Handled result -> 
                printfn "\n%s: %A" header result

            | QueryResult.NotHandled ->
                printfn "\n%s: NOT HANDLED" header

            | QueryResult.QueryError error ->
                printError (sprintf "Query Error: %s" error) ""

        let printCommandResults header result =
            match result with
            | Ok _ ->
              printfn "\n%s: %A" header result

            | Error error ->
              printError (sprintf "Command Error: %s" error) ""

           
    module Event =

        let enveloped streamId events =
            let now = System.DateTime.UtcNow
            let eventId = System.Guid.NewGuid ()
            let envelope event =
                {
                    Metadata = {
                        EventId = eventId
                        StreamId = streamId
                        RecordedAtUtc = now
                        }
                    Event = event
                }

            events |> List.map envelope

        let asEvents events =
            events |> List.map (fun e -> e.Event)

        let withStreamId streamId (e: Event<_>) = 
            e.Metadata.StreamId = streamId

        let withMetaData md e =
            {
                Metadata = {
                    EventId = md.EventId
                    StreamId = md.StreamId
                    RecordedAtUtc = md.RecordedAtUtc
                    }
                Event = e
            }
            

    module Projection =

        type private Cache<'T> () =
            member val Count = 0 with get, set
            member val State : 'T Option  = None with get, set

        /// Cache a projection `p` according
        /// to the number of events
        let cache init f =
            let cache = new Cache<_>()
            fun xs ->
                let count = xs |> List.length
                let state =
                    match cache.State with
                    | Some s -> 
                        // printfn "using cache with count: %i" count
                        (cache.Count, s)
                        // (0, init)
                    | None -> (0, init)
                    |> (fun (c, s) ->
                        xs
                        |> List.skip (if c > count then 0 else c)
                        |> List.fold f s
                    )
                cache.Count <- count
                cache.State <- state |> Some
                state

        let cacheFold init f =
            let cache = new Cache<_>()
            fun xs ->
                let count = xs |> List.length
                let state =
                    match cache.State with
                    | Some s -> 
                        // (0, init) 
                        (cache.Count, s)
                    | None -> (0, init)
                    |> (fun (c, s) ->
                        if c > count then f s xs
                        else
                            xs
                            |> List.skip c
                            |> f s
                    )
                cache.Count <- count
                cache.State <- state |> Some
                state


        module Tests =

            type Event = Count of int

            let projection =
                fun s (Count n) ->
                    System.Threading.Thread.Sleep 10
                    s + n
                

            let run () =
                let cached = cache 0 projection
                
                printfn "Running original version"
                1
                |> List.replicate 100
                |> List.map Count
                |> List.fold projection 0
                |> printfn "Count %i"

                printfn "Running original version extra"
                1
                |> List.replicate 200
                |> List.map Count
                |> List.fold projection 0
                |> printfn "Count %i"

                printfn "Running cached version"
                1
                |> List.replicate 100
                |> List.map Count
                |> cached
                |> printfn "Count %i"

                printfn "Running cached version twice"
                1
                |> List.replicate 100
                |> List.map Count
                |> cached
                |> printfn "Count %i"

                printfn "Running cached version extra"
                1
                |> List.replicate 200
                |> List.map Count
                |> cached
                |> printfn "Count %i"

                printfn "Running cached version with empty list"
                []
                |> List.map Count
                |> cached
                |> printfn "Count %i"


    module EventStorage =

        module FileStorage =
        
            open System.IO
            open Thoth.Json.Net
            open InformedICU.Extensions

            let private get store =
                store
                |> File.ReadLines
                |> List.ofSeq
                |> List.traverseResult Decode.Auto.fromString<Event<'Event>>

            let private getStream store streamId =
                store
                |> File.ReadLines
                |> List.ofSeq
                |> List.traverseResult Decode.Auto.fromString<Event<'Event>>
                |> Result.map (List.filter (Event.withStreamId streamId))

            let private append store events =
                use streamWriter = new StreamWriter(store, true)
                events
                |> List.map (fun event -> Encode.Auto.toString(0,event))
                |> List.iter streamWriter.WriteLine

                do streamWriter.Flush()

            let initialize store : EventStorage<_> =
                {
                    Get = fun () -> async { return get store }
                    GetStream = fun streamId -> 
                        async { return getStream store streamId  }
                    Append = fun events -> 
                        async { return append store events }
                }


        module InMemoryStorage =

            type private Cache<'Event> =
                {
                    Count : int
                    Stream : Event<'Event> list
                }

            type Msg<'Event> =
                private
                | Get of AsyncReplyChannel<EventResult<'Event>>
                | GetStream of StreamId * AsyncReplyChannel<EventResult<'Event>>
                | Append of Event<'Event> list * AsyncReplyChannel<unit>

            let private streamFor<'Event> () =
                let cache : Map<StreamId, Cache<_>> ref = ref Map.empty
                fun (streamId : StreamId) (history : Event<'Event> list ) ->
                    match (!cache).TryFind(streamId) with
                    | Some { Count = c; Stream = stream } -> 
                        (c, stream)
                        // (0, [])
                    | None -> 
                        (0, [])
                    |> fun (c, stream) ->
                        let newStream =
                            history 
                            |> List.skip c
                            |> List.filter (Event.withStreamId streamId)
                            |> List.append stream
                        let newCache = 
                            {
                                Count = history |> List.length
                                Stream = newStream
                            }
                        cache := (!cache).Add(streamId, newCache)
                        newStream
                        

            let private _initialize log history : EventStorage<'Event> =

                let proc (inbox : Agent<Msg<_>>) =
                    let streamFor = streamFor ()

                    let rec loop history =
                        async {
                            let! msg = inbox.Receive()

                            match msg with
                            | Get reply ->
                                history
                                |> Ok
                                |> reply.Reply

                                return! loop history
    
                            | GetStream (streamId, reply) ->
                                history
                                |> streamFor streamId
                                |> Ok
                                |> reply.Reply

                                return! loop history

                            | Append (events, reply) ->
                                reply.Reply ()
                                return! loop (history @ events)
                        }

                    loop history

                let agent = 
                    Agent.startWithLogging log proc

                {
                    Get = fun () ->  agent.PostAndAsyncReply Get
                    GetStream = fun stream -> 
                        agent.PostAndAsyncReply (fun reply -> 
                            (stream, reply) |> GetStream
                        )
                    Append = fun events -> 
                        agent.PostAndAsyncReply (fun reply -> 
                            (events, reply) |> Append
                        )
                }

            let initialize history = _initialize Logger.noLog history

            let initializeWithLogging log history = _initialize log history

            module Tests =

                open System.Diagnostics
                
                let streamId = "helloWorld"
    
                type Event = | HelloWorld

                let storage : EventStorage<Event> = 
                    HelloWorld
                    |> List.replicate 10000
                    |> Event.enveloped streamId
                    |> initializeWithLogging (Logger.logPerf "inmemory storage")

                let run n =
                    for _ in [1..n] do
                        HelloWorld 
                        |> List.singleton
                        |> Event.enveloped streamId
                        |> storage.Append
                        |> Async.RunSynchronously
                        |> ignore


        module PostgresStorage =

            open Npgsql.FSharp
            open Thoth.Json.Net
            open Helper
            open InformedICU.Extensions
            open InformedICU.Extensions.Option.Builder

            let select = "SELECT metadata, payload FROM event_store"
            let order = "ORDER BY recorded_at_utc ASC, event_index ASC"

            let private hydrateEvents reader =
                let row = Sql.readRow reader
                option {
                    let! metadata = Sql.readString "metadata" row
                    let! payload = Sql.readString "payload" row

                    let event =
                        metadata
                        |> Decode.Auto.fromString<EventMetadata>
                        |> Result.bind (fun metadata ->
                            payload
                            |> Decode.Auto.fromString<'Event>
                            |> Result.map (fun event -> 
                                { Metadata = metadata ; Event = event}
                            )
                        )

                    return event
                }

            let private get (DB_Connection_String db_connection) =
                async {
                    return
                        db_connection
                        |> Sql.connect
                        |> Sql.query (sprintf "%s %s" select order)
                        |> Sql.executeReader hydrateEvents
                        |> List.traverseResult id
                }

            let private getStream (DB_Connection_String db_connection) streamId =
                async {
                    return
                        db_connection
                        |> Sql.connect
                        |> Sql.query (sprintf "%s WHERE streamId = @streamId %s" select order)
                        |> Sql.parameters [ "@streamId", SqlValue.String streamId ]
                        |> Sql.executeReader hydrateEvents
                        |> List.traverseResult id
                }

            let private append (DB_Connection_String db_connection) eventEnvelopes =
                let query = """
                  INSERT INTO event_store (streamId, recorded_at_utc, event_index, metadata, payload)
                  VALUES (@streamId, @recorded_at_utc, @event_index, @metadata, @payload)"""

                let parameters =
                    eventEnvelopes
                    |> List.mapi (fun index eventEnvelope ->
                        [
                            "@streamId", SqlValue.String eventEnvelope.Metadata.StreamId
                            "@recorded_at_utc", SqlValue.Date eventEnvelope.Metadata.RecordedAtUtc
                            "@event_index", SqlValue.Int index
                            "@metadata", SqlValue.Jsonb <| Encode.Auto.toString(0,eventEnvelope.Metadata)
                            "@payload", SqlValue.Jsonb <| Encode.Auto.toString(0,eventEnvelope.Event)
                        ])

                db_connection
                |> Sql.connect
                |> Sql.executeTransactionAsync [ query, parameters ]
                |> Async.Ignore


            let initialize db_connection : EventStorage<_> =
                {
                    Get = fun () -> get db_connection
                    GetStream = fun streamId -> getStream db_connection streamId
                    Append = fun events -> append db_connection events
                }


    module EventStore =

        type Msg<'Event> =
            | Get of AsyncReplyChannel<EventResult<'Event>>
            | GetStream of StreamId * AsyncReplyChannel<EventResult<'Event>>
            | Append of Event<'Event> list * AsyncReplyChannel<Result<unit,string>>

        let _initialize logger (storage : EventStorage<_>) : EventStore<_> =
            let eventsAppended = Event<Event<_> list>()

            let proc (inbox : Agent<Msg<_>>) =
                let rec loop () =
                    async {
                        match! inbox.Receive() with
                        | Get reply ->
                            try
                                let! events = storage.Get()
                                do events |> reply.Reply
                            with exn ->
                                do inbox.Trigger(exn)
                                do exn.Message |> Error |> reply.Reply

                            return! loop ()


                        | GetStream (stream, reply) ->
                            try
                                let! stream = stream |> storage.GetStream
                                do stream |> reply.Reply
                            with exn ->
                                do inbox.Trigger(exn)
                                do exn.Message |> Error |> reply.Reply

                            return! loop ()

                        | Append (events, reply) ->
                            try
                                do! events |> storage.Append
                                do eventsAppended.Trigger events
                                do reply.Reply (Ok ())
                            with exn ->
                                do inbox.Trigger(exn)
                                do exn.Message |> Error |> reply.Reply

                            return! loop ()
                    }

                loop ()

            let agent =  Agent.startWithLogging logger proc

            {
                Get = fun () -> agent.PostAndAsyncReply Get
                GetStream = fun stream -> 
                    agent.PostAndAsyncReply (fun reply -> GetStream (stream, reply))
                Append = fun events -> 
                    agent.PostAndAsyncReply (fun reply -> Append (events, reply))
                OnError = agent.OnError
                OnEvents = eventsAppended.Publish
            }

        let initialize storage = _initialize Logger.noLog storage

        let initializeWithLogger logger storage = _initialize logger storage

        module Tests =

            type Event = | HelloWorld

            // create an event store for the hello world event
            let store : EventStore<Event> = 
                EventStorage.InMemoryStorage.initialize []
                |> initializeWithLogger (Logger.logPerf "eventstore")

            // will print out received event envelopes
            // store.OnEvents.Add (printfn "Received: %A")
            
            // run the tests
            let run n =
                let streamId = "helloworld1"
                
                HelloWorld
                |> List.replicate n
                |> Event.enveloped streamId
                |> store.Append
                |> Async.RunSynchronously
                |> (fun _ -> printfn "ready stream 1")

                let streamId = "helloworld2"
                HelloWorld
                |> List.replicate n
                |> Event.enveloped streamId
                |> store.Append
                |> Async.RunSynchronously
                |> (fun _ -> printfn "ready stream 2")
                
                // get the appended events
                store.GetStream streamId
                |> Async.RunSynchronously
                |> (fun _ -> printfn "ready get stream 1")

                // get all appended events
                store.Get ()
                |> Async.RunSynchronously
                |> (fun _ -> printfn "ready get stream 2")


    module EventListener =

        type Msg<'Event> =
            | Notify of Event<'Event> list
            | Subscribe of EventHandler<'Event>


        let notifyEventHandlers events (handlers : EventHandler<_> list) =
            handlers
            |> List.map (fun subscription -> events |> subscription )
            |> Async.Parallel
            |> Async.Ignore

        let _initialize logger : EventListener<_> =

            let proc (inbox : Agent<Msg<_>>) =
                let rec loop (eventHandlers : EventHandler<'Event> list) =
                    async {
                        match! inbox.Receive() with
                        | Notify events ->
                            do! eventHandlers |> notifyEventHandlers events

                            return! loop eventHandlers

                        | Subscribe listener ->
                            return! loop (listener :: eventHandlers)
                    }

                loop []

            let agent = Agent.startWithLogging logger proc

            {
                Notify = Notify >> agent.Post
                Subscribe = Subscribe >> agent.Post
            }


        let initialize () = _initialize Logger.noLog

        let initializeWithLogger logger = _initialize logger

        module Tests =

            type Event = HelloWorld

            let listener : EventListener<Event> = 
                initializeWithLogger (Logger.logPrint "event listener")

            let handler : EventHandler<Event> = fun ees ->
                async { return printfn "Handled: %A" ees }

            let events =
                [ HelloWorld ] |> Event.enveloped "helloworld"

            let run () =
                // no handlers, nothing happens
                printfn "notifying without handler"
                events 
                |> listener.Notify
                // subscribe handler
                handler
                |> listener.Subscribe
                // now the handlers is notified
                printfn "notifying without handler"
                events 
                |> listener.Notify


    module QueryHandler =

        let rec private choice (queryHandler : QueryHandler<_> list) query =
            async {
                match queryHandler with
                | handler :: rest ->
                    match! handler query with
                    | NotHandled ->
                        return! choice rest query

                    | Handled response ->
                        return Handled response

                    | QueryError response ->
                        return QueryError response

                | _ -> return NotHandled
            }

        let initialize queryHandlers : QueryHandler<_> =
            choice queryHandlers
            
        module Tests =

            let handler : QueryHandler<string> =
                [
                    fun _ -> async { printf "not handled "; return NotHandled }
                    fun _ -> async { printf "not handled "; return NotHandled }
                    fun _ -> async { printf "not handled "; return NotHandled }
                    fun x -> async { return x |> box |> Handled }
                ]
                |> initialize

            let run () =
                handler "Finally handled"


    module CommandHandler =
                        
        type Msg<'Event, 'Command> =
            | Handle of StreamId * 'Command * AsyncReplyChannel<Result<unit,string>>

        let private _initialize logger
                                (behaviour : Behaviour<_,_>) 
                                (eventStore : EventStore<_>) : CommandHandler<_> =
            
            let proc (inbox : Agent<Msg<_, _>>) =
                let rec loop () =
                    async {
                        let! msg = inbox.Receive()

                        match msg with
                        | Handle (streamId, command, reply) ->

                            let events =
                                command
                                |> behaviour 
                                |> Event.enveloped streamId

                            let! result = eventStore.Append events

                            do reply.Reply result

                            return! loop ()
                    }

                loop ()

            let agent = Agent.startWithLogging logger proc

            {
                Handle = fun streamId command -> 
                    agent.PostAndAsyncReply (fun reply -> 
                        Handle (streamId, command, reply)
                    )
                OnError = agent.OnError
            }

        let initialize behaviour eventstore = 
            _initialize Logger.noLog behaviour eventstore

        let initializeWithLogging logger behaviour eventstore = 
            _initialize logger behaviour eventstore
        
        module Tests =

            type Command = HelloWordCommand of string

            type Event = HelloWordEvent of string

            // create an event store for the hello world event
            let store n : EventStore<Event> = 
                HelloWordEvent "Init"
                |> List.replicate n
                |> Event.enveloped "helloworld"
                |> EventStorage.InMemoryStorage.initializeWithLogging 
                    (Logger.logPerf "in memory storage")
                |> EventStore.initializeWithLogger (Logger.logPerf "eventstore")
                    

            let behaviour : Behaviour<Command, Event> =
                fun cmd ->
                    match cmd with
                    | HelloWordCommand s -> 
                        HelloWordEvent s
                        |> List.replicate 2

            let handler store =
                initializeWithLogging (Logger.logPerf "event handler") behaviour store

            let run n1 n2 =
                let streamId = "helloworld"

                let store = store n1
                let handler = handler store
                
                for i in [1..n2] do
                    "Hello World"
                    |> HelloWordCommand
                    |> handler.Handle streamId
                    |> Async.RunSynchronously
                    |> ignore

                store.Get ()
                |> Async.RunSynchronously
                |> function
                | Ok evs -> 
                    evs 
                    |> List.length
                | Error _ -> 0


    module ReadModel =

        type Msg<'Event,'Result> =
            | Notify of Event<'Event> list * AsyncReplyChannel<unit>
            | State of AsyncReplyChannel<'Result>

        let _inMemory logger
                     (updateState : 'State -> Event<'Event> list -> 'State) 
                     (initState : 'State) : ReadModel<'Event, 'State> =
            let agent =
                let eventSubscriber (inbox : Agent<Msg<_,_>>) =
                    let rec loop state =
                        async {
                        let! msg = inbox.Receive()

                        match msg with
                        | Notify (events, reply) ->
                            let state = events |> updateState state
                            reply.Reply ()
                            return! loop state

                        | State reply ->
                            reply.Reply state
                            return! loop state
                        }

                    loop initState

                Agent.startWithLogging logger eventSubscriber

            {
                EventHandler = fun eventEnvelopes -> 
                    agent.PostAndAsyncReply(fun reply -> 
                        Notify (eventEnvelopes, reply)
                    )
                State = fun () -> agent.PostAndAsyncReply State
            }

        let inMemory update init = _inMemory Logger.noLog update init
        
        let inMemoryWithLogger update init logger = _inMemory logger update init

        module Tests =

        
            let run () = ()


    type EventSourcedConfig<'Comand,'Event,'Query> =
        {
            EventStorageInit : unit -> EventStorage<'Event>
            CommandHandlerInit : EventStore<'Event> -> CommandHandler<'Comand>
            QueryHandler : QueryHandler<'Query>
            EventHandlers : EventHandler<'Event> list
        }


    type EventSourced<'Comand,'Event,'Query> (configuration : EventSourcedConfig<'Comand,'Event,'Query>) =

        let eventStorage = configuration.EventStorageInit()

        let eventStore = EventStore.initialize eventStorage

        let commandHandler = configuration.CommandHandlerInit eventStore

        let queryHandler = configuration.QueryHandler

        let eventListener = EventListener.initialize ()

        do
            eventStore.OnError.Add(fun exn -> 
                Helper.printError (sprintf "EventStore Error: %s" exn.Message) exn)
            commandHandler.OnError.Add(fun exn -> 
                Helper.printError (sprintf "CommandHandler Error: %s" exn.Message) exn)
            eventStore.OnEvents.Add eventListener.Notify
            configuration.EventHandlers |> List.iter eventListener.Subscribe

        member __.HandleCommand eventSource command =
            commandHandler.Handle eventSource command

        member __.HandleQuery query =
            queryHandler query

        member __.GetAllEvents () =
            eventStore.Get()

        member __.GetStream stream =
            eventStore.GetStream stream


    module EventSourced =

        let private apply f (x: EventSourced<_, _, _>) = x |> f

        let private get x = apply id x

        let fromConfig config = new EventSourced<_, _, _>(config)

        let handleCommand evs = (evs |> get).HandleCommand

        let handleQuery evs = (evs |> get).HandleQuery

        let getAllEvents evs = (evs |> get).GetAllEvents

        let getStream evs = (evs |> get).GetStream


module Domain =

    open System

    
    type HospitalNumber = HospitalNumber of string


    module HospitalNumber =

        type Event = Invalid of string

        let create s = 
            if s = "" then 
                "Hospital number cannot be an empty string"
                |> Invalid
                |> List.singleton
                |> Result.Error
            else 
                s 
                |> HospitalNumber
                |> Result.Ok


        let toString (HospitalNumber s) = s

        let eqsString s (HospitalNumber n) = s = n

    type Name = Name of string



    module Name =

        open InformedICU.Extensions

        type Event = Invalid of string

        let create msg s =
            if s = "" then 
                msg
                |> Invalid         
                |> List.singleton
                |> Result.Error
            else 
                s 
                |> Name
                |> Result.Ok

        let toString (Name s) = s


    type BirthDate = BirthDate of DateTime


    module BirthDate =

        type Event = Invalid of string

        let currentAgeInDays (BirthDate bd) = 
            (DateTime.Now - bd).TotalDays
        
        let create dt =
            match dt with
            | Some dt ->
                let bd = dt |> BirthDate
                let age = bd |> currentAgeInDays
                match age with
                | _ when age < 0. ->
                    sprintf "A birthdate of %A results in a negative age" dt
                    |> Invalid
                    |> List.singleton
                    |> Result.Error 
                | _ when age > (120. * 365.) ->
                    sprintf "A birthdate of %A results in an age > 120 years" dt
                    |> Invalid
                    |> List.singleton
                    |> Result.Error 
                | _ -> bd |> Result.Ok
            | None -> 
                "Birthdate cannot be empty"
                |> Invalid
                |> List.singleton
                |> Result.Error

        let toDate (BirthDate dt) = dt


    [<CustomEquality; NoComparison>]
    type Patient =
        {
            HospitalNumber : HospitalNumber
            LastName : Name
            FirstName : Name
            BirthDate : BirthDate
        }
    with 
        override __.Equals(obj) =
            match obj with
            | :? Patient as p -> __.HospitalNumber = p.HospitalNumber
            | _ -> false
        override __.GetHashCode () = 
            __.HospitalNumber
            |> box
            |> hash


    module Patient =

        open InformedICU.Extensions
        open InformedICU.Extensions.Result.Operators

        type Dto () = 
            member val HospitalNumber  = "" with get, set
            member val LastName = "" with get, set
            member val FirstName = "" with get, set
            member val BirthDate : DateTime Option = None with get, set


        type Event =
            | Registered of Dto
            | AllReadyRegistered of string
            | Admitted of string
            | AllReadyAdmitted of string
            | Discharged of string
            | AllReadyDischarged of string
            | UnknownHospitalNumber of string
            | HospitalNumberEvent of HospitalNumber.Event
            | NameEvent of Name.Event
            | BirthDateEvent of BirthDate.Event
            | InvalidPatient of Event list


        let create hn ln fn bd =
            let f hn ln fn bd =
                {
                    HospitalNumber = hn
                    LastName = ln
                    FirstName = fn
                    BirthDate = bd
                }

            f
            <!> (Result.mapErrorList HospitalNumberEvent (HospitalNumber.create hn))
            <*> (Result.mapErrorList NameEvent (Name.create "Last name cannot be empty" ln))
            <*> (Result.mapErrorList NameEvent (Name.create "First name cannot be empty" fn))
            <*> (Result.mapErrorList BirthDateEvent (BirthDate.create bd))

        module Dto = 

            let dto () = new Dto () 

            let toDto (pat : Patient) =
                let dto = dto ()
                dto.HospitalNumber <- pat.HospitalNumber |> HospitalNumber.toString
                dto.FirstName <- pat.FirstName |> Name.toString
                dto.LastName <- pat.LastName |> Name.toString
                dto.BirthDate <- pat.BirthDate |> BirthDate.toDate |> Some
                dto

            let fromDto (dto : Dto) =
                create dto.HospitalNumber
                        dto.LastName
                        dto.FirstName
                        dto.BirthDate

            let toString (dto: Dto) =
                let ds = 
                    dto.BirthDate 
                    |> Option.bind (fun bd ->
                        bd.ToString("dd-mm-yyyy")
                        |> Some
                    )
                    |> Option.defaultValue ""
                sprintf "%s: %s, %s %s"
                        dto.HospitalNumber
                        dto.LastName
                        dto.FirstName
                        ds


        module Projections =

            open Infrastructure

            let updateRegisteredPatients dtos event =
                match event with
                | Registered dto ->
                    if dtos |> List.exists ((=) dto) then dtos
                    else
                        dto
                        |> List.singleton
                        |> List.append dtos
                | _ -> dtos
            
            let registeredPatients = Projection.cache [] updateRegisteredPatients

            let updateAdmittedPatients registered pats event =
                let find s = 
                    registered 
                    |> List.tryFind (fun (dto : Dto) -> 
                        dto.HospitalNumber = s
                    )

                match event with
                | Admitted s ->
                    match s |> find with
                    | Some pat -> pats |> List.append [ pat ]
                    | None -> pats
                | Discharged s ->
                    match s |> find with
                    | Some pat -> 
                        pats 
                        |> List.remove ((=) pat)
                    | None -> pats
                | _ -> pats

            let admittedPatients =
                fun pats evs ->
                    let registered = 
                        evs 
                        |> registeredPatients

                    let find s = 
                        registered 
                        |> List.tryFind (fun dto -> dto.HospitalNumber = s)

                    evs
                    |> List.fold (updateAdmittedPatients registered) pats

                |> Projection.cacheFold []

            module Tests =

                open Infrastructure
                open InformedICU.Extensions

                let store : EventStore<Event> =
                    EventStorage.InMemoryStorage.initialize []
                    |> EventStore.initialize

                let run () =
                    let streamId = "patients"

                    // add some registered patient events to the store
                    [
                        create "1" "LastName" "FirstName" (DateTime.Now |> Some)
                        create "2" "LastName" "FirstName" (DateTime.Now |> Some)
                        create "3" "LastName" "FirstName" (DateTime.Now |> Some)
                    ]
                    |> List.map (Result.map (Dto.toDto >> Registered))
                    |> List.append [ "1" 
                                     |> HospitalNumber.create 
                                     |> Result.get
                                     |> HospitalNumber.toString
                                     |> Admitted 
                                     |> Result.Ok ]
                    |> List.filter Result.isOk
                    |> List.map Result.get
                    |> Event.enveloped "patients"
                    |> store.Append
                    |> ignore

                    // get the registered patients
                    store.GetStream streamId
                    |> Async.RunSynchronously
                    |> Result.map (Event.asEvents >> registeredPatients)
                    |> Result.map (fun pats -> pats |> List.iter (printfn "%A"))
                    |> ignore

                    // get admitted patients
                    store.GetStream streamId
                    |> Async.RunSynchronously
                    |> Result.map (Event.asEvents >> admittedPatients)
                    |> Result.map (fun pats -> pats |> List.iter (printfn "%A"))
                    |> ignore


        module Behaviour =

            open Infrastructure

            type Command =
                | Register of bool * Dto
                | Admit of bool * bool * string
                | Discharge of string


            let registerPatient isRegistered (dto : Dto) = 

                if isRegistered then 
                    AllReadyRegistered dto.HospitalNumber
                else 
                    dto |> Registered
                |> List.singleton

            let admitPatient isRegistered isAdmitted s = 
                match s with
                | _ when isRegistered |> not -> 
                    s 
                    |> UnknownHospitalNumber
                    |> List.singleton
                | _ when isAdmitted ->
                    s 
                    |> AllReadyAdmitted
                    |> List.singleton
                | _ ->
                    s 
                    |> Admitted
                    |> List.singleton

            let behaviour : Behaviour<Command, Event> =
                fun cmd -> 
                    match cmd with
                    | Register (isRegistered, dto) -> 
                        match dto |> Dto.fromDto with
                        | Ok pat -> 
                            pat
                            |> Dto.toDto
                            |> registerPatient isRegistered
                        | Error e -> [ e |> InvalidPatient ]
                    | Admit (isRegistered, isAdmitted, s) ->
                        match s |> HospitalNumber.create with
                        | Result.Ok hn ->
                            hn 
                            |> HospitalNumber.toString
                            |> admitPatient isRegistered isAdmitted
                        | Result.Error e -> 
                            e 
                            |> List.map HospitalNumberEvent
                    | _ -> "Not implemented yet" |> exn |> raise


            module Tests =

                open Infrastructure
                open InformedICU.Extensions

                type Command = TestRegisterPatient
    
                // create an event store for the hello world event
                let store : EventStore<Event> = 
                    EventStorage.InMemoryStorage.initialize []
                    |> EventStore.initialize

                let behaviour : Behaviour<Command, Event> =
                    fun cmd ->
                        match cmd with
                        | TestRegisterPatient -> 
                            let pat = 
                                new DateTime(1965, 12, 7)
                                |> Some
                                |> create "1" "Test" "Test"
                                |> Result.get
                            pat
                            |> Dto.toDto
                            |> registerPatient true

                let handler =
                    CommandHandler.initialize behaviour store

                let listener = EventListener.initialize ()

                store.OnEvents.Add listener.Notify

                let run () =
                    let streamId = "patients"

                    TestRegisterPatient
                    |> handler.Handle streamId
                    |> Async.RunSynchronously
                    |> printfn "%A"

                    store.Get ()
                    |> Async.RunSynchronously
                    |> Helper.printEvents "Events"

                    TestRegisterPatient
                    |> handler.Handle streamId
                    |> Async.RunSynchronously
                    |> printfn "%A"

                    store.Get ()
                    |> Async.RunSynchronously
                    |> Helper.printEvents "Events"


        module ReadModels =

            open Infrastructure

            type RegisteredPatient =
                {
                    HospitalNumber : string
                    LastName : string
                    FirstName : string
                    Admitted : bool
                    Discharged : bool
                }

            type Query = 
                | GetRegistered
                | OnlyAdmitted
                | OnlyDischarged



            let registered () : ReadModel<_, _> =
                let updateState (state : RegisteredPatient list) evs =
                    evs
                    |> Event.asEvents
                    |> List.fold (fun (state : RegisteredPatient list) e ->
                        match e with 
                        | Registered dto ->
                            let exists =
                                state
                                |> List.exists (fun p ->
                                    p.HospitalNumber = dto.HospitalNumber
                                )
                            if exists then state
                            else
                                {
                                    HospitalNumber = 
                                        dto.HospitalNumber 
                                    LastName = 
                                        dto.LastName 
                                    FirstName =
                                        dto.FirstName
                                    Admitted = false
                                    Discharged = false
                                }
                                |> List.singleton
                                |> List.append state
                        | Admitted s ->
                            state
                            |> List.map (fun p ->
                                if p.HospitalNumber = s |> not then p
                                else
                                    { p with Admitted = true
                                             Discharged = false }
                            )
                        | Discharged s ->
                            state
                            |> List.map (fun p ->
                                if p.HospitalNumber = s |> not then p
                                else
                                    { p with Admitted = false
                                             Discharged = true }
                            )
                        | _ -> state
                    

                    ) state

                ReadModel.inMemory updateState []


            let query registered query =
                match query with
                | GetRegistered -> 
                    async {
                        let! state = registered ()
        
                        return 
                            state 
                            |> box
                            |> Handled
                    }
                | OnlyAdmitted -> 
                    async {
                        let! state = registered ()

                        return 
                            state 
                            |> List.filter (fun p -> p.Admitted)
                            |> box
                            |> Handled
                    }
                | OnlyDischarged -> 
                    async {
                        let! state = registered ()

                        return 
                            state 
                            |> List.filter (fun p -> p.Discharged)
                            |> box
                            |> Handled
                    }


module App =


    open Infrastructure 
    open Domain

    [<Literal>]
    let streamId = "Patients-ca75bfa2-e781-463f-850c-d63b84217370"


    type Event = 
        | PatientEvent of Patient.Event
        | SomeOtherEvent


    type Command = 
        | PatientCommand of Patient.Behaviour.Command
        | SomeOtherCommand


    type Query = 
        | PatientQuery of Patient.ReadModels.Query
        | SomeOtherQuery


    let behaviour cmd =
        match cmd with
        | PatientCommand c ->
            Patient.Behaviour.behaviour c
            |> List.map PatientEvent
        | _ -> []


    let mapPatientModel model =
        {
            EventHandler = fun evs -> 
                evs
                |> List.fold (fun acc e -> 
                    match e.Event with
                    | PatientEvent pe -> 
                        [ pe |> Event.withMetaData e.Metadata ] |> List.append acc
                    | _ -> acc
                ) []
                |> model.EventHandler
            State = model.State
        }

    let patientQuery state = function 
        | PatientQuery qry ->
            Patient.ReadModels.query state qry
        | _ -> async { return NotHandled }


    let registered = 
        Patient.ReadModels.registered ()
        |> mapPatientModel


    let config = 
        let storageLog = Logger.noLog // Logger.logPerf "storage"
        let commandLog = Logger.noLog // Logger.logPerf "command"
        
        {
            EventStorageInit =
                fun () -> 
                    EventStorage.InMemoryStorage.initializeWithLogging storageLog []
            CommandHandlerInit =
                CommandHandler.initializeWithLogging commandLog behaviour
            QueryHandler =
                QueryHandler.initialize
                    [
                        patientQuery registered.State
                    ]
            EventHandlers =
                [
                    registered.EventHandler
                ]
        }

    let init () = EventSourced<Command, 
                                    Event, 
                                    Query>(config)

    let handleCommand app = EventSourced.handleCommand app streamId // app.HandleCommand streamId

    let getStream app = EventSourced.getStream app streamId

    let getAllEvents app = (EventSourced.getAllEvents app) ()

    let handleQuery app = EventSourced.handleQuery app



open System
open Infrastructure
open Domain

let app = App.init ()

let dto = Patient.Dto.dto ()
dto.HospitalNumber <- "0"
dto.FirstName <- "Test2"
dto.LastName <- "Test2"
dto.BirthDate <- DateTime.Now.AddDays(-2000.) |> Some

let count app =
    app
    |> App.getAllEvents
    |> Async.RunSynchronously
    |>  (fun r ->
        match r with
        | Ok evs -> 
            evs
            |> List.length
        | Error _ -> 0
    )

let perform n app =
    let c = count app
    for _ in [1..n] do
        let n = 
            dto.HospitalNumber
            |> (fun hn -> if hn = "" then "0" else hn)
            |> System.Int32.Parse 
            |> ((+) 1)
            |> string
        // System.Threading.Thread.Sleep(1000)
        dto.HospitalNumber <- n //|> string

        let isRegistered =
            Domain.Patient.ReadModels.GetRegistered
            |> App.PatientQuery
            |> App.handleQuery app
            |> Async.RunSynchronously
            |> function
            | Handled obj ->
                obj 
                :?> Domain.Patient.ReadModels.RegisteredPatient list
                |> List.exists (fun p ->
                    p.HospitalNumber = dto.HospitalNumber
                )
            | _ -> false
                

        Patient.Behaviour.Register (isRegistered, dto)
        |> App.PatientCommand
        |> App.handleCommand app
        |> Async.RunSynchronously
        |> ignore

    while not (count app = n + c) do ()
    
    printfn "count: %i" (count app)

let printEvents app =            
    app
    |>App.getStream 
    |> Async.RunSynchronously
    |> function
    | Ok evs -> 
        evs 
        |> List.iter (fun e ->
            match e.Event with 
            | App.PatientEvent pe ->
                match pe with
                | Patient.Registered dto -> 
                    dto
                    |> Patient.Dto.toString
                    |> printfn "%s"
                | _ -> ()
            | _ -> ()
        )
    | _ -> ()

app
|> App.getAllEvents
|> Async.RunSynchronously
|> Helper.printEvents "All Events"

Domain.Patient.ReadModels.GetRegistered
|> App.PatientQuery
|> App.handleQuery app
|> Async.RunSynchronously
|> function
| Handled obj ->
    printfn "Registered patients"
    obj 
    |> (fun o -> (printfn "got %A") o)
| _ -> ()

Domain.Patient.ReadModels.GetRegistered
|> App.PatientQuery
|> App.handleQuery app
|> Async.RunSynchronously
|> function
| Handled obj ->
    obj 
    :?> Domain.Patient.ReadModels.RegisteredPatient list
    |> List.iter (fun pat ->
        
        Patient.Behaviour.Admit (true, true, pat.HospitalNumber)
        |> App.PatientCommand
        |> App.handleCommand app
        |> Async.RunSynchronously
        |> ignore
    )
| _ -> ()


Domain.Patient.ReadModels.OnlyAdmitted
|> App.PatientQuery
|> App.handleQuery app
|> Async.RunSynchronously
|> function
| Handled obj ->
    printfn "Admitted patients"
    obj 
    |> (fun o -> (printfn "got %A") o)
| _ -> ()


for i in [1..1000] do
    let n = 
        dto.HospitalNumber
        |> (fun n -> if n = "" then "0" else n)
        |> System.Int32.Parse 
        |> ((+) 1)

    dto.HospitalNumber <- i |> string
    dto
    |> Patient.Dto.fromDto
    |> (printfn "%A")

InformedICU.Extensions.Result.Tests.Test.run ()
Domain.Patient.Behaviour.Tests.run ()


