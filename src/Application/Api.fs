namespace InformedICU.Application.Api

module Patient =

    module Workflow =

        open InformedICU.Domain
        open InformedICU.Domain.Types
        open InformedICU.EventSourced

        let dependencies =
            {
                ValidateHospitalNumber = Patient.HospitalNumber.validate
                ValidateName = Patient.Name.validate
                ValidateBirthDate = Patient.BirthDate.validate
                HasDetails = Patient.hasDetails
                IsRegistered = Patient.isRegistered
                IsAdmitted = Patient.isAdmitted
                DischargeLaterThanAdmission = Patient.dischargeLaterThanAdmission
            }

        let processCommand : Workflow<Command, Event> = 
            fun cmd es ->
                Patient.processCommand dependencies cmd es
                |> function 
                | Ok es -> es
                | Error errs ->
                    errs
                    |> Errors
                    |> List.singleton


    module ReadModels =

        open InformedICU.Domain.Projections
        open InformedICU.Domain.Types
        open InformedICU.EventSourced

        type Query = 
            | GetRegistered
            | OnlyAdmitted
            | OnlyDischarged

        
        let patientInfo () : ReadModel<_, _> =
            let update state es =
                es
                |> List.fold 
                    (Projection.projectIntoMap 
                        Patient.empty
                        Patient.Projections.updatePatient)
                    state

            ReadModel.inMemory update Map.empty

        let query (getPatientInfo : unit -> Async<Patient.Patient list>) query =
            match query with
            | GetRegistered -> 
                async {
                    let! state = getPatientInfo ()
        
                    return 
                        state 
                        |> box
                        |> Handled
                }
            | OnlyAdmitted -> 
                async {
                    let! state = getPatientInfo ()

                    return 
                        state 
                        |> List.filter (fun p -> 
                            p.Admissions 
                            |> List.rev
                            |> List.tryHead
                            |> function 
                            | None -> true
                            | Some ad -> ad.DischargeDate |> Option.isNone
                        )
                        |> box
                        |> Handled
                }
            | OnlyDischarged -> 
                async {
                    let! state = getPatientInfo ()

                    return 
                        state 
                        |> List.filter (fun p -> 
                            p.Admissions 
                            |> List.rev
                            |> List.tryHead
                            |> function 
                            | None -> true
                            | Some ad -> ad.DischargeDate |> Option.isSome
                        )
                        |> box
                        |> Handled
                }


module App =

    open InformedICU.Extensions
    open InformedICU.EventSourced
    open Patient

    let init () =
        let model = ReadModels.patientInfo ()

        {
            EventStorageInit = fun () -> EventStorage.InMemoryStorage.initialize []
            EventStoreInit = fun storage -> storage |> EventStore.initialize
            CommandHandlerInit = CommandHandler.initialize Workflow.processCommand
            QueryHandler =
                QueryHandler.initialize
                    [
                        ReadModels.query (fun () ->
                            model.State ()
                            |> Async.map (fun map ->
                                map
                                |> Map.toList 
                                |> List.map (fun (id, pat) ->
                                    { pat with Id = id }
                                )
                            )
                        )
                    ]
            EventHandlers =
                [
                    model.EventHandler
                ]
        }
        |> EventSourced.fromConfig