#I __SOURCE_DIRECTORY__

#load "./../Extensions/List.fs"
#load "./../Extensions/Option.fs"
#load "./../Extensions/Result.fs"

#load "./../Domain/Types.fs"
#load "./../Domain/Implement.fs"

#load "./../Api/Patient.fs"

open System

open InformedICU.Extensions
open InformedICU.Extensions.Result.Operators
open InformedICU.Domain.Types
open InformedICU.Domain
open InformedICU.Api

""
|> Patient.HospitalNumber.create

"123"
|> Patient.HospitalNumber.create

let dto = Patient.Dto.dto ()

dto.LastName <- "Test"
dto.FirstName <- "Test"
dto.BirthDate <- DateTime.Now |> Some

dto
|> Patient.validateDetails Patient.Name.validate
                           Patient.BirthDate.validate

let processCommand =
    {
        ValidateHospitalNumber = Patient.HospitalNumber.validate
        ValidateName = Patient.Name.validate
        ValidateBirthDate = Patient.BirthDate.validate
        HasDetails = Patient.hasDetails
        IsRegistered = Patient.isRegistered
        IsAdmitted = Patient.isAdmitted
        DischargeLaterThanAdmission = Patient.dischargeLaterThanAdmission
    }
    |> Patient.processCommand

[] |> Result.Ok
>>= processCommand (Validate dto)
>>= (processCommand (Register "1"))
>>= (fun es ->
    dto.FirstName <- "Walter"
    dto.LastName <- "Goodman"

    es |> Result.Ok
)
>>= (processCommand (Change dto))
>>= (processCommand (Admit (DateTime.Now.AddDays(-10.))))
>>= (processCommand (Discharge (DateTime.Now.AddDays(-5.))))
>>= (processCommand (Admit (DateTime.Now.AddDays(-1.))))
>>= (fun es ->
    es
    |> Patient.Projections.patientInfo
    |> printfn "%A"

    () |> Result.Ok
)




