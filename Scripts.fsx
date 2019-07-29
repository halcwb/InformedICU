/// Scripting the entire ICU app
///
#I __SOURCE_DIRECTORY__
#load ".paket/load/netcoreapp2.2/main.group.fsx"


module List =

    /// Map a Result producing function over a list to get a new Result
    /// ('a -> Result<'b>) -> 'a list -> Result<'b list>
    let traverseResult f list =

        // define the monadic functions
        let (>>=) x f = Result.bind f x
        let ok = Result.Ok

        // right fold over the list
        let initState = ok []
        let folder head tail =
            f head >>= (fun h ->
            tail >>= (fun t ->
            ok (h :: t) ))

        List.foldBack folder list initState

    module Tests =
    
        let f1 x : Result<_, string> = Result.Ok x
        let f2 _ : Result<string, _> = Result.Error "always fails"

        traverseResult f1 [ "a" ] // returns OK [ "a" ]
        traverseResult f2 [ "a" ] // returns Error "always fails"


module Option =

    type OptionBuilder() =
        member x.Bind(v,f) = Option.bind f v
        member x.Return v = Some v
        member x.ReturnFrom o = o
        member x.Zero () = None

    let option = OptionBuilder()

    module Tests =

        let twice x =
            option {
                let! v = x
                return v + v
            }

        Some ("x") |> twice // returns Some "xx"
        None |> twice       // returns None


