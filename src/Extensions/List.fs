namespace InformedICU.Extensions

module List =

    /// Map a Result producing function over a list to get a new Result
    /// ('a -> Result<'b>) -> 'a list -> Result<'b list>
    let traverseResult f list =

        // define the monadic functions
        let (>>=) x f = Result.bind f x
        let ok = Result.Ok

        // right fold over the list
        let initState = ok []
        let folder head tail =
            f head >>= (fun h ->
            tail >>= (fun t ->
            ok (h :: t) ))

        List.foldBack folder list initState


    let remove eqs xs = 
        xs
        |> List.fold (fun acc x ->
            if x |> eqs then acc
            else acc @ [x]
        ) []

    module Tests =
    
        let f1 x : Result<_, string> = Result.Ok x
        let f2 _ : Result<string, _> = Result.Error "always fails"

        let run () =
            traverseResult f1 [ "a" ] |> printfn "%A" // returns OK [ "a" ]
            traverseResult f2 [ "a" ] |> printfn "%A" // returns Error "always fails"

