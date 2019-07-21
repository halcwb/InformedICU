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


/// A wrapper for MailboxProcessor that catches all unhandled exceptions
/// and reports them via the 'OnError' event. Otherwise, the API
/// is the same as the API of 'MailboxProcessor'

module Infrastructure =

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

    type EventSource = System.Guid

    type EventProducer<'Event> =
        'Event list -> 'Event list

    type EventMetadata =
        {
            Source : EventSource
            RecordedAtUtc : System.DateTime
        }

    type EventEnvelope<'Event> =
        {
            Metadata : EventMetadata
            Event : 'Event
        }

    type EventHandler<'Event> =
        EventEnvelope<'Event> list -> Async<unit>

    type EventResult<'Event> =
        Result<EventEnvelope<'Event> list, string>

    type EventStore<'Event> =
        {
            Get : unit -> Async<EventResult<'Event>>
            GetStream : EventSource -> Async<EventResult<'Event>>
            Append : EventEnvelope<'Event> list -> Async<Result<unit, string>>
            OnError : IEvent<exn>
            OnEvents : IEvent<EventEnvelope<'Event> list>
        }

    type EventListener<'Event> =
        {
            Subscribe : EventHandler<'Event> -> unit
            Notify : EventEnvelope<'Event> list -> unit
        }

    type EventStorage<'Event> =
        {
            Get : unit -> Async<EventResult<'Event>>
            GetStream : EventSource -> Async<EventResult<'Event>>
            Append : EventEnvelope<'Event> list -> Async<unit>
        }

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
        Handle : EventSource -> 'Command -> Async<Result<unit,string>>
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

            waitForAnyKey()

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

            waitForAnyKey()


        let printCommandResults header result =
            match result with
            | Ok _ ->
              printfn "\n%s: %A" header result

            | Error error ->
              printError (sprintf "Command Error: %s" error) ""

            waitForAnyKey()


    type EventSourcedConfig<'Comand,'Event,'Query> =
        {
            EventStoreInit : EventStorage<'Event> -> EventStore<'Event>
            EventStorageInit : unit -> EventStorage<'Event>
            CommandHandlerInit : EventStore<'Event> -> CommandHandler<'Comand>
            QueryHandler : QueryHandler<'Query>
            EventListenerInit : unit -> EventListener<'Event>
            EventHandlers : EventHandler<'Event> list
        }

    type EventSourced<'Comand,'Event,'Query> (configuration : EventSourcedConfig<'Comand,'Event,'Query>) =

        let eventStorage = configuration.EventStorageInit()

        let eventStore = configuration.EventStoreInit eventStorage

        let commandHandler = configuration.CommandHandlerInit eventStore

        let queryHandler = configuration.QueryHandler

        let eventListener = configuration.EventListenerInit()

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

        member __.GetStream eventSource =
            eventStore.GetStream eventSource


    module FileStorage =

        open System.IO
        open Thoth.Json.Net

        let private get store =
            store
            |> File.ReadLines
            |> List.ofSeq
            |> List.traverseResult Decode.Auto.fromString<EventEnvelope<'Event>>

        let private getStream store source =
            store
            |> File.ReadLines
            |> List.ofSeq
            |> List.traverseResult Decode.Auto.fromString<EventEnvelope<'Event>>
            |> Result.map (List.filter (fun ee -> ee.Metadata.Source = source))

        let private append store events =
            use streamWriter = new StreamWriter(store, true)
            events
            |> List.map (fun eventEnvelope -> Encode.Auto.toString(0,eventEnvelope))
            |> List.iter streamWriter.WriteLine

            do streamWriter.Flush()

        let initialize store : EventStorage<_> =
            {
                Get = fun () -> async { return get store }
                GetStream = fun eventSource -> async { return getStream store eventSource  }
                Append = fun events -> async { return append store events }
            }


    module InMemoryStorage =

        type Msg<'Event> =
            private
            | Get of AsyncReplyChannel<EventResult<'Event>>
            | GetStream of EventSource * AsyncReplyChannel<EventResult<'Event>>
            | Append of EventEnvelope<'Event> list * AsyncReplyChannel<unit>

        let private streamFor source history =
            history |> List.filter (fun ee -> ee.Metadata.Source = source)

        let initialize () : EventStorage<'Event> =
            let history : EventEnvelope<'Event> list = []

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
    
                        | GetStream (source,reply) ->
                            history
                            |> streamFor source
                            |> Ok
                            |> reply.Reply

                            return! loop history

                        | Append (events,reply) ->
                            reply.Reply ()
                            return! loop (history @ events)
                    }

                loop history

            let agent = Agent<Msg<_>>.Start(proc)

            {
                Get = fun () ->  agent.PostAndAsyncReply Get
                GetStream = fun eventSource -> agent.PostAndAsyncReply (fun reply -> (eventSource,reply) |> GetStream)
                Append = fun events -> agent.PostAndAsyncReply (fun reply -> (events,reply) |> Append)
            }

        module Tests =

            type Event = | HelloWorld

            let storage : EventStorage<Event> = initialize()

            let private enveloped source events =
                let now = System.DateTime.UtcNow
                let envelope event =
                    {
                        Metadata = {
                            Source = source
                            RecordedAtUtc = now
                            }
                        Event = event
                    }

                events |> List.map envelope

            let run () =
                let source = System.Guid.NewGuid ()
                HelloWorld 
                |> List.replicate 10
                |> enveloped source
                |> storage.Append
                |> ignore

                storage.GetStream source
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

        //let private getStream (DB_Connection_String db_connection) source =
        //    async {
        //        return
        //            db_connection
        //            |> Sql.connect
        //            |> Sql.query (sprintf "%s WHERE source = @source %s" select order)
        //            |> Sql.parameters [ "@source", SqlValue.Uuid source ]
        //            |> Sql.executeReader hydrateEventEnvelopes
        //            |> List.traverseResult id
        //    }

        //let private append (DB_Connection_String db_connection) eventEnvelopes =
        //    let query = """
        //      INSERT INTO event_store (source, recorded_at_utc, event_index, metadata, payload)
        //      VALUES (@source, @recorded_at_utc, @event_index, @metadata, @payload)"""

        //    let parameters =
        //        eventEnvelopes
        //        |> List.mapi (fun index eventEnvelope ->
        //            [
        //                "@source", SqlValue.Uuid eventEnvelope.Metadata.Source
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
            | GetStream of EventSource * AsyncReplyChannel<EventResult<'Event>>
            | Append of EventEnvelope<'Event> list * AsyncReplyChannel<Result<unit,string>>

        let initialize (storage : EventStorage<_>) : EventStore<_> =
            let eventsAppended = Event<EventEnvelope<_> list>()

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


                        | GetStream (source,reply) ->
                            try
                                let! stream = source |> storage.GetStream
                                do stream |> reply.Reply
                            with exn ->
                                do inbox.Trigger(exn)
                                do exn.Message |> Error |> reply.Reply

                            return! loop ()

                        | Append (events,reply) ->
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
                GetStream = fun eventSource -> agent.PostAndAsyncReply (fun reply -> GetStream (eventSource,reply))
                Append = fun events -> agent.PostAndAsyncReply (fun reply -> Append (events,reply))
                OnError = agent.OnError
                OnEvents = eventsAppended.Publish
            }

        module Tests =

            type Event = | HelloWorld

            // create an event store for the hello world event
            let store : EventStore<Event> = 
                InMemoryStorage.initialize ()
                |> initialize

            // will print out received event envelopes
            store.OnEvents.Add (printfn "Received: %A")

            // create event envelopes for events belonging to a source
            let private enveloped source events =
                let now = System.DateTime.UtcNow
                let envelope event =
                    {
                        Metadata = {
                            Source = source
                            RecordedAtUtc = now
                            }
                        Event = event
                    }

                events |> List.map envelope

            // run the tests
            let run () =
                let source = System.Guid.NewGuid ()
                HelloWorld
                |> List.replicate 5
                |> enveloped source
                |> store.Append
                |> ignore

                let source = System.Guid.NewGuid ()
                HelloWorld
                |> List.replicate 5
                |> enveloped source
                |> store.Append
                |> ignore
                
                // get the appended events
                store.GetStream source
                |> Async.RunSynchronously
                |> Helper.printEvents "Get Stream"

                // get all appended events
                store.Get ()
                |> Async.RunSynchronously
                |> Helper.printEvents "Get All"



    module EventListener =

        type Msg<'Event> =
            | Notify of EventEnvelope<'Event> list
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

            // create event envelopes for events belonging to a source
            let private enveloped events =
                let source = System.Guid.NewGuid ()
                let now = System.DateTime.UtcNow
                let envelope event =
                    {
                        Metadata = {
                            Source = source
                            RecordedAtUtc = now
                            }
                        Event = event
                    }

                events |> List.map envelope


            type Event = HelloWorld

            let listener : EventListener<Event> = initialize()

            let handler : EventHandler<Event> = fun ees ->
                async { return printfn "Handled: %A" ees }

            let events =
                [ HelloWorld ] |> enveloped

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



    module CommandHandler =

        let private asEvents eventEnvelopes =
            eventEnvelopes |> List.map (fun envelope -> envelope.Event)

        let private enveloped source events =
            let now = System.DateTime.UtcNow
            let envelope event =
                {
                    Metadata = {
                        Source = source
                        RecordedAtUtc = now
                        }
                    Event = event
                }

            events |> List.map envelope

        type Msg<'Command> =
            | Handle of EventSource * 'Command * AsyncReplyChannel<Result<unit,string>>

        let initialize (behaviour : Behaviour<_,_>) (eventStore : EventStore<_>) : CommandHandler<_> =
            let proc (inbox : Agent<Msg<_>>) =
                let rec loop () =
                    async {
                        let! msg = inbox.Receive()

                        match msg with
                        | Handle (eventSource,command,reply) ->
                            let! stream = eventSource |> eventStore.GetStream

                            let newEvents =
                                stream |> Result.map (asEvents >> behaviour command >> enveloped eventSource)

                            let! result =
                                newEvents
                                |> function
                                    | Ok events -> eventStore.Append events
                                    | Error err -> async { return Error err }

                            do reply.Reply result

                            return! loop ()
                    }

                loop ()

            let agent = Agent<Msg<_>>.Start(proc)

            {
                Handle = fun source command -> agent.PostAndAsyncReply (fun reply -> Handle (source,command,reply))
                OnError = agent.OnError
            }