#I __SOURCE_DIRECTORY__

#load "./../Agent/Agent.fs"
#load "./../Extensions/List.fs"
#load "./../Extensions/Option.fs"
#load "./../Extensions/Result.fs"
#load "./../EventSourced/Types.fs"
#load "./../EventSourced/Event.fs"
#load "./../EventSourced/Utils/Utils.fs"
#load "./../EventSourced/EventStorage/InMemoryStorage.fs"
#load "./../EventSourced/EventStore.fs"
#load "./../EventSourced/CommandHandler.fs"

#time

open InformedICU.Agent
open InformedICU.Extensions
open InformedICU.EventSourced
open InformedICU.EventSourced.EventStorage

let streamId = "helloWorld"
    
type Event = | HelloWorld

type Command = SayHello

let store n : EventStore<Event> = 
    if n = 0 then
        List.empty
    else
        HelloWorld
        |> List.replicate n
        |> Event.addMetaData 0 streamId
    |> InMemoryStorage.initialize
    |> EventStore.initialize 

let sayHello : Workflow<Command, Event> =
    fun cmd es ->
        HelloWorld
        |> List.singleton

let handler store =
    store
    |> CommandHandler.initializeWithLogging (Logger.logPerf "Command Handler") sayHello

let run n1 n2 =
    let streamId = "helloworld"
    let store = store n1
    let handler =
        store
        |> handler

    for _ in [0..n2] do
        SayHello
        |> handler.Handle streamId
        |> Async.RunSynchronously
        |> ignore

    store.Get ()
    |> Async.RunSynchronously
    |> function
    | Ok es -> es |> List.length |> (printfn "%A")
    | Error err -> printfn "%s" err