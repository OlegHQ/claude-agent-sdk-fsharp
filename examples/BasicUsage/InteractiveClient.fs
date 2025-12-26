/// Example: Interactive client with multiple turns and interrupt
module Examples.InteractiveClient

open System
open System.Threading.Tasks
open FSharp.Control
open ClaudeAgentSdk
open Examples.Logging

/// Basic interactive session - multiple back-and-forth messages
let interactiveSession () = task {
    printfn "=== Interactive Session Example ==="
    printfn ""

    let options = {
        Options.defaults with
            SystemPrompt = Some (SystemPromptText "You are a helpful coding assistant. Be concise.")
    }

    info "Connecting to Claude..."

    let! connectResult = verboseConnect options

    match connectResult with
    | Error e ->
        logError e

    | Ok ctx ->
        try
            // First message
            info "Sending first message..."
            printfn ""
            printfn "> What is a closure in programming?"
            let! queryResult = streamingClientQuery "What is a closure in programming?" ctx
            match queryResult with
            | Ok (ctx, msgs) ->
                printfn ""
                Console.ForegroundColor <- ConsoleColor.Blue
                Console.WriteLine("Claude:")
                Console.ResetColor()
                for msg in Client.getAssistantText msgs do
                    printfn "  %s" msg

                // Follow-up question (context is maintained)
                printfn ""
                info "Sending follow-up message..."
                printfn "> Can you show me an example in F#?"
                let! queryResult2 = streamingClientQuery "Can you show me an example in F#?" ctx
                match queryResult2 with
                | Ok (ctx, msgs) ->
                    printfn ""
                    Console.ForegroundColor <- ConsoleColor.Blue
                    Console.WriteLine("Claude:")
                    Console.ResetColor()
                    for msg in Client.getAssistantText msgs do
                        printfn "  %s" msg

                    // Another follow-up
                    printfn ""
                    info "Sending third message..."
                    printfn "> How is that different from a regular function?"
                    let! queryResult3 = streamingClientQuery "How is that different from a regular function?" ctx
                    match queryResult3 with
                    | Ok (_, msgs) ->
                        printfn ""
                        Console.ForegroundColor <- ConsoleColor.Blue
                        Console.WriteLine("Claude:")
                        Console.ResetColor()
                        for msg in Client.getAssistantText msgs do
                            printfn "  %s" msg
                    | Error e -> logError e
                | Error e -> logError e
            | Error e -> logError e
        finally
            info "Disconnecting..."
            Client.disconnect ctx |> Async.AwaitTask |> Async.RunSynchronously
            success "Session ended."
}

/// Demonstrate interrupt functionality
let interruptExample () = task {
    printfn ""
    printfn "=== Interrupt Example ==="
    printfn ""

    let options = Options.defaults

    info "Connecting..."
    let! connectResult = verboseConnect options

    match connectResult with
    | Error e ->
        logError e

    | Ok ctx ->
        try
            // Start a long-running task
            info "Starting a long task (will interrupt after 2 seconds)..."
            send "Prompt: Write a very long detailed essay about the history of computing from 1800 to 2024"

            let! sendResult = Client.send "Write a very long detailed essay about the history of computing from 1800 to 2024" ctx

            match sendResult with
            | Error e ->
                logError e
            | Ok ctx ->
                // Start receiving in background
                let messageCount = ref 0
                let receiveTask = task {
                    for result in Client.receive ctx do
                        match result with
                        | Ok msg ->
                            incr messageCount
                            if !messageCount <= 5 then
                                logMessage msg
                            elif !messageCount = 6 then
                                debug "... (suppressing further messages)"
                        | Error e ->
                            logError e
                }

                // Wait 2 seconds then interrupt
                do! Task.Delay(2000)
                warn "Sending interrupt..."
                let! interruptResult = Client.interrupt ctx

                match interruptResult with
                | Ok () -> success "Interrupt sent successfully"
                | Error e -> logError e

                // Wait for receive to finish
                do! receiveTask
                info $"Total messages received: {!messageCount}"
        finally
            info "Disconnecting..."
            Client.disconnect ctx |> Async.AwaitTask |> Async.RunSynchronously
            success "Session ended."
}

