#I __SOURCE_DIRECTORY__

#time

#load "./../Agent/Agent.fs"

open InformedICU.Agent

let agent = Agent.start <| fun inbox ->
    let rec loop () =
        async { 
            let! msg = inbox.Receive()
            if msg = "HelloWorld" then 
                printfn  "Hello"
            else ()
            return! loop ()
        }

    loop ()


"HelloWorld"
|> agent.Post

let agentWithLogger =
    Logger.logPerf "Hello World"
    |> Agent.startWithLogging <| fun inbox ->
        let rec loop () =
            async { 
                let! msg = inbox.Receive()
                if msg = "HelloWorld" then 
                    printfn  "Hello"
                else ()
                return! loop ()
            }
    
        loop ()

for _ in [1..10] do
    "HelloWorld"
    |> agentWithLogger.Post
