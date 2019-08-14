#I __SOURCE_DIRECTORY__

#load "./../Extensions/List.fs"
#load "./../Extensions/Option.fs"
#load "./../Extensions/Result.fs"

#load "./../Domain/Types.fs"
#load "./../Domain/Domain.fs"

open System

open InformedICU.Extensions
open InformedICU.Extensions.Result.Operators
open InformedICU.Domain.Types
open InformedICU.Domain

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
        IsAdmitted = (fun _ -> true)
    }
    |> Patient.processCommand

[] |> Result.Ok
>>= processCommand (Validate dto)
>>= (processCommand (Register "1"))
//>>= (processCommand (Register "1"))
>>= (processCommand (Change dto))
