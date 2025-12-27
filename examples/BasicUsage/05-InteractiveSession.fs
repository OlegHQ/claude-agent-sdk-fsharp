/// Example 05: Interactive Session - Recursive REPL (no mutable state)
module Examples.InteractiveSession

open System
open System.Threading.Tasks
open FSharp.Control
open ClaudeAgentSdk
open Examples.Common

// ============================================================================
// Recursive Interactive Loop (Pure Functional)
// ============================================================================

let rec interactiveLoop (ctx: ClientContext) (logger: Logger) = task {
    printf "You> "
    let prompt = Console.ReadLine()

    match prompt with
    | null | "" | "exit" | "quit" ->
        Logger.info "Goodbye!" logger
        return ()

    | "/help" ->
        printfn ""
        printfn "Commands:"
        printfn "  exit, quit  - Exit the session"
        printfn "  /help       - Show this help"
        printfn "  /clear      - Clear screen"
        printfn "  <text>      - Send message to Claude"
        printfn ""
        return! interactiveLoop ctx logger

    | "/clear" ->
        Console.Clear()
        return! interactiveLoop ctx logger

    | prompt ->
        send (sprintf "Sending: %s" prompt)

        // Send and receive
        let! sendResult = Client.send prompt ctx

        match sendResult with
        | Error e ->
            logError e
            return! interactiveLoop ctx logger

        | Ok ctx ->
            // Collect messages
            let! result = Stream.collectMessages logger (Client.receive ctx)

            match result with
            | Ok messages ->
                // Display assistant text
                for text in Client.getAssistantText messages do
                    Console.ForegroundColor <- ConsoleColor.White
                    printfn "\n%s\n" text
                    Console.ResetColor()

                return! interactiveLoop ctx logger

            | Error e ->
                logError e
                return! interactiveLoop ctx logger
}

// ============================================================================
// Examples
// ============================================================================

let basicSession () = task {
    UI.banner "Interactive Session - Basic REPL"

    let logger = Logger.normal

    let options = {
        Options.defaults with
            SystemPrompt = Some (SystemPromptText "Be concise and helpful.")
    }

    let! connectResult = Client.connect options

    match connectResult with
    | Error e ->
        logError e
    | Ok ctx ->
        try
            printfn "Connected! Type your messages (type 'exit' to quit):"
            printfn ""
            do! interactiveLoop ctx logger
        finally
            Client.disconnect ctx |> Async.AwaitTask |> Async.RunSynchronously
}

let withEvents () = task {
    UI.banner "Interactive Session - With Event Logging"

    let logger = Logger.verbose

    // Create event bus for monitoring
    let bus = Events.createBus ()

    // Create event handler
    let eventHandler event =
        match event with
        | ToolUseStarted (name, _, _) ->
            debug (sprintf "[EVENT] Tool started: %s" name)
        | ThinkingStarted content ->
            let preview = if content.Length > 30 then content.[..29] + "..." else content
            debug (sprintf "[EVENT] Thinking: %s" preview)
        | _ -> ()

    let _subId = Events.subscribe eventHandler bus

    let options = {
        Options.defaults with
            Events = Some bus
            SystemPrompt = Some (SystemPromptText "Be helpful and think step by step.")
    }

    let! connectResult = Client.connect options

    match connectResult with
    | Error e ->
        logError e
    | Ok ctx ->
        try
            printfn "Connected with event logging!"
            printfn "Try: What is the capital of France?"
            printfn ""
            do! interactiveLoop ctx logger
        finally
            Client.disconnect ctx |> Async.AwaitTask |> Async.RunSynchronously
}

let runAll () = task {
    do! basicSession ()
    printfn ""
    UI.separator ()
    printfn ""

    printfn "Press Enter to try with event logging..."
    Console.ReadLine() |> ignore

    do! withEvents ()
}
