/// Scripting the entire ICU app
///
#I @"C:\"
#load ".paket/load/net472/main.group.fsx"

module Infrastructure =
    type Events<'Event> = 'Event list

    type EventProducer<'Event> = Events<'Event> -> Events<'Event>

    type EventStore<'Event> =
        { Get : unit -> Events<'Event>
          Append : Events<'Event> -> unit
          Evolve : EventProducer<'Event> -> unit }

    type Msg<'Event> =
        | Get of AsyncReplyChannel<Events<'Event>>
        | Append of Events<'Event>
        | Evolve of EventProducer<'Event>

    type Projection<'State, 'Event> =
        { Init : 'State
          Update : 'State -> 'Event -> 'State }

    module EventStore =
        let init() : EventStore<'Event> =
            let mailbox =
                MailboxProcessor.Start <| fun mailbox ->
                    let rec loop history =
                        async {
                            let! msg = mailbox.Receive()
                            match msg with
                            | Get channel ->
                                channel.Reply(history)
                                return! loop history
                            | Append events -> return! loop (history @ events)
                            | Evolve producer ->
                                return! loop (history @ producer history)
                        }
                    loop []
            { Get = fun () -> Get |> mailbox.PostAndReply
              Append = Append >> mailbox.Post
              Evolve = Evolve >> mailbox.Post }

    module Projection =
        let project (projection : Projection<_, _>) events =
            events |> List.fold projection.Update projection.Init

module Domain =
    open Infrastructure

    type Patient =
        { Id : string
          LastName : string
          FirstName : string }

    module Patient =
        let create id ln fn =
            { Id = id
              LastName = ln
              FirstName = fn }

        type Event =
            | Registered of Patient
            | Admitted of Patient
            | Discharged of Patient

        module Projections =
            let private updateRegistered state event =
                match event with
                | Registered pat -> state |> Map.add pat.Id pat
                | _ -> state

            let registered : Projection<Map<string, Patient>, Event> =
                { Init = Map.empty
                  Update = updateRegistered }

        module Behavior =
            let register id lastname firstname _ =
                create id lastname firstname
                |> Registered
                |> List.singleton

module App =
    open Infrastructure
    open Domain

    type private Id = string

    type private FirstName = string

    type LastName = string

    type private Msg =
        | GetEvents of AsyncReplyChannel<Events<Patient.Event>>
        | RegisterPatient of Id * FirstName * LastName
        | GetRegisteredPatients of AsyncReplyChannel<Map<string, Patient>>

    let private program =
        let store : EventStore<Patient.Event> = EventStore.init()
        MailboxProcessor.Start <| fun mailbox ->
            let rec loop (store : EventStore<Patient.Event>) =
                async {
                    let! msg = mailbox.Receive()
                    match msg with
                    | GetEvents channel ->
                        store.Get() |> channel.Reply
                        return! loop store
                    | RegisterPatient(id, fn, ln) ->
                        Patient.Behavior.register id ln fn |> store.Evolve
                        return! loop store
                    | GetRegisteredPatients channel ->
                        store.Get()
                        |> Projection.project Patient.Projections.registered
                        |> channel.Reply
                        return! loop store
                }
            loop store

    let getEvents() = GetEvents |> program.PostAndReply

    let registerPatient id lastName firstName =
        (id, lastName, firstName)
        |> RegisterPatient
        |> program.Post

    let getRegisteredPatients() = GetRegisteredPatients |> program.PostAndReply

module Tests =
    open Expecto
    open Domain

    module Patient =
        let tests =
            let pd = "1"
            let ln = "LastName"
            let fn = "FirstName"
            let pat = Patient.create pd ln fn
            testList "Creating a patient"
                [ test "the id" { Expect.equal pat.Id pd "should be 1" }

                  test "the last name"
                      { Expect.equal pat.LastName ln "should be LastName" }

                  test "the first name"
                      { Expect.equal pat.FirstName fn "should be FirstName" } ]

    let tests = testList "Domain tests" [ Patient.tests ]

module Helper =
    open Expecto

    let runTests() = Tests.tests |> runTests defaultConfig

    let printEvents events =
        printfn "Events:\n"
        events |> List.iter (printfn "- %A")

    let printRegisteredPatients patients =
        printfn "Registered patients:\n"
        patients |> Map.iter (printfn "- %A: %A")

// Enable for production [<EntryPoint>]
let main _ =
    printfn "Start of InformedICU"
    printfn "First start testing ..."
    let returnValue = Helper.runTests()
    // Get all patient events
    App.getEvents() |> Helper.printEvents
    // Register a patient
    App.registerPatient "1" "Test2" "Test2"
    // Get all registered patients
    App.getRegisteredPatients() |> Helper.printRegisteredPatients
    returnValue

main()
