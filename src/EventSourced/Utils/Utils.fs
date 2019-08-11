namespace InformedICU.EventSourced

module Utils =

    open System

    let versionError actual expected streamId =
        sprintf "%s: the actual version %i <> expected version %i" 
            streamId
            actual
            expected

    let waitForAnyKey () =
        Console.ReadKey() |> ignore

    let inline printError message details =
        Console.ForegroundColor <- ConsoleColor.Red
        printfn "\n%s" message
        Console.ResetColor()
        printfn "%A" details

    let printUl list =
        list
        |> List.iteri (fun i item -> printfn " %i: %A" (i+1) item)

    let printEvents header events =
        match events with
        | Ok events ->
            events
            |> List.length
            |> printfn "\nHistory for %s (Length: %i)" header

            events |> printUl

        | Error error ->
            let s = (sprintf "Error when retrieving events: %s" error)
            printError s ""

    let runAsync asnc =
        asnc |> Async.RunSynchronously

    let printQueryResults header result =
        result
        |> runAsync
        |> function
        | QueryResult.Handled result -> 
            printfn "\n%s: %A" header result

        | QueryResult.NotHandled ->
            printfn "\n%s: NOT HANDLED" header

        | QueryResult.QueryError error ->
            printError (sprintf "Query Error: %s" error) ""

    let printCommandResults header result =
        match result with
        | Ok _ ->
          printfn "\n%s: %A" header result

        | Error error ->
          printError (sprintf "Command Error: %s" error) ""

