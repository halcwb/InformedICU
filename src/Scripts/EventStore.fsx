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

#time

open InformedICU.Agent
open InformedICU.Extensions
open InformedICU.EventSourced
open InformedICU.EventSourced.EventStorage

let streamId = "helloWorld"
    
type Event = | HelloWorld

let store : EventStore<Event> = 
    HelloWorld
    |> List.replicate 10000
    |> Event.addMetaData 0 streamId
    |> InMemoryStorage.initialize
    |> EventStore.initializeWithLogger (Logger.logPerf "Performance")


let run n =
    let latest =
        store.GetStream streamId
        |> Async.RunSynchronously
        |> Result.get
        |> Event.latestVersion

    for i in [1..n] do
        HelloWorld 
        |> List.singleton
        |> Event.addMetaData (latest + 1) streamId
        |> store.AppendStream streamId
        |> Async.RunSynchronously
        |> ignore

