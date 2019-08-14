#I __SOURCE_DIRECTORY__

#load "./../../.paket/load/netcoreapp2.2/Npgsql.fsx"
#load "./../../.paket/load/netcoreapp2.2/main.group.fsx"
// Extensions
#load "./../Extensions/List.fs"
#load "./../Extensions/Option.fs"
#load "./../Extensions/Async.fs"
#load "./../Extensions/Result.fs"
// Domain
#load "./../Domain/Types.fs"
#load "./../Domain/Implement.fs"
#load "./../Domain/Projections.fs"
// EventSourced
#load "./../Agent/Agent.fs"
#load "./../EventSourced/Types.fs"
#load "./../EventSourced/Event.fs"
#load "./../EventSourced/Projection.fs"
#load "./../EventSourced/Utils/Utils.fs"
#load "./../EventSourced/EventStorage/InMemoryStorage.fs"
#load "./../EventSourced/EventStore.fs"
#load "./../EventSourced/CommandHandler.fs"
#load "./../EventSourced/EventListener.fs"
#load "./../EventSourced/QueryHandler.fs"
#load "./../EventSourced/ReadModel.fs"
#load "./../EventSourced/EventSourced.fs"
// Application
#load "./../Application/Api.fs"

open System

open InformedICU.Extensions
open InformedICU.Extensions.Result.Operators
open InformedICU.Domain.Types
open InformedICU.Domain
open InformedICU.Domain.Projections
open InformedICU.Application.Api

let dto = Patient.Dto.dto ()

dto.LastName <- "Test"
dto.FirstName <- "Test"
dto.BirthDate <- DateTime.Now |> Some

let app = App.init ()

let streamId = Guid.NewGuid().ToString()

[
    (Validate dto)
    (Register "2")
]
|> List.map ((app.HandleCommand streamId) >> Async.RunSynchronously)
|> printfn "%A"


app.GetAllEvents ()
|> Async.RunSynchronously
|> function
| Ok es -> 
    es 
    |> List.iter (fun e -> 
        e.Metadata.EventVersion |> printfn "version: %i"
        e.Event |> printfn "%A"
    )
| Error err -> printfn "%A" err


app.HandleQuery (Patient.ReadModels.GetRegistered)
|> Async.RunSynchronously
