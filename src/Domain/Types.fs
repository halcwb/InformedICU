namespace InformedICU.Domain.Types

open System

type Errors = string list

type Description = string

type HospitalNumber = HospitalNumber of string

type Name = Name of string

type BirthDate = BirthDate of DateTime

type AdmissionDate = AdmissionDate of DateTime

type DischargeDate = DischargeDate of DateTime

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
        Patient : PatientDetails
    }

type PatientAdmission =
    {
        HospitalNumber : HospitalNumber
        AdmissionDate : AdmissionDate
    }

type PatientDischarge =
    {
        HospitalNumber : HospitalNumber
        DischargeDate : DischargeDate
    }

type ChangedDetails =
    {
        OldDetails : PatientDetails
        NewDetails : PatientDetails
    }

type Event =
    | Validated of PatientDetails
    | Registered of RegisteredPatient
    | Changed of ChangedDetails
    | Admitted of PatientAdmission
    | Discharged of PatientDischarge

type ValidateHospitalNumber = string -> Result<HospitalNumber, Errors>

type ValidateName = Description -> string -> Result<Name, Errors>

type ValidateBirthDate = DateTime Option -> Result<BirthDate, Errors>

type ValidateDetails = 
    ValidateName
        -> ValidateBirthDate
        -> PatientDetails.Dto 
        -> Result<PatientDetails, Errors>

type RegisterPatient = 
    ValidateHospitalNumber 
        -> string 
        -> PatientDetails 
        -> Result<RegisteredPatient, Errors>

type ChangeDetails = 
    ValidateDetails 
        -> PatientDetails.Dto
        -> PatientDetails 
        -> Result<ChangedDetails, Errors>

type AdmitPatient = 
    AdmissionDate 
        -> Result<PatientAdmission, Errors>

type DischargePatient = 
    DischargeDate 
        -> PatientAdmission 
        -> Result<PatientDischarge, Errors>

type EventResult = Result<Event list, Errors>

type Command =
    | Validate of PatientDetails.Dto
    | Register of string
    | Change of PatientDetails.Dto
    | Admit 
    | Discharge 

type ProcessCommand = Command -> Event list -> EventResult
