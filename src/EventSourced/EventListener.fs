namespace InformedICU.EventSourced

module EventListener =

    open InformedICU.Agent

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


