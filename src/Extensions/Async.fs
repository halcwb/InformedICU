namespace InformedICU.Extensions

module Async =
    
    let map f (x : Async<_>) =
        async {
            let y = 
                x 
                |> Async.RunSynchronously
                |> f
            return y
        }
        

