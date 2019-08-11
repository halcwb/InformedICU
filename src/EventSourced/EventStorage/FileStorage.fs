namespace InformedICU.EventSourced.EventStorage


module FileStorage =

    open System.IO
    open Thoth.Json.Net

    open InformedICU.Extensions
    open InformedICU.EventSourced

    let private get path =
        path
        |> File.ReadLines
        |> List.ofSeq
        |> List.traverseResult Decode.Auto.fromString<Event<'Event>>

    let private getStream path streamId =
        path
        |> File.ReadLines
        |> List.ofSeq
        |> List.traverseResult Decode.Auto.fromString<Event<'Event>>
        |> Result.map (List.filter (Event.withAggregateId streamId))

    let private appendStream path streamId events =
        let expected = 
            events
            |> Event.newestVersion
            |> (fun v -> v - 1)

        getStream path streamId
        |> function
        | Ok es ->
            let latest = 
                es
                |> Event.latestVersion

            if latest = expected then
                use streamWriter = new StreamWriter(path, true)
                events
                |> List.map (fun event -> Encode.Auto.toString(0, event))
                |> List.iter streamWriter.WriteLine

                streamWriter.Flush()
                |> Ok
            else
                streamId
                |> Utils.versionError latest expected
                |> Error
        | Error error -> error |> Error
            

    let initialize path : EventStorage<_> =
        let get = fun () -> get path
        let getStream = getStream path
        let appendStream = appendStream path
        {
            Get = fun () -> async { return get () }
            GetStream = fun streamId -> 
                async { return getStream streamId  }
            AppendStream = fun aggregageId events -> 
                async { return appendStream aggregageId events }
        }



