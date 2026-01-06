/// Example 09: Structured Output - JSON Schema output format
module Examples.StructuredOutput

open ClaudeAgentSdk
open Thoth.Json.Net
open Examples.Common

let basicStructured () = task {
    TUI.banner "Structured Output - JSON Schema"

    // Define output schema
    let schema = Mcp.Schema.object' [
        Mcp.Schema.required "answer" (Mcp.Schema.number' "The numeric answer")
        Mcp.Schema.required "explanation" (Mcp.Schema.string' "Step-by-step explanation")
        Mcp.Schema.optional "steps" (Mcp.Schema.array (Mcp.Schema.string' "Individual steps"))
    ]

    let options = {
        Options.defaults with
            OutputFormat = Some schema
    }

    info "Requesting structured output (JSON)..."
    TUI.blank ()

    let! result = queryStructured "What is 15% of 80? Show your work." options

    match result with
    | Ok (Some json) ->
        success "Structured output received:"
        TUI.blank ()
        TUI.greenLn (Encode.toString 2 json)
    | Ok None ->
        warn "No structured output received"
    | Error e ->
        logError e

    TUI.blank ()
}

let complexSchema () = task {
    TUI.banner "Structured Output - Complex Schema"

    // Complex nested schema
    let personSchema = Mcp.Schema.object'' "A person" [
        Mcp.Schema.required "name" (Mcp.Schema.string' "Full name")
        Mcp.Schema.required "age" (Mcp.Schema.number' "Age in years")
        Mcp.Schema.optional "email" (Mcp.Schema.string' "Email address")
    ]

    let schema = Mcp.Schema.object' [
        Mcp.Schema.required "people" (Mcp.Schema.array' "List of people" personSchema)
        Mcp.Schema.required "total" (Mcp.Schema.number' "Total count")
    ]

    let options = {
        Options.defaults with
            OutputFormat = Some schema
    }

    info "Requesting complex structured output..."
    TUI.blank ()

    let! result = queryStructured "Create data for 3 fictional people (name, age, email)" options

    match result with
    | Ok (Some json) ->
        success "Complex structured output:"
        TUI.blank ()
        TUI.cyanLn (Encode.toString 2 json)
    | Ok None ->
        warn "No output"
    | Error e ->
        logError e

    TUI.blank ()
}

let runAll () = task {
    do! basicStructured ()
    TUI.separator ()
    do! complexSchema ()
}
