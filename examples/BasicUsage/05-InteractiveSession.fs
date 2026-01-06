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
    TUI.prompt "You"
    let prompt = TUI.readLine ()

    match prompt with
    | null | "" | "exit" | "quit" ->
        Logger.info "Goodbye!" logger
        return ()

    | "/help" ->
        TUI.blank ()
        TUI.textLn "Commands:"
        TUI.textLn "  exit, quit  - Exit the session"
        TUI.textLn "  /help       - Show this help"
        TUI.textLn "  /clear      - Clear screen"
        TUI.textLn "  <text>      - Send message to Claude"
        TUI.blank ()
        return! interactiveLoop ctx logger

    | "/clear" ->
        TUI.clear ()
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
                    TUI.blank ()
                    TUI.whiteLn text
                    TUI.blank ()

                return! interactiveLoop ctx logger

            | Error e ->
                logError e
                return! interactiveLoop ctx logger
}

// ============================================================================
// Examples
// ============================================================================

let basicSession () = task {
    TUI.banner "Interactive Session - Basic REPL"

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
            TUI.textLn "Connected! Type your messages (type 'exit' to quit):"
            TUI.blank ()
            do! interactiveLoop ctx logger
        finally
            Client.disconnect ctx |> Async.AwaitTask |> Async.RunSynchronously
}

let withEvents () = task {
    TUI.banner "Interactive Session - With Event Logging"

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
            TUI.textLn "Connected with event logging!"
            TUI.textLn "Try: What is the capital of France?"
            TUI.blank ()
            do! interactiveLoop ctx logger
        finally
            Client.disconnect ctx |> Async.AwaitTask |> Async.RunSynchronously
}

let runAll () = task {
    do! basicSession ()
    TUI.blank ()
    TUI.separator ()
    TUI.blank ()

    TUI.textLn "Press Enter to try with event logging..."
    TUI.readLine () |> ignore

    do! withEvents ()
}
