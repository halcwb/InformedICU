/// Scripting the entire ICU app
///
#I __SOURCE_DIRECTORY__
#load ".paket/load/net471/main.group.fsx"

module Agent =

    /// A wrapper for MailboxProcessor that catches all unhandled exceptions
    /// and reports them via the 'OnError' event. Otherwise, the API
    /// is the same as the API of 'MailboxProcessor'
    type Agent<'T>(f:Agent<'T> -> Async<unit>) as self =
        // Create an event for reporting errors
        let errorEvent = Event<_>()
        // Start standard MailboxProcessor
        let inbox = new MailboxProcessor<'T>(fun _ ->
            async {
              // Run the user-provided function & handle exceptions
              try return! f self
              with e -> errorEvent.Trigger(e)
            })

        /// Triggered when an unhandled exception occurs
        member __.OnError = errorEvent.Publish

        member __.Trigger exn = errorEvent.Trigger exn

        /// Starts the mailbox processor
        member __.Start() = inbox.Start()
        /// Receive a message from the mailbox processor
        member __.Receive() = inbox.Receive()
        /// Post a message to the mailbox processor
        member __.Post(value:'T) = inbox.Post value

        member __.PostAndReply(f: AsyncReplyChannel<'a> -> 'T) = inbox.PostAndReply f

        member __.PostAndAsyncReply(f: AsyncReplyChannel<'a> -> 'T) = inbox.PostAndAsyncReply f

        /// Start the mailbox processor
        static member Start f =
            let agent = new Agent<_>(f)
            agent.Start()
            agent


module Infrastructure =



    /// A list of events represents a history of 
    /// of events that happened in the domain and 
    /// that belong together
    type Events<'Event> = 'Event list

    /// An `EventProducer` represents a workflow that
    /// based on previous events produces new events
    type EventProducer<'Event> = Events<'Event> -> Events<'Event>

    /// An `EventSource` is the combination of an event
    /// and the source of the event
    type EventSource<'Source, 'Event> =
        { Source : 'Source 
          Event: 'Event }

    /// `EventSources` is the total of all events in the domain
    type EventSources<'Source, 'Event> = EventSource<'Source, 'Event> list

    /// The `EventStore` takes care of adding events and retrieving events
    type EventStore<'Source, 'Event> =
        { Get : unit -> EventSources<'Source, 'Event>
          GetStream : 'Source -> Events<'Event>
          Append : 'Source -> Events<'Event> -> unit
          Evolve : 'Source -> EventProducer<'Event> -> unit }

    /// The different kind of messages that can be send to the `EventStore`
    /// to:
    /// - Get all events
    /// - Get events for a source
    /// - Append events for a source
    /// - Run the `EventProducer` for a source
    type Msg<'Source, 'Event> =
        | Get of AsyncReplyChannel<EventSources<'Source, 'Event>>
        | GetStream of 'Source * AsyncReplyChannel<Events<'Event>>
        | Append of 'Source * Events<'Event>
        | Evolve of 'Source * EventProducer<'Event>

    /// Project an `Event` to a `State`.
    /// Starts with an initial state and updates 
    /// the state for each additional event.
    type Projection<'State, 'Event> =
        { Init : 'State
          Update : 'State -> 'Event -> 'State }
          

    module EventStore =

        let private createEventSource source event = { Source = source; Event = event }

        let private appendEventSource source = List.map (createEventSource source)

        let private eventsOfSource source history =
            history
            |> List.filter(fun { Source = source' } -> source = source')
            |> List.map (fun { Event = event } -> event)

        let private getStream source channel = (source, channel) |> GetStream
        
        /// Initializes the `EventStore`.
        let init() : EventStore<'Source, 'Event> =
            let mailbox =
                MailboxProcessor.Start <| fun mailbox ->
                    let rec loop history =
                        async {
                            let! msg = mailbox.Receive()
                            match msg with
                            | Get channel ->
                                channel.Reply(history)
                                return! loop history
                            | GetStream (source, channel) ->
                                history
                                |> eventsOfSource source
                                |> channel.Reply
                                return! loop history
                            | Append (source, events) -> 
                                let append =
                                    events 
                                    |> (appendEventSource source)
                                return! loop (history @ append)
                            | Evolve (source, producer) ->
                                let append = 
                                    history
                                    |> eventsOfSource source
                                    |> producer
                                    |> (appendEventSource source)

                                return! loop (history @ append)
                        }
                    loop []
            { Get = fun () -> Get |> mailbox.PostAndReply
              GetStream = getStream >> mailbox.PostAndReply
              Append = (fun source events -> (source, events) |> Append |> mailbox.Post)
              Evolve = (fun source producer -> (source, producer) |> Evolve |> mailbox.Post) }

    module Producer =
        let inline mapProducer matcher mapper producer events =
            events 
            |> List.map matcher
            |> List.filter Option.isSome
            |> List.map Option.get
            |> producer 
            |> List.map mapper



    module Projection =
        let project (projection : Projection<_, _>) events =
            events |> List.fold projection.Update projection.Init

        let inline mapProjection matcher project projection events =
            events 
            |> List.map matcher
            |> List.filter Option.isSome
            |> List.map Option.get
            |> project projection

module Domain =
    open Infrastructure

    type Id = string

    type FirstName = string

    type LastName = string

    [<NoComparison; NoEquality>]
    type Patient =
        { Id : Id
          LastName : FirstName
          FirstName : LastName }
          
    module Patient =

        type Event =
            | Registered of Patient
            | NotFound of Id
            | Admitted of Id
            | Discharged of Id

        let create id ln fn =
            { Id = id
              LastName = ln
              FirstName = fn }

        let apply f (pat: Patient) = pat |> f

        let get = apply id

        let equals pat1 pat2 = (pat1 |> get).Id = (pat2 |> get).Id

        let toString { Id = id; LastName = ln; FirstName = fn } =
            sprintf "%s: %s, %s" id ln fn

        module Projections =
            let private updateRegistered state event =
                match event with
                | Registered pat -> state |> Map.add pat.Id pat
                | _ -> state

            let registered : Projection<Map<string, Patient>, Event> =
                { Init = Map.empty
                  Update = updateRegistered }

            let private updateAdmitted events state event =
                let registered = 
                    events
                    |> Projection.project registered
                    
                match event with
                | Admitted id -> 
                    registered 
                    |> Map.tryFind id 
                    |> Option.map (fun pat -> state |> Map.add id pat)
                    |> Option.defaultValue state
                | Discharged id ->
                    state
                    |> Map.remove id
                | _ -> state

            let admitted events : Projection<Map<string, Patient>, Event> =
                { Init = Map.empty
                  Update = updateAdmitted events }
                              

        module Behavior =
            open Projections

            let register id lastname firstname _ =
                create id lastname firstname
                |> Registered
                |> List.singleton

            let admit id events =
                events
                |> Projection.project registered
                |> Map.tryFind id
                |> Option.map (fun _ -> Admitted id)
                |> Option.defaultValue (NotFound id)
                |> List.singleton

            let discharge id events =
                events
                |> Projection.project registered
                |> Map.tryFind id
                |> Option.map (fun _ -> Discharged id)
                |> Option.defaultValue (NotFound id)
                |> List.singleton


    type Event = | PatientEvent of Patient.Event

    let mapEvent c e = e |> c

    let mapPatientEvent = mapEvent PatientEvent

    let matchPatientEvent event = 
        match event with
        | PatientEvent e -> Some e
        | _ -> None
        
    let mapPatientProducer =
        Producer.mapProducer matchPatientEvent mapPatientEvent
        
    let mapPatientProjection = Projection.mapProjection matchPatientEvent


module App =
    open Infrastructure
    open Domain
    
    type Source = string

    type private Msg =
        | GetEvents of AsyncReplyChannel<EventSources<Source, Event>>
        | RegisterPatient of Id * FirstName * LastName
        | AdmitPatient of Id
        | DischargePatient of Id
        | GetRegisteredPatients of AsyncReplyChannel<Map<string, Patient>>
        | GetAdmittedPatients of AsyncReplyChannel<Map<string, Patient>>

    let private program =
        let store : EventStore<Source, Domain.Event> = EventStore.init()
        MailboxProcessor.Start <| fun mailbox ->
            let rec loop store =
                async {
                    let! msg = mailbox.Receive()
                    match msg with
                    | GetEvents channel ->
                        store.Get() |> channel.Reply
                        return! loop store
                    | RegisterPatient(id, fn, ln) ->
                        Patient.Behavior.register id ln fn 
                        |> mapPatientProducer
                        |> store.Evolve "ICU"
                        return! loop store
                    | GetRegisteredPatients channel ->
                        store.GetStream "ICU"
                        |> (mapPatientProjection Projection.project Patient.Projections.registered)
                        |> channel.Reply
                        return! loop store
                    | AdmitPatient id ->
                        Patient.Behavior.admit id 
                        |> mapPatientProducer
                        |> store.Evolve "ICU"
                        return! loop store
                    | GetAdmittedPatients channel ->
                        let events = store.GetStream "ICU"
                        let admitted =
                            events
                            |> List.map (fun event ->
                                match event with
                                | PatientEvent e -> Some e
                                | _ -> None
                            )
                            |> List.filter Option.isSome
                            |> List.map Option.get
                            |> Patient.Projections.admitted

                        events
                        |> (mapPatientProjection Projection.project admitted) 
                        |> channel.Reply
                        return! loop store
                    | DischargePatient id ->
                        Patient.Behavior.discharge id 
                        |> mapPatientProducer
                        |> store.Evolve "ICU"
                        return! loop store
                }
            loop store

    let getEvents() = GetEvents |> program.PostAndReply

    let registerPatient id lastName firstName =
        (id, lastName, firstName)
        |> RegisterPatient
        |> program.Post

    let getRegisteredPatients() = GetRegisteredPatients |> program.PostAndReply

    let admitPatient = AdmitPatient >> program.Post

    let getAdmittedPatiens () = GetAdmittedPatients |> program.PostAndReply

    let dischargePatient = DischargePatient >> program.Post

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
    open Domain

    let runTests() = Tests.tests |> runTests defaultConfig

    let printEvents events =
        printfn "Events:\n"
        events |> List.iter (printfn "- %A")

    let printPatients patients =
        printfn "Registered patients:\n"
        patients
        |> Map.iter (fun _ pat ->
               pat
               |> Patient.toString
               |> printfn "%s")
              
let main _ =
    printfn "Start of InformedICU"
    printfn "First start testing ..."
    let returnValue = Helper.runTests()
    printfn "Then run the app"

    App.registerPatient "1" "Test" "Test"
    App.getEvents () |> Helper.printEvents
    App.getRegisteredPatients () |> Helper.printPatients
    App.admitPatient "1"
    App.getEvents () |> Helper.printEvents
    App.getAdmittedPatiens () |> Helper.printPatients
    App.dischargePatient "2"
    App.getEvents () |> Helper.printEvents
    App.getAdmittedPatiens () |> Helper.printPatients

    returnValue

main()
