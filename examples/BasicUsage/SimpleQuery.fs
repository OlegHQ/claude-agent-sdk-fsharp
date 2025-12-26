/// Example: Simple one-shot query with verbose logging
module Examples.SimpleQuery

open System
open FSharp.Control
open ClaudeAgentSdk
open Examples.Logging

/// Basic one-shot query - simplest usage
let basicQuery () = task {
    printfn "=== Basic Query Example ==="
    printfn ""

    // Use defaults - will use Claude Code's default tools and settings
    let options = Options.defaults

    info "Executing basic query..."

    // Execute query with verbose logging
    let! result = verboseQuery "What is 2 + 2? Reply with just the number." options

    match result with
    | Ok messages ->
        let text = messages |> List.tryPick (function
            | ResultMsg r -> r.Result
            | _ -> None)
        match text with
        | Some t -> success $"Final result: {t}"
        | None ->
            // Get text from assistant messages
            let assistantText = Client.getAssistantText messages
            if not (List.isEmpty assistantText) then
                for t in assistantText do
                    success $"Assistant: {t}"
            else
                warn "No text result returned"
    | Error e ->
        logError e
}

/// Query with streaming - see messages as they arrive
let streamingQuery () = task {
    printfn ""
    printfn "=== Streaming Query Example ==="
    printfn ""

    let options = Options.defaults

    info "Starting streaming query..."
    logOptions options
    send "Prompt: Write a haiku about programming"

    let mutable messageCount = 0

    // Stream messages as they arrive using TaskSeq
    for result in query "Write a haiku about programming" options do
        messageCount <- messageCount + 1
        match result with
        | Ok msg ->
            logMessage msg
            match msg with
            | AssistantMsg m ->
                for block in m.Content do
                    match block with
                    | Text t ->
                        Console.ForegroundColor <- ConsoleColor.White
                        printf "%s" t
                        Console.ResetColor()
                    | _ -> ()
            | ResultMsg result ->
                printfn ""
                success $"Completed in {result.DurationMs}ms, cost: ${result.TotalCostUsd |> Option.defaultValue 0.0}"
            | _ -> ()
        | Error e ->
            logError e

    info $"Total messages received: {messageCount}"
}

/// Query with custom options
let customOptionsQuery () = task {
    printfn ""
    printfn "=== Custom Options Query Example ==="
    printfn ""

    let options = {
        Options.defaults with
            // Use specific model
            Model = Some "claude-sonnet-4-20250514"

            // Custom system prompt
            SystemPrompt = Some (SystemPromptText "You are a helpful math tutor. Be concise.")

            // Limit turns to prevent runaway conversations
            MaxTurns = Some 5

            // Set a budget limit
            MaxBudgetUsd = Some 0.10

            // Restrict available tools
            Tools = Some (ToolsList ["Read"; "Bash"])
    }

    info "Executing query with custom options..."

    let! result = verboseQuery "Calculate the factorial of 5" options

    match result with
    | Ok messages ->
        let assistantText = Client.getAssistantText messages
        for t in assistantText do
            success $"Result: {t}"
    | Error e ->
        logError e
}

/// Query with structured output (JSON schema)
let structuredOutputQuery () = task {
    printfn ""
    printfn "=== Structured Output Query Example ==="
    printfn ""

    // Define the expected output schema
    let schema = Mcp.Schema.object' [
        Mcp.Schema.required "answer" Mcp.Schema.number
        Mcp.Schema.required "explanation" Mcp.Schema.string
        Mcp.Schema.optional "steps" (Mcp.Schema.array Mcp.Schema.string)
    ]

    let options = {
        Options.defaults with
            OutputFormat = Some schema
    }

    info "Executing query with structured output schema..."
    debug $"Schema: {Thoth.Json.Net.Encode.toString 0 (Json.Encode.schema schema)}"

    let! result = queryStructured "What is 15% of 80? Show your work." options

    match result with
    | Ok (Some json) ->
        success $"Structured output:\n{Thoth.Json.Net.Encode.toString 2 json}"
    | Ok None ->
        warn "No structured output returned"
    | Error e ->
        logError e
}

/// Run all examples
let runAll () = task {
    info "Starting SimpleQuery examples..."
    printfn ""

    do! basicQuery ()
    do! streamingQuery ()
    do! customOptionsQuery ()
    do! structuredOutputQuery ()

    printfn ""
    success "SimpleQuery examples completed!"
}
