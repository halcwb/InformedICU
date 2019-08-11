namespace InformedICU.EventSourced

module CommandHandler =

    open InformedICU.Agent
                    
    type Msg<'Event, 'Command> =
        | Handle of StreamId * 'Command * AsyncReplyChannel<Result<unit,string>>

    let private _initialize logger
                            (workflow : Workflow<_,_>) 
                            (eventStore : EventStore<_>) : CommandHandler<_> =
        
        let proc (inbox : Agent<Msg<_, _>>) =
            let rec loop () =
                async {
                    let! msg = inbox.Receive()

                    match msg with
                    | Handle (streamId, command, reply) ->
                        let! stream = eventStore.GetStream streamId

                        let events =
                            stream
                            |> function
                            | Ok es ->
                                let latest = es |> Event.latestVersion
                                
                                es
                                |> Event.removeMetaData
                                |> workflow command
                                |> Event.addMetaData latest streamId
                                |> Ok
                            | Error _ -> stream

                        let! result = 
                            events
                            |> function
                            | Ok es -> eventStore.AppendStream streamId es
                            | Error err -> async { return Error err }

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
    