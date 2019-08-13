namespace InformedICU.Domain

open System

open InformedICU.Extensions
open InformedICU.Extensions.Result.Operators
open InformedICU.Domain.Types

module Patient =

    module HospitalNumber =
            
        let create s = HospitalNumber s

        let validate : ValidateHospitalNumber = 
            fun s ->
                if s = "" then 
                    "Hospital number cannot be an empty string"
                    |> List.singleton
                    |> Result.Error
                else 
                    s 
                    |> HospitalNumber
                    |> Result.Ok

        let toString (HospitalNumber s) = s

        let eqsString s (HospitalNumber n) = s = n

    module Name =

        let create s = Name s

        let validate : ValidateName =
            fun msg s ->
                if s = "" then 
                    sprintf "%s cannot be an empty string" msg
                    |> List.singleton
                    |> Result.Error
                else 
                    s 
                    |> Name
                    |> Result.Ok

        let toString (Name s) = s


    module BirthDate =

        open System

        let currentAgeInDays (BirthDate bd) = 
            (DateTime.Now - bd).TotalDays

        let create dt = BirthDate dt
    
        let validate : ValidateBirthDate =
            fun dt -> 
                match dt with
                | Some dt ->
                    let bd = dt |> BirthDate
                    let age = bd |> currentAgeInDays
                    match age with
                    | _ when age < 0. ->
                        sprintf "A birthdate of %A results in a negative age" dt
                        |> List.singleton
                        |> Result.Error 
                    | _ when age > (120. * 365.) ->
                        sprintf "A birthdate of %A results in an age > 120 years" dt
                        |> List.singleton
                        |> Result.Error 
                    | _ -> 
                        bd
                        |> Result.Ok
                | None -> 
                    "Birthdate cannot be empty"
                    |> List.singleton
                    |> Result.Error

        let toDate (BirthDate dt) = dt

    let createDetails ln fn bd =
        {
            LastName = ln |> Name.create
            FirstName = fn |> Name.create
            BirthDate = BirthDate.create bd
        }

    let validateDetails : ValidateDetails =
        fun vnm vbd dto ->
            let f ln fn bd =
                {
                    LastName = ln
                    FirstName = fn
                    BirthDate = bd
                }

            f
            <!> (dto.LastName |> vnm "Last name")
            <*> (dto.FirstName |> vnm "First name")
            <*> (dto.BirthDate |> vbd)

    let private _validateDetails = 
        validateDetails Name.validate BirthDate.validate

    let registerPatient : RegisterPatient =
        fun vhn s pd ->
            let f hn ds : RegisteredPatient =
                {
                    HospitalNumber = hn
                    Patient = ds
                }

            f 
            <!> (s |> vhn)
            <*> (pd |> Result.Ok)

    let changeDetails : ChangeDetails =
        fun _ dto dts ->
            let f oldd newd =
                {
                    OldDetails = oldd
                    NewDetails = newd
                }

            f
            <!> (dts |> Result.Ok)
            <*> (dto |> _validateDetails)

    let private _changeDetails = changeDetails (fun _ _ -> _validateDetails)

    let lastEvent = List.rev >> List.tryHead

    let getDetails es =
        es 
        |> List.fold (fun s e ->
            match e with
            | Validated pd -> Some pd
            | Changed pd ->
                pd.NewDetails |> Some
            | _ -> s
        ) None

    let processCommand : ProcessCommand = 
        fun cmd es ->
            match cmd with
            | Validate dto ->
                dto
                |> _validateDetails
                |> Result.map (Validated >> List.singleton)
            | Register s ->
                match es |> lastEvent with
                | Some pat ->
                    match pat with
                    | Validated pd ->
                        pd
                        |> registerPatient HospitalNumber.validate s
                        |> Result.map (Registered >> List.singleton)
                    | _ ->
                        "the patient is already registered"
                        |> List.singleton
                        |> Result.Error
                | None -> 
                    "an unknown patient cannot be registered"
                    |> List.singleton
                    |> Result.Error
            | Change dto ->
                match es |> getDetails with
                | Some pd -> 
                    pd
                    |> _changeDetails dto
                    |> Result.map (Changed >> List.singleton)
                | None ->
                    "there are no patient details to change"
                    |> List.singleton
                    |> Result.Error
            | _ -> "cannot process command yet" |> exn |> raise

            |> Result.map (List.append es)
        

    module Dto = 

        open InformedICU.Domain.Types.PatientDetails

        let dto () = new Dto () 

        let toDto (pat : PatientDetails) =
            let dto = dto ()
            dto.FirstName <- pat.FirstName |> Name.toString
            dto.LastName <- pat.LastName |> Name.toString
            dto.BirthDate <- pat.BirthDate |> BirthDate.toDate |> Some
            dto

        let fromDto (dto : Dto) =
            createDetails dto.LastName
                          dto.FirstName
                          (dto.BirthDate |> Option.get)

        let toString (dto: Dto) =
            let ds = 
                dto.BirthDate 
                |> Option.bind (fun bd ->
                    bd.ToString("dd-mm-yyyy")
                    |> Some
                )
                |> Option.defaultValue ""
            sprintf "%s: %s, %s %s"
                    dto.LastName
                    dto.FirstName
                    ds