/// Demonstrate permission mode changes during session
let permissionModeExample () = task {
    printfn ""
    printfn "=== Permission Mode Example ==="
    printfn ""

    let options = {
        Options.defaults with
            PermissionMode = Some Default
    }

    info "Connecting with Default permission mode..."
    let! connectResult = verboseConnect options

    match connectResult with
    | Error e ->
        logError e

    | Ok ctx ->
        try
            // Change to AcceptEdits mode
            info "Changing to AcceptEdits mode..."
            let! result = Client.setPermissionMode AcceptEdits ctx
            match result with
            | Ok () -> success "Permission mode changed to AcceptEdits"
            | Error e -> logError e

            // Now file edits will be auto-accepted
            info "Sending query that may edit files..."
            let! queryResult = verboseClientQuery "Create a file called test.txt with 'Hello World'" ctx
            match queryResult with
            | Ok (ctx, msgs) ->
                success $"Task completed with {List.length msgs} messages"

                // Change to Plan mode
                info "Changing to Plan mode..."
                let! result = Client.setPermissionMode Plan ctx
                match result with
                | Ok () -> success "Permission mode changed to Plan"
                | Error e -> logError e
            | Error e -> logError e
        finally
            info "Disconnecting..."
            Client.disconnect ctx |> Async.AwaitTask |> Async.RunSynchronously
}

/// Demonstrate model switching during session
let modelSwitchExample () = task {
    printfn ""
    printfn "=== Model Switch Example ==="
    printfn ""

    let options = {
        Options.defaults with
            Model = Some "claude-sonnet-4-20250514"
    }

    info "Connecting with Sonnet model..."
    let! connectResult = verboseConnect options

    match connectResult with
    | Error e ->
        logError e

    | Ok ctx ->
        try
            // Quick question with Sonnet
            info "Quick question with Sonnet..."
            let! queryResult = verboseClientQuery "What is 2+2?" ctx
            match queryResult with
            | Ok (ctx, _) ->
                // Switch to Opus for complex task
                info "Switching to Opus for complex reasoning..."
                let! result = Client.setModel (Some "claude-opus-4-20250514") ctx
                match result with
                | Ok () -> success "Model switched to Opus"
                | Error e -> logError e

                // Complex task with Opus
                info "Complex question with Opus..."
                let! queryResult2 = verboseClientQuery "Explain the P vs NP problem in one sentence" ctx
                match queryResult2 with
                | Ok (ctx, msgs) ->
                    for t in Client.getAssistantText msgs do
                        printfn "  %s" t

                    // Switch to Haiku for simple tasks
                    info "Switching to Haiku for simple task..."
                    let! result = Client.setModel (Some "claude-haiku-3-5-20241022") ctx
                    match result with
                    | Ok () -> success "Model switched to Haiku"
                    | Error e -> logError e
                | Error e -> logError e
            | Error e -> logError e
        finally
            info "Disconnecting..."
            Client.disconnect ctx |> Async.AwaitTask |> Async.RunSynchronously
}

/// Session with file checkpointing and rewind
let rewindExample () = task {
    printfn ""
    printfn "=== Rewind Files Example ==="
    printfn ""

    let options = {
        Options.defaults with
            EnableFileCheckpointing = true
    }

    info "Connecting with file checkpointing enabled..."
    let! connectResult = verboseConnect options

    match connectResult with
    | Error e ->
        logError e

    | Ok ctx ->
        try
            // Make some file changes
            info "Making file changes..."
            let! queryResult = verboseClientQuery "Create a file called experiment.txt with some content" ctx

            match queryResult with
            | Ok (ctx, msgs) ->
                // Get the user message ID for potential rewind
                let userMsgId =
                    msgs
                    |> List.tryPick (function
                        | UserMsg m -> m.Uuid
                        | _ -> None)

                match userMsgId with
                | Some msgId ->
                    debug $"User message ID: {msgId}"

                    // Make more changes
                    info "Making more changes..."
                    let! queryResult2 = verboseClientQuery "Now modify experiment.txt to add more content" ctx
                    match queryResult2 with
                    | Ok (ctx, _) ->
                        // Rewind to before the second change
                        warn "Rewinding files to state before second change..."
                        let! result = Client.rewindFiles msgId ctx
                        match result with
                        | Ok () -> success "Files rewound successfully"
                        | Error e -> logError e
                    | Error e -> logError e

                | None ->
                    warn "No user message ID found for rewind"
            | Error e -> logError e
        finally
            info "Disconnecting..."
            Client.disconnect ctx |> Async.AwaitTask |> Async.RunSynchronously
}

/// Run all interactive examples
let runAll () = task {
    info "Starting InteractiveClient examples..."
    printfn ""

    do! interactiveSession ()

    printfn ""
    printfn "----------------------------------------"
    printfn ""

    do! interruptExample ()

    printfn ""
    printfn "----------------------------------------"
    printfn ""

    do! permissionModeExample ()

    printfn ""
    printfn "----------------------------------------"
    printfn ""

    do! modelSwitchExample ()

    printfn ""
    printfn "----------------------------------------"
    printfn ""

    do! rewindExample ()

    printfn ""
    success "InteractiveClient examples completed!"
}
