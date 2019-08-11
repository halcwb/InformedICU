namespace InformedICU.Extensions

module Option =

    module Builder =

        type OptionBuilder() =
            member x.Bind(v,f) = Option.bind f v
            member x.Return v = Some v
            member x.ReturnFrom o = o
            member x.Zero () = None

        let option = OptionBuilder()

    module Tests =
        
        open Builder

        let twice x =
            option {
                let! v = x
                return v + v
            }

        let run () =
            Some ("x") |> twice |> printfn "%A" // returns Some "xx"
            None |> twice |> printfn "%A" // returns None


