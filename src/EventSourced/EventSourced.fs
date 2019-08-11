namespace InformedICU.EventSourced

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
            Utils.printError (sprintf "EventStore Error: %s" exn.Message) exn)
        commandHandler.OnError.Add(fun exn -> 
            Utils.printError (sprintf "CommandHandler Error: %s" exn.Message) exn)
        eventStore.OnEvents.Add eventListener.Notify
        configuration.EventHandlers |> List.iter eventListener.Subscribe

    member __.HandleCommand streamId command =
        commandHandler.Handle streamId command

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

    let handleCommand events = (events |> get).HandleCommand

    let handleQuery events = (events |> get).HandleQuery

    let getAllEvents events = (events |> get).GetAllEvents

    let getStream events = (events |> get).GetStream


