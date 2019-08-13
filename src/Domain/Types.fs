namespace InformedICU.Domain.Types

open System

type Description = string

type HospitalNumber = HospitalNumber of string

module HospitalNumber =

    type Event = 
        | Valid of HospitalNumber
        | Invalid of Description

type Name = Name of string

module Name =

    type Event =
        | Valid of Name
        | Invalid of Description

type BirthDate = BirthDate of DateTime

module BirthDate =

    type Event = 
        | Valid of BirthDate
        | Invalid of Description

[<NoComparison>]
type Patient =
    {
        HospitalNumber : HospitalNumber
        LastName : Name
        FirstName : Name
        BirthDate : BirthDate
    }

module Patient =

    type Dto () = 
        member val HospitalNumber  = "" with get, set
        member val LastName = "" with get, set
        member val FirstName = "" with get, set
        member val BirthDate : DateTime Option = None with get, set

    type Event =
        | Registered of Patient
        | AllReadyRegistered of HospitalNumber
        | Admitted of HospitalNumber
        | Discharged of HospitalNumber
        | Invalid of Description * Dto

type Event =
    | HospitalNumberEvent of HospitalNumber.Event
    | NameEvent of Name.Event
    | BirthDateEvent of BirthDate.Event
    | PatientEvent of Patient.Event

type EventResult<'Event> = Result<'Event list, 'Event list>

type ValidateHospitalNumber = 
    string -> EventResult<HospitalNumber.Event>

type ValidateName = 
    Description -> string -> EventResult<Name.Event>

type ValidateBirthDate = 
    DateTime option -> EventResult<BirthDate.Event>

type RegisterPatient = 
    ValidateHospitalNumber
        -> ValidateName 
            -> ValidateBirthDate
                -> Patient.Dto 
                    -> EventResult<Event>

