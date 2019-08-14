namespace InformedICU.Domain.Types

open System

type Errors = string list

type Description = string

type HospitalNumber = HospitalNumber of string

type Name = Name of string

type BirthDate = BirthDate of DateTime

type AdmissionDate = DateTime

type DischargeDate = DateTime

[<NoComparison>]
type PatientDetails =
    {
        LastName : Name
        FirstName : Name
        BirthDate : BirthDate
    }

module PatientDetails =
    
    type Dto () =
        member val LastName = "" with get, set
        member val FirstName = "" with get, set
        member val BirthDate : DateTime option = None with get, set

type RegisteredPatient =
    {
        HospitalNumber : HospitalNumber
    }

type PatientAdmission =
    {
        AdmissionDate : AdmissionDate
    }

type PatientDischarge =
    {
        DischargeDate : DischargeDate
    }

type ChangedDetails =
    {
        NewDetails : PatientDetails
    }

type Event =
    | Validated of PatientDetails
    | Registered of RegisteredPatient
    | Changed of ChangedDetails
    | Admitted of PatientAdmission
    | Discharged of PatientDischarge
    | Errors of Description list

type ValidateHospitalNumber = string -> Result<HospitalNumber, Errors>

type ValidateName = Description -> string -> Result<Name, Errors>

type ValidateBirthDate = DateTime Option -> Result<BirthDate, Errors>

type ValidateDetails = 
    ValidateName
        -> ValidateBirthDate
        -> PatientDetails.Dto 
        -> Result<PatientDetails, Errors>

type HasDetails = Event list -> bool

type IsRegistered = Event list -> bool

type RegisterPatient = 
    HasDetails 
        -> IsRegistered
        -> ValidateHospitalNumber 
        -> string 
        -> Event list
        -> Result<RegisteredPatient, Errors>

type ChangeDetails = 
    HasDetails
        -> ValidateDetails 
        -> PatientDetails.Dto
        -> Event list
        -> Result<ChangedDetails, Errors>

type IsAdmitted = Event list -> bool

type DischargeLaterThanAdmission = 
    DischargeDate -> Event list -> bool

type AdmitPatient = 
    IsRegistered
        -> IsAdmitted 
        -> AdmissionDate 
        -> Event list
        -> Result<PatientAdmission, Errors>

type DischargePatient = 
    IsRegistered 
        -> IsAdmitted
        -> DischargeLaterThanAdmission
        -> DischargeDate 
        -> Event list
        -> Result<PatientDischarge, Errors>

type EventResult = Result<Event list, Errors>

type Command =
    | Validate of PatientDetails.Dto
    | Register of string
    | Change of PatientDetails.Dto
    | Admit of AdmissionDate
    | Discharge of DischargeDate

type Dependencies =
    {
        ValidateHospitalNumber : ValidateHospitalNumber
        ValidateName : ValidateName
        ValidateBirthDate : ValidateBirthDate
        HasDetails : HasDetails
        IsRegistered : IsRegistered
        IsAdmitted : IsAdmitted
        DischargeLaterThanAdmission : DischargeLaterThanAdmission
    }

type ProcessCommand = Dependencies -> Command -> Event list -> EventResult
