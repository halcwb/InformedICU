namespace InformedICU.EventSourced

module ReadModel =

    open InformedICU.Agent

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


