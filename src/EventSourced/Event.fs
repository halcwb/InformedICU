namespace InformedICU.EventSourced

module Event =

    let newestVersion es =
        match es with
        | [] -> 0
        | e::_ -> e.Metadata.EventVersion

    let latestVersion es = 
        es
        |> List.rev 
        |> newestVersion

    let addMetaData latestVersion streamId events =
        let now = System.DateTime.UtcNow
        let eventId = System.Guid.NewGuid ()

        let envelope version event =
            {
                Metadata = {
                    EventId = eventId
                    StreamId = streamId
                    RecordedAtUtc = now
                    EventVersion = version
                    }
                Event = event
            }

        events |> List.mapi (fun i e -> e|> envelope (latestVersion + i))

    let removeMetaData events =
        events |> List.map (fun e -> e.Event)

    let withAggregateId streamId (e: Event<_>) = 
        e.Metadata.StreamId = streamId
