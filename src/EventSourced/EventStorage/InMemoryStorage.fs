namespace InformedICU.EventSourced.EventStorage

module InMemoryStorage =

    open InformedICU.Agent
    open InformedICU.EventSourced

    type private Cache<'Event> =
        {
            Count : int
            Stream : Event<'Event> list
        }

    type Msg<'Event> =
        private
        | Get of AsyncReplyChannel<EventResult<'Event>>
        | GetStream of StreamId * AsyncReplyChannel<EventResult<'Event>>
        | AppendStream of StreamId * Event<'Event> list * AsyncReplyChannel<Result<unit, string>>

    let private withAggregateId aggregagateId (e: Event<_>) = 
        e.Metadata.StreamId = aggregagateId

    let private streamFor<'Event> () =
        let cache : Map<StreamId, Cache<_>> ref = ref Map.empty

        fun (aggregagateId : StreamId) (history : Event<'Event> list ) ->
            match (!cache).TryFind(aggregagateId) with
            | Some { Count = c; Stream = stream } -> 
                (c, stream)
            | None -> 
                (0, [])
            |> fun (c, stream) ->
                let newStream =
                    history 
                    |> List.skip c
                    |> List.filter (withAggregateId aggregagateId)
                    |> List.append stream

                let newCache = 
                    {
                        Count = history |> List.length
                        Stream = newStream
                    }

                cache := (!cache).Add(aggregagateId, newCache)
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

                    | AppendStream (streamId, events, reply) ->
                        let latest =
                            history
                            |> streamFor streamId
                            |> Event.latestVersion
                        
                        let expected =
                            events
                            |> Event.newestVersion
                            |> (fun v -> v - 1)

                        if latest = expected then
                            reply.Reply (Ok ())
                            return! loop (history @ events)
                        else 
                            streamId
                            |> Utils.versionError latest expected
                            |> Error
                            |> reply.Reply
                            return! loop history
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
            AppendStream = fun streamId events -> 
                agent.PostAndAsyncReply (fun reply -> 
                    (streamId, events, reply) |> AppendStream
                )
        }

    let initialize history = _initialize Logger.noLog history

    let initializeWithLogging log history = _initialize log history