module Infrastructure =

    /// A wrapper for MailboxProcessor that catches all unhandled exceptions
    /// and reports them via the 'OnError' event. Otherwise, the API
    /// is the same as the API of 'MailboxProcessor'
    type Agent<'T>(f: Agent<'T> -> Async<unit>) as self =
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
        member __.Start() = inbox.Start()
        /// Receive a message from the mailbox processor
        member __.Receive() = inbox.Receive()
        /// Post a message to the mailbox processor
        member __.Post(value:'T) = inbox.Post value

        member __.PostAndReply(f: AsyncReplyChannel<'a> -> 'T) = inbox.PostAndReply f

        member __.PostAndAsyncReply(f: AsyncReplyChannel<'a> -> 'T) = inbox.PostAndAsyncReply f

        /// Start the mailbox processor
        static member Start f =
            let agent = new Agent<_>(f)
            agent.Start()
            agent

    type EventId = System.Guid

    type StreamId = string

    /// Produces a new list of events based on
    /// a list of previous events
    type EventProducer<'Event> =
        'Event list -> 'Event list

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
        {
            Init : 'State
            Update : 'State -> 'Event -> 'State
        }


    type QueryResult =
        | Handled of obj
        | NotHandled
        | QueryError of string

    type  QueryHandler<'Query> =
        {
            Handle : 'Query -> Async<QueryResult>
        }

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
      'Command -> EventProducer<'Event>

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

            | Error error -> printError (sprintf "Error when retrieving events: %s" error) ""

            // waitForAnyKey()

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

            // waitForAnyKey()


        let printCommandResults header result =
            match result with
            | Ok _ ->
              printfn "\n%s: %A" header result

            | Error error ->
              printError (sprintf "Command Error: %s" error) ""

            // waitForAnyKey()
           

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

        let withStreamId streamId (e: Event<_>) = e.Metadata.StreamId = streamId

    module Projection =
        
        let project projection events =
            events |> List.fold projection.Update projection.Init

        let intoMap projection =
            fun state event ->
                state
                |> Map.tryFind event.Metadata.StreamId
                |> Option.defaultValue projection.Init
                |> fun projectionState -> event.Event |> projection.Update projectionState
                |> fun newState -> state |> Map.add event.Metadata.StreamId newState


    module EventStorage =

        module FileStorage =

            open System.IO
            open Thoth.Json.Net

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
                    GetStream = fun streamId -> async { return getStream store streamId  }
                    Append = fun events -> async { return append store events }
                }


        module InMemoryStorage =

            type Msg<'Event> =
                private
                | Get of AsyncReplyChannel<EventResult<'Event>>
                | GetStream of StreamId * AsyncReplyChannel<EventResult<'Event>>
                | Append of Event<'Event> list * AsyncReplyChannel<unit>

            let private streamFor streamId history =
                history |> List.filter (Event.withStreamId streamId)

            let initialize history : EventStorage<'Event> =
    //            let history : Event<'Event> list = []

                let proc (inbox : Agent<Msg<_>>) =
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

                let agent = Agent<Msg<_>>.Start(proc)

                {
                    Get = fun () ->  agent.PostAndAsyncReply Get
                    GetStream = fun stream -> agent.PostAndAsyncReply (fun reply -> (stream, reply) |> GetStream)
                    Append = fun events -> agent.PostAndAsyncReply (fun reply -> (events, reply) |> Append)
                }

            module Tests =
    
                type Event = | HelloWorld

                let storage : EventStorage<Event> = initialize []

                let run () =
                    let streamId = "helloWorld"
                    HelloWorld 
                    |> List.replicate 10
                    |> Event.enveloped streamId
                    |> storage.Append
                    |> ignore

                    storage.GetStream streamId
                    |> Async.RunSynchronously
                    |> Helper.printEvents "Stored events in storage:"



        //module PostgresStorage =

            //open Npgsql.FSharp
            //open Thoth.Json.Net
            //open Helper
            //open Option

            //let select = "SELECT metadata, payload FROM event_store"
            //let order = "ORDER BY recorded_at_utc ASC, event_index ASC"

            //let private hydrateEventEnvelopes reader =
            //    let row = Sql.readRow reader
            //    option {
            //        let! metadata = Sql.readString "metadata" row
            //        let! payload = Sql.readString "payload" row

            //        let eventEnvelope =
            //            metadata
            //            |> Decode.Auto.fromString<EventMetadata>
            //            |> Result.bind (fun metadata ->
            //                payload
            //                |> Decode.Auto.fromString<'Event>
            //                |> Result.map (fun event -> { Metadata = metadata ; Event = event}))

            //        return eventEnvelope
            //    }

            //let private get (DB_Connection_String db_connection) =
            //    async {
            //        return
            //            db_connection
            //            |> Sql.connect
            //            |> Sql.query (sprintf "%s %s" select order)
            //            |> Sql.executeReader hydrateEventEnvelopes
            //            |> List.traverseResult id
            //    }

            //let private getStream (DB_Connection_String db_connection) streamId =
            //    async {
            //        return
            //            db_connection
            //            |> Sql.connect
            //            |> Sql.query (sprintf "%s WHERE streamId = @streamId %s" select order)
            //            |> Sql.parameters [ "@streamId", SqlValue.Uuid streamId ]
            //            |> Sql.executeReader hydrateEventEnvelopes
            //            |> List.traverseResult id
            //    }

            //let private append (DB_Connection_String db_connection) eventEnvelopes =
            //    let query = """
            //      INSERT INTO event_store (streamId, recorded_at_utc, event_index, metadata, payload)
            //      VALUES (@streamId, @recorded_at_utc, @event_index, @metadata, @payload)"""

            //    let parameters =
            //        eventEnvelopes
            //        |> List.mapi (fun index eventEnvelope ->
            //            [
            //                "@streamId", SqlValue.Uuid eventEnvelope.Metadata.Source
            //                "@recorded_at_utc", SqlValue.Date eventEnvelope.Metadata.RecordedAtUtc
            //                "@event_index", SqlValue.Int index
            //                "@metadata", SqlValue.Jsonb <| Encode.Auto.toString(0,eventEnvelope.Metadata)
            //                "@payload", SqlValue.Jsonb <| Encode.Auto.toString(0,eventEnvelope.Event)
            //            ])

            //    db_connection
            //    |> Sql.connect
            //    |> Sql.executeTransactionAsync [ query, parameters ]
            //    |> Async.Ignore


            //let initialize db_connection : EventStorage<_> =
                //{
                //    Get = fun () -> get db_connection
                //    GetStream = fun eventSource -> getStream db_connection eventSource
                //    Append = fun events -> append db_connection events
                //}



    module EventStore =

        type Msg<'Event> =
            | Get of AsyncReplyChannel<EventResult<'Event>>
            | GetStream of StreamId * AsyncReplyChannel<EventResult<'Event>>
            | Append of Event<'Event> list * AsyncReplyChannel<Result<unit,string>>

        let initialize (storage : EventStorage<_>) : EventStore<_> =
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

            let agent =  Agent<Msg<_>>.Start(proc)

            {
                Get = fun () -> agent.PostAndAsyncReply Get
                GetStream = fun stream -> agent.PostAndAsyncReply (fun reply -> GetStream (stream, reply))
                Append = fun events -> agent.PostAndAsyncReply (fun reply -> Append (events, reply))
                OnError = agent.OnError
                OnEvents = eventsAppended.Publish
            }

        module Tests =

            type Event = | HelloWorld

            // create an event store for the hello world event
            let store : EventStore<Event> = 
                EventStorage.InMemoryStorage.initialize []
                |> initialize

            // will print out received event envelopes
            store.OnEvents.Add (printfn "Received: %A")
            
            // run the tests
            let run () =
                let streamId = "helloworld1"
                
                HelloWorld
                |> List.replicate 5
                |> Event.enveloped streamId
                |> store.Append
                |> ignore

                let streamId = "helloworld2"
                HelloWorld
                |> List.replicate 5
                |> Event.enveloped streamId
                |> store.Append
                |> ignore
                
                // get the appended events
                store.GetStream streamId
                |> Async.RunSynchronously
                |> Helper.printEvents "Get Stream"

                // get all appended events
                store.Get ()
                |> Async.RunSynchronously
                |> Helper.printEvents "Get All"



    module EventListener =

        type Msg<'Event> =
            | Notify of Event<'Event> list
            | Subscribe of EventHandler<'Event>


        let notifyEventHandlers events (handlers : EventHandler<_> list) =
            handlers
            |> List.map (fun subscription -> events |> subscription )
            |> Async.Parallel
            |> Async.Ignore

        let initialize () : EventListener<_> =

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

            let agent = Agent<Msg<_>>.Start(proc)

            {
                Notify = Notify >> agent.Post
                Subscribe = Subscribe >> agent.Post
            }

        module Tests =

            type Event = HelloWorld

            let listener : EventListener<Event> = initialize()

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
                    match! handler.Handle query with
                    | NotHandled ->
                        return! choice rest query

                    | Handled response ->
                        return Handled response

                    | QueryError response ->
                        return QueryError response

                | _ -> return NotHandled
            }

        let initialize queryHandlers : QueryHandler<_> =
            {
              Handle = choice queryHandlers
            }
            
        module Tests =

            let handler : QueryHandler<string> =
                [
                    { Handle = fun _ -> async { printf "not handled "; return NotHandled } }
                    { Handle = fun _ -> async { printf "not handled "; return NotHandled } }
                    { Handle = fun _ -> async { printf "not handled "; return NotHandled } }
                    { Handle = fun x -> async { return x |> box |> Handled } }
                ]
                |> initialize

            let run () =
                handler.Handle "Finally handled"


    module CommandHandler =
                        
        type Msg<'Event, 'Command> =
            | Handle of StreamId * 'Command * AsyncReplyChannel<Result<unit,string>>

        let initialize (behaviour : Behaviour<_,_>) 
                       (eventStore : EventStore<_>) : CommandHandler<_> =
            let proc (inbox : Agent<Msg<_, _>>) =
                let rec loop () =
                    async {
                        let! msg = inbox.Receive()

                        match msg with
                        | Handle (streamId, command, reply) ->
                            let! stream = streamId |> eventStore.GetStream

                            let newEvents =
                                stream 
                                |> Result.map (fun evs ->
                                    evs
                                    |> Event.asEvents 
                                    |> behaviour command 
                                    |> Event.enveloped streamId
                                )

                            let! result =
                                newEvents
                                |> function
                                    | Ok events -> eventStore.Append events
                                    | Error err -> async { return Error err }

                            do reply.Reply result

                            return! loop ()
                    }

                loop ()

            let agent = Agent<Msg<_, _>>.Start(proc)

            {
                Handle = fun streamId command -> agent.PostAndAsyncReply (fun reply -> Handle (streamId, command, reply))
                OnError = agent.OnError
            }

        module Tests =

            type Command = HelloWordCommand of string

            type Event = HelloWordEvent of string

            // create an event store for the hello world event
            let store : EventStore<Event> = 
                EventStorage.InMemoryStorage.initialize []
                |> EventStore.initialize

            let behaviour : Behaviour<Command, Event> =
                fun cmd ees ->
                    match cmd with
                    | HelloWordCommand s -> 
                        if ees |> List.length >= 1 then []
                        else
                            HelloWordEvent s
                            |> List.replicate 2

            let handler =
                initialize behaviour store

            let run () =
                let streamId = "helloworld"
                
                "Hello World"
                |> HelloWordCommand
                |> handler.Handle streamId
                |> Async.RunSynchronously
                |> printfn "%A"

                store.Get ()
                |> Async.RunSynchronously
                |> Helper.printEvents "Events"

                "Hello World"
                |> HelloWordCommand
                |> handler.Handle streamId
                |> Async.RunSynchronously
                |> printfn "%A"

                store.Get ()
                |> Async.RunSynchronously
                |> Helper.printEvents "Events"


    module ReadModel =

        type Msg<'Event,'Result> =
            | Notify of Event<'Event> list * AsyncReplyChannel<unit>
            | State of AsyncReplyChannel<'Result>

        let inMemory (updateState : 'State -> Event<'Event> list -> 'State) 
                     (initState : 'State) : ReadModel<'Event, 'State> =
            let agent =
                let eventSubscriber (inbox : Agent<Msg<_,_>>) =
                    let rec loop state =
                        async {
                        let! msg = inbox.Receive()

                        match msg with
                        | Notify (eventEnvelopes, reply) ->
                            printfn "Readmodel got %i envelopes" (eventEnvelopes |> List.length)
                            reply.Reply ()
                            return! loop (eventEnvelopes |> updateState state)

                        | State reply ->
                            reply.Reply state
                            return! loop state
                        }

                    loop initState

                Agent<Msg<_,_>>.Start(eventSubscriber)

            {
                EventHandler = fun eventEnvelopes -> agent.PostAndAsyncReply(fun reply -> Notify (eventEnvelopes, reply))
                State = fun () -> agent.PostAndAsyncReply State
            }

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
            eventStore.OnError.Add(fun exn -> Helper.printError (sprintf "EventStore Error: %s" exn.Message) exn)
            commandHandler.OnError.Add(fun exn -> Helper.printError (sprintf "CommandHandler Error: %s" exn.Message) exn)
            eventStore.OnEvents.Add eventListener.Notify
            configuration.EventHandlers |> List.iter eventListener.Subscribe

        member __.HandleCommand eventSource command =
            commandHandler.Handle eventSource command

        member __.HandleQuery query =
            queryHandler.Handle query

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

    type HospitalNumber = string

    type Patient =
        {
            HospitalNumber : HospitalNumber
            LastName : string
            FirstName : string
            BirthDate : DateTime
        }
        
    module Patient =


        let create hn ln fn bd : Patient =
            {
                HospitalNumber = hn
                LastName = ln
                FirstName = fn
                BirthDate = bd
            }

        type Dto () = 
            member val HospitalNumber  = "" with get, set
            member val LastName = "" with get, set
            member val FirstName = "" with get, set
            member val BirthDate : DateTime Option = None with get, set

        module Dto = 

            let dto () = new Dto () 

            let toDto (pat : Patient) =
                let dto = dto ()
                dto.HospitalNumber <- pat.HospitalNumber
                dto.FirstName <- pat.FirstName
                dto.LastName <- pat.LastName
                dto.BirthDate <- pat.BirthDate |> Some
                dto

            let fromDto (dto : Dto) =
                match dto.BirthDate with
                | Some bd  ->
                    create dto.HospitalNumber
                           dto.LastName
                           dto.FirstName
                           bd
                    |> Some
                | None -> None

        type Event =
            | Registered of Patient
            | InvalidPatient of Dto
            | AllReadyRegistered of HospitalNumber
            | Admitted of HospitalNumber
            | Discharged of HospitalNumber
            | UnknownHospitalNumber of HospitalNumber

        module Projections =

            open Infrastructure

            let updateRegisteredPatients (set: Set<_>) event =
                match event with
                | Registered pat ->
                    pat |> set.Add
                | _ -> set

            let registerdPatients =
                {
                    Init = Set.empty
                    Update = updateRegisteredPatients
                }

            let admittedPatients events =
                let registered = 
                    events 
                    |> (Projection.project registerdPatients)
                    |> Set.toList

                events
                |> List.fold (fun acc e ->
                    match e with
                    | Admitted hn ->
                        match registered 
                              |> List.tryFind (fun pat -> pat.HospitalNumber = hn) with
                        | Some pat -> acc |> List.append [ pat ]
                        | None -> acc
                    | _ -> acc
                ) []

            module Tests =

                open Infrastructure

                let store : EventStore<Event> =
                    EventStorage.InMemoryStorage.initialize []
                    |> EventStore.initialize

                let run () =
                    let streamId = "patients"

                    // add some registered patient events to the store
                    [
                        create "1" "LastName" "FirstName" DateTime.Now
                        create "2" "LastName" "FirstName" DateTime.Now
                        create "3" "LastName" "FirstName" DateTime.Now   
                    ]
                    |> List.map Registered
                    |> List.append [ "1" |> Admitted ]
                    |> Event.enveloped streamId
                    |> store.Append
                    |> ignore

                    // get the registered patients
                    store.GetStream streamId
                    |> Async.RunSynchronously
                    |> Result.map (Event.asEvents >> (Projection.project registerdPatients))
                    |> Result.map (fun set -> set |> Set.iter (printfn "%A"))
                    |> ignore

                    // get admitted patients
                    store.GetStream streamId
                    |> Async.RunSynchronously
                    |> Result.map (Event.asEvents >> admittedPatients)
                    |> Result.map (fun pats -> pats |> List.iter (printfn "%A"))
                    |> ignore


        module ReadModels =

            open Infrastructure

            type Query = 
                | Registered
                | Admitted
                | Discharged


            let registered () : ReadModel<_, _> =
                let updateState state evs =
                    evs
                    |> List.fold (Projection.intoMap Projections.registerdPatients) state

                ReadModel.inMemory updateState Map.empty


        module Behaviour =

            open Infrastructure

            type Command =
                | Register of Dto
                | Admit of HospitalNumber
                | Discharge of HospitalNumber

            let registerPatient pat events = 

                if events 
                   |> (Projection.project Projections.registerdPatients) 
                   |> Set.contains pat then 
                    AllReadyRegistered pat.HospitalNumber
                else 
                    pat |> Registered
                |> List.singleton

            let behaviour : Behaviour<Command, Event> =
                fun cmd evs ->
                    match cmd with
                    | Register dto -> 
                        match dto |> Dto.fromDto with
                        | Some pat -> 
                            evs
                            |> registerPatient pat
                        | None -> [ dto |> InvalidPatient ]
                    | _ -> "Not implemented yet" |> exn |> raise

            module Tests =

                open Infrastructure

                type Command = TestRegisterPatient
                
                // create an event store for the hello world event
                let store : EventStore<Event> = 
                    EventStorage.InMemoryStorage.initialize []
                    |> EventStore.initialize

                let behaviour : Behaviour<Command, Event> =
                    fun cmd ees ->
                        match cmd with
                        | TestRegisterPatient -> 
                            ees
                            |> registerPatient (create "1" "Test" "Test" (new DateTime(1965, 12, 7)))

                let handler =
                    CommandHandler.initialize behaviour store

                let listener = EventListener.initialize ()

                store.OnEvents.Add listener.Notify

                let readModel = ReadModels.registered ()
                readModel.EventHandler |> listener.Subscribe

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

                    readModel.State ()
                    |> Async.RunSynchronously
                    |> Map.iter (fun s p -> printfn "%A" p)



module App =

    module Patient =

        open Infrastructure 
        open Domain

        let store : EventStore<Patient.Event> = 
            EventStorage.InMemoryStorage.initialize []
            |> EventStore.initialize


        let handler =
            CommandHandler.initialize Patient.Behaviour.behaviour store

        let listener = EventListener.initialize ()

        store.OnEvents.Add listener.Notify

        let readModel = Patient.ReadModels.registered ()
        readModel.EventHandler |> listener.Subscribe

        let config = 
            {
                EventStorageInit =
                    fun () -> EventStorage.InMemoryStorage.initialize []
                CommandHandlerInit =
                    CommandHandler.initialize Patient.Behaviour.behaviour
                QueryHandler =
                    QueryHandler.initialize
                        [
                        ]
                EventHandlers =
                    [
                    ]
            }

        let app = EventSourced<Patient.Behaviour.Command, Patient.Event, Patient.ReadModels.Query>(config)


open System
open Infrastructure
open Domain

let dto = Patient.Dto.dto ()
dto.HospitalNumber <- "1"
dto.FirstName <- "Test"
dto.LastName <- "Test"
dto.BirthDate <- DateTime.Now.AddDays(-200.) |> Some


dto
|> Patient.Behaviour.Register
|> App.Patient.app.HandleCommand "patients"

App.Patient.app.GetStream "patients"
|> Async.RunSynchronously
|> Helper.printEvents "Events"


App.Patient.app.GetAllEvents ()
|> Async.RunSynchronously
|> Helper.printEvents "Events"

Domain.Patient.Behaviour.Tests.run ()

