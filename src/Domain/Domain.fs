namespace InformedICU.Domain

open System

open InformedICU.Extensions
open InformedICU.Extensions.Result.Operators
open InformedICU.Domain.Types

module Patient =

    module HospitalNumber =
            
        open InformedICU.Domain.Types.HospitalNumber

        let create s = HospitalNumber s

        let validate : ValidateHospitalNumber = 
            fun s ->
                if s = "" then 
                    "Hospital number cannot be an empty string"
                    |> Invalid
                    |> List.singleton
                    |> Result.Error
                else 
                    s 
                    |> HospitalNumber
                    |> Valid
                    |> List.singleton
                    |> Result.Ok

        let toString (HospitalNumber s) = s

        let eventToHospitalNumber = function
            | Valid hn -> hn
            | Invalid err -> err |> exn |> raise

        let eqsString s (HospitalNumber n) = s = n


    module Name =

        open InformedICU.Domain.Types.Name

        let create s = Name s

        let validate : ValidateName =
            fun msg s ->
                if s = "" then 
                    sprintf "%s cannot be an empty string" msg
                    |> Invalid         
                    |> List.singleton
                    |> Result.Error
                else 
                    s 
                    |> Name
                    |> Valid
                    |> List.singleton
                    |> Result.Ok

        let toString (Name s) = s

        let eventToName = function
            | Valid n -> n
            | Invalid err -> err |> exn |> raise


    module BirthDate =

        open System
        open InformedICU.Domain.Types.BirthDate

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
                        |> Invalid
                        |> List.singleton
                        |> Result.Error 
                    | _ when age > (120. * 365.) ->
                        sprintf "A birthdate of %A results in an age > 120 years" dt
                        |> Invalid
                        |> List.singleton
                        |> Result.Error 
                    | _ -> 
                        bd
                        |> Valid
                        |> List.singleton
                        |> Result.Ok
                | None -> 
                    "Birthdate cannot be empty"
                    |> Invalid
                    |> List.singleton
                    |> Result.Error

        let toDate (BirthDate dt) = dt

        let eventToBirthDate = function
            | Valid bd -> bd
            | Invalid err  -> err |> exn |> raise  

    let create hn ln fn bd =
        {
            HospitalNumber = hn |> HospitalNumber.create
            LastName = ln |> Name.create
            FirstName = fn |> Name.create
            BirthDate = BirthDate.create bd
        }

    let register : RegisterPatient =
        fun vhn vnm vbd dto ->
            let f hne lne fne bde =
                let hn = List.head >> HospitalNumber.eventToHospitalNumber 
                let nm = List.head >> Name.eventToName
                let bd = List.head >> BirthDate.eventToBirthDate
                {
                    HospitalNumber = hne |> hn
                    LastName = lne |> nm
                    FirstName = fne |> nm
                    BirthDate = bde |> bd
                }
                |> Patient.Registered
                |> PatientEvent
                |> List.singleton
                |> List.append (bde |> List.map BirthDateEvent )
                |> List.append (fne |> List.map NameEvent )
                |> List.append (lne |> List.map NameEvent )
                |> List.append (hne |> List.map HospitalNumberEvent )

            f
            <!> (Result.mapErrorList HospitalNumberEvent (dto.HospitalNumber |> vhn))
            <*> (Result.mapErrorList NameEvent (dto.LastName |> vnm "Last name"))
            <*> (Result.mapErrorList NameEvent (dto.FirstName |> vnm "First name"))
            <*> (Result.mapErrorList BirthDateEvent (dto.BirthDate |> vbd))


    module Dto = 

        open InformedICU.Domain.Types.Patient

        let dto () = new Dto () 

        let toDto (pat : Patient) =
            let dto = dto ()
            dto.HospitalNumber <- pat.HospitalNumber |> HospitalNumber.toString
            dto.FirstName <- pat.FirstName |> Name.toString
            dto.LastName <- pat.LastName |> Name.toString
            dto.BirthDate <- pat.BirthDate |> BirthDate.toDate |> Some
            dto

        let fromDto (dto : Dto) =
            create dto.HospitalNumber
                   dto.LastName
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
                    dto.HospitalNumber
                    dto.LastName
                    dto.FirstName
                    ds