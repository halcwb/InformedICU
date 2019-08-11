namespace InformedICU.Agent

open System
open System.Diagnostics

type Logger = string -> Stopwatch -> obj Option -> unit

module Logger =

    let noLog : Logger = fun _ _ _ -> ()
    
    let logPerf header : Logger =
        fun s t _ -> 
            printfn "%s" header
            printfn "%s: %f" s (t.Elapsed.TotalSeconds)

    let logPrint header : Logger =
        fun s t o -> 
            printfn "header"
            printfn "%s: %f" s (t.Elapsed.TotalSeconds)
            printfn "%A" o


/// A wrapper for MailboxProcessor that catches all unhandled exceptions
/// and reports them via the 'OnError' event. Otherwise, the API
/// is the same as the API of 'MailboxProcessor'
type Agent<'T>(f: Agent<'T> -> Async<unit>, logger : Logger) as self =

    let timer = Stopwatch.StartNew ()

    let logAsync s x =
        timer.Restart ()
        async { 
            let! v = x
            do 
                v
                |> box
                |> Some
                |> logger s timer 
            return v }
        
    
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
    member __.Start() =
        timer.Restart ()
        logger "start"  timer None
        inbox.Start()

    /// Receive a message from the mailbox processor
    member __.Receive() = 
            inbox.Receive()
            |> logAsync "receive"

    /// Post a message to the mailbox processor
    member __.Post(value:'T) = 
        timer.Restart ()

        box value
        |> Some
        |> logger "post" timer

        inbox.Post value
 
    member __.PostAndReply(f: AsyncReplyChannel<'a> -> 'T) = 
        inbox.PostAndReply f

    member __.PostAndAsyncReply(f: AsyncReplyChannel<'a> -> 'T) = 
        inbox.PostAndAsyncReply f
        |> logAsync "reply"


module Agent =

    /// Start the mailbox processor
    let start f =
        let agent = new Agent<_>(f, fun _ _ _ -> ())
        agent.Start()
        agent

    /// Start the mailbox processor
    let startWithLogging log f =
        let agent = new Agent<_>(f, log)
        agent.Start()
        agent


