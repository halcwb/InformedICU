namespace InformedICU.Extensions

module Result = 

    let isOk (result : Result<_, _>) =
        match result with
        | Ok _ -> true
        | Error _ -> false

    let get (result : Result<_, _>) =
        match result with
        | Ok r -> r
        | Error e -> 
            e.ToString () 
            |> exn 
            |> raise

    let either success failure x =
        match x with
        | Ok s -> s |> success
        | Error e -> e |> failure


    /// given a function wrapped in a result
    /// and a value wrapped in a result
    /// apply the function to the value only if both are Success
    let applyR add f x =
        match f, x with
        | Ok f, Ok x -> 
            x 
            |> f 
            |> Ok 
        | Error e, Ok _ 
        | Ok _, Error e -> 
            e |> Error
        | Error e1, Error e2 -> 
            add e1 e2 |> Error 

    /// given a function that transforms a value
    /// apply it only if the result is on the Success branch
    let liftR f x =
        let f' =  f |> Result.Ok
        applyR (List.append) f' x

    let mapErrorList f x =
        match x with
        | Ok o -> o |> Ok
        | Error ee -> 
            ee 
            |> List.map f
            |> Error

    module Operators = 
    
        let (>>=) x f = Result.bind f x

        let (>=>) f1 f2 = f1 >> f2

        /// infix version of apply
        let (<*>) f x = applyR List.append f x

        let (<!>) = liftR

    module Tests =

        type Name = Name of string

        type Value = Value of float

        module Name =

            let create s = 
                if s = "" then "Name cannot be empty" 
                               |> List.singleton
                               |> Result.Error
                else s 
                     |> Name
                     |> Result.Ok

        module Value =

            let create v =
                if v < 0. then "Value must be greater than 0" 
                               |> List.singleton
                               |> Result.Error
                else v
                     |> Value
                     |> Result.Ok

        type Test = 
            {
                Name : Name
                Value1 : Value
                Value2 : Value
            }

        module Test =

            open Operators

            let create n v1 v2 =
                let f n v1 v2 =
                    {
                        Name = n
                        Value1 = v1
                        Value2 = v2
                    }

                f
                <!> Name.create n
                <*> Value.create v1
                <*> Value.create v2

            let run () =
                
                create "" -1. -1.
                |> printfn "%A"

