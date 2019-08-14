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
                    |> Result.errorList
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
                    |> Result.errorList
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
                        |> Result.errorList 
                    | _ -> 
                        bd
                        |> Result.Ok
                | None -> 
                    "Birthdate cannot be empty"
                    |> Result.errorList

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

    let getDetails es =
        es 
        |> List.fold (fun s e ->
            match e with
            | Validated pd -> Some pd
            | Changed pd ->
                pd.NewDetails |> Some
            | _ -> s
        ) None

    let hasDetails : HasDetails =
        fun es ->
            es 
            |> getDetails
            |> Option.isSome

    let isRegistered : IsRegistered =
        fun es ->
            es 
            |> List.fold (fun _ e ->
                match e with
                | Validated _ -> false
                | _ -> true
            ) false

    let registerPatient : RegisterPatient =
        fun hd ir vhn s es ->
            let f hn : RegisteredPatient =
                {
                    HospitalNumber = hn
                }
            es
            |> hd 
            |> function 
            | true -> () |> Result.Ok
            | false -> "patient has no details" |> Result.errorList
            >>= (fun _ -> 
                es 
                |> ir 
                |> function
                | true -> "patient is already registerd" |> Result.errorList
                | false -> () |> Result.Ok
            )
            >>= (fun _ ->
                (s |> vhn)
                |> Result.map f    
            )

    let changeDetails vn vb : ChangeDetails =
        fun hd vd dto es ->
            let f newd =
                {
                    NewDetails = newd
                }

            es
            |> hd
            |> function 
            | true -> () |> Result.Ok
            | false -> "patient has no details to change" |> Result.errorList
            >>= (fun _ ->
                (dto |> vd vn vb)
                |> Result.map f
            )

    let isAdmitted es =
        es
        |> List.fold (fun _ e ->
            match e with
            | Admitted _ -> true
            | _ -> false
        ) false


    let admitPatient : AdmitPatient =
        fun isr isa ad es  ->
            es
            |> isr
            |> function 
            | true -> () |> Result.Ok
            | false -> "cannot admit a patient that is not registered" |> Result.errorList
            >>= (fun _ ->
                if ad <= DateTime.Now then () |> Result.Ok
                else "the admission date cannot be in the futer" |> Result.errorList
            )
            >>= (fun _ ->
                es
                |> isa
                |> function 
                | true -> "patient is already admitted" |> Result.errorList
                | false -> () |> Result.Ok
            )
            >>= (fun _ ->
                {
                    AdmissionDate = ad
                } |> Result.Ok
            )

    let dischargeLaterThanAdmission : DischargeLaterThanAdmission =
        fun dd es ->
            es
            |> List.fold (fun s e ->
                match e with
                | Admitted d -> Some d.AdmissionDate
                | Discharged _ -> None
                | _ -> s
            ) None
            |> function
            | Some ad -> dd >= ad
            | _ -> false

    let dischargePatient : DischargePatient =
        fun isr isa dla dd es  ->
            es
            |> isr
            |> function 
            | true -> () |> Result.Ok
            | false -> "cannot discharge a patient that is not registered" |> Result.errorList
            >>= (fun _ ->
                if dd <= DateTime.Now then () |> Result.Ok
                else "the discharge date cannot be in the futer" |> Result.errorList
            )
            >>= (fun _ ->
                es
                |> isa
                |> function 
                | true -> () |> Result.Ok
                | false ->  "patient is not admitted" |> Result.errorList
            )
            >>= (fun _ ->
                es
                |> dla dd
                |> function 
                | true -> () |> Result.Ok
                | false -> "patient cannot be discharged before admission" |> Result.errorList
            )
            >>= (fun _ ->
                {
                    DischargeDate = dd
                } |> Result.Ok
            )


    let processCommand : ProcessCommand = 
        fun dep cmd es ->
            match cmd with
            | Validate dto ->
                dto
                |> validateDetails dep.ValidateName dep.ValidateBirthDate
                |> Result.map (Validated >> List.singleton)
            | Register s ->
                es
                |> registerPatient 
                    dep.HasDetails 
                    dep.IsRegistered
                    dep.ValidateHospitalNumber s
                |> Result.map (Registered >> List.singleton)
            | Change dto ->
                es
                |> changeDetails dep.ValidateName dep.ValidateBirthDate
                    hasDetails
                    validateDetails dto
                |> Result.map (Changed >> List.singleton)
            | Admit ad ->
                es
                |> admitPatient dep.IsRegistered dep.IsAdmitted ad
                |> Result.map (Admitted >> List.singleton)
            | Discharge dd ->
                es
                |> dischargePatient 
                    dep.IsRegistered 
                    dep.IsAdmitted
                    dep.DischargeLaterThanAdmission dd
                |> Result.map (Discharged >> List.singleton)
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