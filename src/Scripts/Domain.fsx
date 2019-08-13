#I __SOURCE_DIRECTORY__

#load "./../Extensions/List.fs"
#load "./../Extensions/Option.fs"
#load "./../Extensions/Result.fs"

#load "./../Domain/Types.fs"
#load "./../Domain/Domain.fs"

open System

open InformedICU.Extensions
open InformedICU.Domain.Types
open InformedICU.Domain

""
|> Patient.HospitalNumber.create

"123"
|> Patient.HospitalNumber.create

let dto = Patient.Dto.dto ()

dto.HospitalNumber <- "1"
dto.LastName <- "Test"
dto.FirstName <- "Test"
dto.BirthDate <- DateTime.Now |> Some

dto
|> Patient.register Patient.HospitalNumber.validate
                    Patient.Name.validate
                    Patient.BirthDate.validate

