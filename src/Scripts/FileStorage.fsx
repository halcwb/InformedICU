#I __SOURCE_DIRECTORY__

#load "./../../.paket/load/netcoreapp2.2/Npgsql.fsx"
#load "./../../.paket/load/netcoreapp2.2/main.group.fsx"

#load "./../Agent/Agent.fs"
#load "./../Extensions/List.fs"
#load "./../Extensions/Option.fs"
#load "./../Extensions/Result.fs"
#load "./../EventSourced/Types.fs"
#load "./../EventSourced/Event.fs"
#load "./../EventSourced/Utils/Utils.fs"
#load "./../EventSourced/EventStorage/FileStorage.fs"

#time

open System
open InformedICU.Extensions
open InformedICU.EventSourced.Utils
open InformedICU.EventSourced
open InformedICU.EventSourced.EventStorage

Environment.CurrentDirectory <- __SOURCE_DIRECTORY__

let streamId = "helloWorld"
    
type Event = | HelloWorld

let path = "./../../data/eventstore.txt"

let storage : EventStorage<Event> = 
    FileStorage.initialize path

let run n =
    let latest =
        storage.GetStream streamId
        |> Async.RunSynchronously
        |> Result.get
        |> Event.latestVersion

    for i in [1..n] do
        HelloWorld 
        |> List.singleton
        |> Event.addMetaData (latest + i) streamId
        |> storage.AppendStream streamId
        |> Async.RunSynchronously
        |> ignore

let read () =
    storage.GetStream streamId
    |> Async.RunSynchronously
    |> Result.map (fun es ->
        es
        |> List.iter (printfn "%A")
    )
