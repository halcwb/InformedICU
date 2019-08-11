namespace InformedICU.EventSourced

module EventStore =

    open InformedICU.Agent

    type Msg<'Event> =
        | Get of AsyncReplyChannel<EventResult<'Event>>
        | GetStream of StreamId * AsyncReplyChannel<EventResult<'Event>>
        | AppendStream of StreamId * Event<'Event> list * AsyncReplyChannel<Result<unit,string>>

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


                    | GetStream (streamId, reply) ->
                        try
                            let! stream = streamId |> storage.GetStream
                            do stream |> reply.Reply
                        with exn ->
                            do inbox.Trigger(exn)
                            do exn.Message |> Error |> reply.Reply

                        return! loop ()

                    | AppendStream (streamId, events, reply) ->
                        try
                            events 
                            |> storage.AppendStream streamId
                            |> Async.RunSynchronously
                            |> function 
                            | Ok _ ->
                                do eventsAppended.Trigger events
                                do reply.Reply (Ok ())
                            | Error err -> 
                                err 
                                |> Error 
                                |> reply.Reply
                        with exn ->
                            do inbox.Trigger(exn)
                            do exn.Message |> Error |> reply.Reply

                        return! loop ()
                }

            loop ()

        let agent =  Agent.startWithLogging logger proc

        {
            Get = fun () -> agent.PostAndAsyncReply Get
            GetStream = fun streamId -> 
                agent.PostAndAsyncReply (fun reply -> GetStream (streamId, reply))
            AppendStream = fun streamId events -> 
                agent.PostAndAsyncReply (fun reply -> AppendStream (streamId, events, reply))
            OnError = agent.OnError
            OnEvents = eventsAppended.Publish
        }

    let initialize storage = _initialize Logger.noLog storage

    let initializeWithLogger logger storage = _initialize logger storage

