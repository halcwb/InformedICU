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
#load "./../EventSourced/EventListener.fs"

#time

open InformedICU.Agent
open InformedICU.Extensions
open InformedICU.EventSourced
open InformedICU.EventSourced.EventStorage

open System.Diagnostics

type Event = Stopwatch

let listener : EventListener<Event> = 
    EventListener.initialize ()

let handler : EventHandler<Event> = fun ees ->
    async { return printfn "Handled in: %A" (ees |> List.head).Event.Elapsed.TotalSeconds }

let events watch =
    [ watch ] |> Event.addMetaData 0 "helloworld"

// subscribe handler
handler
|> listener.Subscribe

let run n =
    let watch = Stopwatch.StartNew ()

    for _ in [0..n] do
        
        watch
        |> events 
        |> listener.Notify





