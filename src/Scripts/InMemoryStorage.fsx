#I __SOURCE_DIRECTORY__

#load "./../Agent/Agent.fs"
#load "./../Extensions/List.fs"
#load "./../Extensions/Option.fs"
#load "./../Extensions/Result.fs"
#load "./../EventSourced/Types.fs"
#load "./../EventSourced/Event.fs"
#load "./../EventSourced/Utils/Utils.fs"
#load "./../EventSourced/EventStorage/InMemoryStorage.fs"

#time

open InformedICU.Agent
open InformedICU.Extensions
open InformedICU.EventSourced
open InformedICU.EventSourced.EventStorage

let streamId = "helloWorld"
    
type Event = | HelloWorld

let storage : EventStorage<Event> = 
    HelloWorld
    |> List.replicate 10
    |> Event.addMetaData 0 streamId
    |> InMemoryStorage.initializeWithLogging (Logger.logPerf "inmemory storage")

let run n =
    let latest =
        storage.GetStream streamId
        |> Async.RunSynchronously
        |> Result.get
        |> Event.latestVersion
    printfn "latest: %i" latest

    for i in [1..n] do
        HelloWorld 
        |> List.singleton
        |> Event.addMetaData (latest + i) streamId
        |> storage.AppendStream streamId
        |> Async.RunSynchronously
        |> function 
        | Ok _ -> printfn "added an event"
        | Error err -> printfn "%s" err

storage.Get ()
|> Async.RunSynchronously
