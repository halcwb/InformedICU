namespace InformedICU.EventSourced

module Projection =

    type private Cache<'T> () =
        member val Count = 0 with get, set
        member val State : 'T Option  = None with get, set

    /// Cache a projection `p` according
    /// to the number of events
    let cache init f =
        let cache = new Cache<_>()
        fun xs ->
            let count = xs |> List.length
            let state =
                match cache.State with
                | Some s -> 
                    (cache.Count, s)
                | None -> (0, init)
                |> (fun (c, s) ->
                    xs
                    |> List.skip (if c > count then 0 else c)
                    |> List.fold f s
                )
            cache.Count <- count
            cache.State <- state |> Some
            state

    let cacheFold init f =
        let cache = new Cache<_>()
        fun xs ->
            let count = xs |> List.length
            let state =
                match cache.State with
                | Some s -> 
                    // (0, init) 
                    (cache.Count, s)
                | None -> (0, init)
                |> (fun (c, s) ->
                    if c > count then f s xs
                    else
                        xs
                        |> List.skip c
                        |> f s
                )
            cache.Count <- count
            cache.State <- state |> Some
            state

    let projectIntoMap init update =
      fun state event ->
        state
        |> Map.tryFind event.Metadata.StreamId
        |> Option.defaultValue init
        |> fun projectionState -> event.Event |> update projectionState
        |> fun newState -> state |> Map.add event.Metadata.StreamId newState

