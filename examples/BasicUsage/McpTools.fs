/// Example: Creating and using MCP tools with verbose logging
module Examples.McpTools

open System
open System.IO
open ClaudeAgentSdk
open Thoth.Json.Net
open Examples.Logging

/// Example 1: Simple tool with no input
let createGreetingTool () =
    Mcp.tool
        "greet"
        "Returns a friendly greeting"
        Mcp.Schema.any
        (fun _ -> task {
            debug "greet tool called"
            return Mcp.textResult "Hello! I'm your friendly MCP tool."
        })

/// Example 2: Tool with typed input schema
let createCalculatorTool () =
    let schema = Mcp.Schema.object' [
        Mcp.Schema.required "operation" (Mcp.Schema.string' "One of: add, subtract, multiply, divide")
        Mcp.Schema.required "a" (Mcp.Schema.number' "First operand")
        Mcp.Schema.required "b" (Mcp.Schema.number' "Second operand")
    ]

    Mcp.tool
        "calculator"
        "Performs basic arithmetic operations"
        schema
        (fun input -> task {
            debug $"calculator tool called with: {Encode.toString 0 input}"
            match Mcp.getString "operation" input, Mcp.getFloat "a" input, Mcp.getFloat "b" input with
            | Ok op, Ok a, Ok b ->
                let result =
                    match op with
                    | "add" -> Some (a + b)
                    | "subtract" -> Some (a - b)
                    | "multiply" -> Some (a * b)
                    | "divide" when b <> 0.0 -> Some (a / b)
                    | "divide" -> None
                    | _ -> None

                match result with
                | Some r ->
                    debug $"calculator result: {r}"
                    return Mcp.textResult (sprintf "Result: %.2f" r)
                | None -> return Mcp.errorResult "Invalid operation or division by zero"

            | _ ->
                return Mcp.errorResult "Missing required parameters"
        })

/// Example 3: Tool that returns multiple content items
let createWeatherTool () =
    let schema = Mcp.Schema.object' [
        Mcp.Schema.required "city" Mcp.Schema.string
    ]

    Mcp.tool
        "get_weather"
        "Gets current weather for a city (mock data)"
        schema
        (fun input -> task {
            debug $"get_weather tool called with: {Encode.toString 0 input}"
            match Mcp.getString "city" input with
            | Ok city ->
                // Mock weather data
                let temp = Random.Shared.Next(0, 35)
                let conditions = ["Sunny"; "Cloudy"; "Rainy"; "Snowy"].[Random.Shared.Next(4)]

                debug $"Returning weather for {city}: {temp}C, {conditions}"
                return Mcp.multiResult [
                    TextContent (sprintf "Weather for %s:" city)
                    TextContent (sprintf "Temperature: %d°C" temp)
                    TextContent (sprintf "Conditions: %s" conditions)
                ]
            | Error e ->
                return Mcp.errorResult e
        })

/// Example 4: Tool with optional parameters
let createSearchTool () =
    let schema = Mcp.Schema.object' [
        Mcp.Schema.required "query" (Mcp.Schema.string' "Search query")
        Mcp.Schema.optional "limit" (Mcp.Schema.number' "Max results (default 10)")
        Mcp.Schema.optional "filter" (Mcp.Schema.string' "Optional filter")
    ]

    Mcp.tool
        "search"
        "Searches for items (mock implementation)"
        schema
        (fun input -> task {
            debug $"search tool called with: {Encode.toString 0 input}"
            match Mcp.getString "query" input with
            | Ok query ->
                let limit = Mcp.tryGetInt "limit" input |> Option.defaultValue 10
                let filter = Mcp.tryGetString "filter" input

                debug $"Searching for '{query}' with limit={limit}, filter={filter}"

                let results =
                    [1..limit]
                    |> List.map (fun i ->
                        match filter with
                        | Some f -> sprintf "Result %d for '%s' (filtered by: %s)" i query f
                        | None -> sprintf "Result %d for '%s'" i query)

                return Mcp.textResult (String.concat "\n" results)

            | Error e ->
                return Mcp.errorResult e
        })

/// Example 5: Tool with array input
let createBatchProcessTool () =
    let schema = Mcp.Schema.object' [
        Mcp.Schema.required "items" (Mcp.Schema.array Mcp.Schema.string)
        Mcp.Schema.optional "uppercase" Mcp.Schema.bool
    ]

    Mcp.tool
        "batch_process"
        "Processes a batch of string items"
        schema
        (fun input -> task {
            debug $"batch_process tool called with: {Encode.toString 0 input}"
            match Mcp.getStringList "items" input with
            | Ok items ->
                let uppercase = Mcp.tryGetBool "uppercase" input |> Option.defaultValue false
                debug $"Processing {List.length items} items, uppercase={uppercase}"
                let processed =
                    if uppercase then
                        items |> List.map (fun s -> s.ToUpper())
                    else
                        items |> List.map (fun s -> sprintf "Processed: %s" s)

                return Mcp.textResult (String.concat "\n" processed)

            | Error e ->
                return Mcp.errorResult e
        })

/// Example 6: Async tool that simulates work
let createLongRunningTool () =
    let schema = Mcp.Schema.object' [
        Mcp.Schema.required "duration_ms" (Mcp.Schema.number' "How long to wait in milliseconds")
    ]

    Mcp.tool
        "long_running"
        "Simulates a long-running operation"
        schema
        (fun input -> task {
            debug $"long_running tool called with: {Encode.toString 0 input}"
            match Mcp.getInt "duration_ms" input with
            | Ok duration ->
                let actualDuration = min duration 5000 // Cap at 5 seconds
                debug $"Waiting for {actualDuration}ms..."
                do! System.Threading.Tasks.Task.Delay(actualDuration)
                debug "Long running task completed"
                return Mcp.textResult (sprintf "Completed after %dms" duration)
            | Error e ->
                return Mcp.errorResult e
        })

/// Create an SDK server with all example tools
let createExampleServer () =
    Mcp.createSdkServer "example-tools" [
        createGreetingTool ()
        createCalculatorTool ()
        createWeatherTool ()
        createSearchTool ()
        createBatchProcessTool ()
        createLongRunningTool ()
    ]

/// Demonstrate using MCP tools with Claude
let useToolsWithClaude () = task {
    printfn "=== MCP Tools Example ==="
    printfn ""

    let server = createExampleServer ()

    let options = {
        Options.defaults with
            McpServers = Map.ofList ["example-tools", server]
            SystemPrompt = Some (SystemPromptText "You have access to various tools. Use them when appropriate.")
            PermissionMode = Some AcceptEdits  // Auto-approve tool use
    }

    info "Registered MCP server: example-tools"
    match server with
    | SdkServer(_, tools) ->
        for tool in tools do
            debug $"  - {tool.Name}: {tool.Description}"
    | _ -> ()

    info "Connecting to Claude with MCP tools..."
    let! connectResult = verboseConnect options

    match connectResult with
    | Error e ->
        logError e
    | Ok ctx ->
        try
            // Ask Claude to use the calculator
            info "Asking Claude to use the calculator tool..."
            let! result = verboseClientQuery "Use the calculator tool to multiply 7 by 8" ctx

            match result with
            | Ok (ctx, msgs) ->
                printfn ""
                Console.ForegroundColor <- ConsoleColor.Blue
                Console.WriteLine("Claude:")
                Console.ResetColor()
                for t in Client.getAssistantText msgs do
                    printfn "  %s" t

                // Ask about weather
                printfn ""
                info "Asking Claude to check the weather..."
                let! result2 = verboseClientQuery "What's the weather like in Tokyo?" ctx

                match result2 with
                | Ok (_, msgs) ->
                    printfn ""
                    Console.ForegroundColor <- ConsoleColor.Blue
                    Console.WriteLine("Claude:")
                    Console.ResetColor()
                    for t in Client.getAssistantText msgs do
                        printfn "  %s" t
                | Error e -> logError e
            | Error e -> logError e
        finally
            info "Disconnecting..."
            Client.disconnect ctx |> Async.AwaitTask |> Async.RunSynchronously
            success "MCP session ended."
}

/// Test tools directly without Claude
let testToolsDirectly () = task {
    printfn ""
    printfn "=== Testing Tools Directly ==="
    printfn ""

    // Test calculator
    info "Testing calculator tool..."
    let calculator = createCalculatorTool ()
    let input = Encode.object [
        "operation", Encode.string "multiply"
        "a", Encode.float 7.0
        "b", Encode.float 6.0
    ]
    send $"Input: {Encode.toString 0 input}"
    let! result = calculator.Handler input
    match result with
    | ToolSuccess [TextContent t] -> success $"Calculator: {t}"
    | _ -> error "Calculator failed"

    printfn ""

    // Test weather
    info "Testing weather tool..."
    let weather = createWeatherTool ()
    let input = Encode.object ["city", Encode.string "San Francisco"]
    send $"Input: {Encode.toString 0 input}"
    let! result = weather.Handler input
    match result with
    | ToolSuccess contents ->
        for c in contents do
            match c with
            | TextContent t -> success t
            | _ -> ()
    | ToolFailure e -> error $"Weather failed: {e}"

    printfn ""

    // Test batch process
    info "Testing batch_process tool..."
    let batch = createBatchProcessTool ()
    let input = Encode.object [
        "items", Encode.list [Encode.string "hello"; Encode.string "world"]
        "uppercase", Encode.bool true
    ]
    send $"Input: {Encode.toString 0 input}"
    let! result = batch.Handler input
    match result with
    | ToolSuccess [TextContent t] -> success $"Batch result:\n{t}"
    | _ -> error "Batch failed"
}

/// Run all MCP examples
let runAll () = task {
    info "Starting MCP Tools examples..."
    printfn ""

    do! testToolsDirectly ()

    printfn ""
    info "Now testing with actual Claude CLI..."
    printfn ""

    do! useToolsWithClaude ()

    printfn ""
    success "MCP Tools examples completed!"
}
