/// Example 03: MCP Tools - Create and use custom tools
module Examples.McpTools

open System
open System.Threading.Tasks
open ClaudeAgentSdk
open Examples.Common

// ============================================================================
// Example MCP Tools
// ============================================================================

let createGreetTool () =
    let schema = Mcp.Schema.object' [
        Mcp.Schema.required "name" (Mcp.Schema.string' "The name to greet")
    ]

    Mcp.tool "greet" "Greet a person by name" schema (fun input -> task {
        match Mcp.getString "name" input with
        | Ok name -> return Mcp.textResult $"Hello, {name}! Nice to meet you!"
        | Error e -> return Mcp.errorResult e
    })

let createCalculatorTool () =
    let schema = Mcp.Schema.object' [
        Mcp.Schema.required "operation" (Mcp.Schema.string' "Operation: add, subtract, multiply, divide")
        Mcp.Schema.required "a" (Mcp.Schema.number' "First number")
        Mcp.Schema.required "b" (Mcp.Schema.number' "Second number")
    ]

    Mcp.tool "calculator" "Perform basic math operations" schema (fun input -> task {
        match Mcp.getString "operation" input, Mcp.getFloat "a" input, Mcp.getFloat "b" input with
        | Ok op, Ok a, Ok b ->
            match op.ToLower() with
            | "add" -> return Mcp.textResult (sprintf "Result: %f" (a + b))
            | "subtract" -> return Mcp.textResult (sprintf "Result: %f" (a - b))
            | "multiply" -> return Mcp.textResult (sprintf "Result: %f" (a * b))
            | "divide" when b <> 0.0 -> return Mcp.textResult (sprintf "Result: %f" (a / b))
            | "divide" -> return Mcp.errorResult "Cannot divide by zero"
            | _ -> return Mcp.errorResult (sprintf "Unknown operation: %s" op)
        | Error e, _, _ | _, Error e, _ | _, _, Error e ->
            return Mcp.errorResult e
    })

let createWeatherTool () =
    let schema = Mcp.Schema.object' [
        Mcp.Schema.required "city" (Mcp.Schema.string' "City name")
    ]

    Mcp.tool "get_weather" "Get weather for a city (mock data)" schema (fun input -> task {
        match Mcp.getString "city" input with
        | Ok city ->
            // Mock weather data
            let temp = 15 + (city.Length % 20)
            let conditions = ["Sunny"; "Cloudy"; "Rainy"; "Snowy"].[city.Length % 4]
            return Mcp.textResult $"Weather in {city}: {temp}°C, {conditions}"
        | Error e ->
            return Mcp.errorResult e
    })

let createSearchTool () =
    let schema = Mcp.Schema.object' [
        Mcp.Schema.required "query" (Mcp.Schema.string' "Search query")
        Mcp.Schema.optional "limit" (Mcp.Schema.number' "Max results")
    ]

    Mcp.tool "search" "Search for information (mock)" schema (fun input -> task {
        match Mcp.getString "query" input with
        | Ok query ->
            let limit = Mcp.tryGetInt "limit" input |> Option.defaultValue 3
            let results = [1..limit] |> List.map (fun i -> $"Result {i} for '{query}'")
            return Mcp.textResult (String.concat "\n" results)
        | Error e ->
            return Mcp.errorResult e
    })

let createExampleServer () =
    Mcp.createSdkServer "example-tools" [
        createGreetTool ()
        createCalculatorTool ()
        createWeatherTool ()
        createSearchTool ()
    ]

// ============================================================================
// Examples
// ============================================================================

let directToolTest () = task {
    UI.banner "MCP Tools - Direct Testing"

    let logger = Logger.normal

    let greet = createGreetTool ()
    let calc = createCalculatorTool ()

    Logger.info "Testing greet tool..." logger
    let! greetResult = greet.Handler (Thoth.Json.Net.Encode.object ["name", Thoth.Json.Net.Encode.string "Alice"])
    match greetResult with
    | ToolSuccess content ->
        for c in content do
            match c with
            | TextContent t -> printfn "  %s" t
            | _ -> ()
    | ToolFailure e ->
        Logger.error $"Error: {e}" logger

    printfn ""

    Logger.info "Testing calculator tool..." logger
    let! calcResult = calc.Handler (Thoth.Json.Net.Encode.object [
        "operation", Thoth.Json.Net.Encode.string "multiply"
        "a", Thoth.Json.Net.Encode.float 7.0
        "b", Thoth.Json.Net.Encode.float 8.0
    ])
    match calcResult with
    | ToolSuccess content ->
        for c in content do
            match c with
            | TextContent t -> printfn "  %s" t
            | _ -> ()
    | ToolFailure e ->
        Logger.error $"Error: {e}" logger

    printfn ""
}

let withClaude () = task {
    UI.banner "MCP Tools - With Claude (Auto-Allow)"

    let logger = Logger.normal

    let server = createExampleServer ()

    let options = {
        Options.defaults with
            McpServers = Map.ofList ["example-tools", server]
            // NO AllowedTools needed! Auto-allowed by default
            ToolAllowMode = AutoAllowMcp
            SystemPrompt = Some (SystemPromptText "You have access to tools. Use them when helpful.")
            PermissionMode = Some AcceptEdits
    }

    Logger.info "Registered MCP server with auto-allow" logger
    match server with
    | SdkServer(_, tools) ->
        for tool in tools do
            Logger.debug $"  - {tool.Name}: {tool.Description}" logger
    | _ -> ()

    printfn ""

    let! connectResult = Client.connect options

    match connectResult with
    | Error e ->
        logError e
    | Ok ctx ->
        try
            Logger.info "Asking Claude to use the calculator..." logger
            let! sendResult = Client.send "Use the calculator to multiply 7 by 8" ctx

            match sendResult with
            | Ok ctx ->
                let! result = Stream.streamWithDisplay logger (Client.receive ctx)
                match result with
                | Ok messages ->
                    printfn ""
                    for text in Client.getAssistantText messages do
                        Console.ForegroundColor <- ConsoleColor.Cyan
                        printfn "\n%s\n" text
                        Console.ResetColor()
                | Error e ->
                    logError e
            | Error e ->
                logError e
        finally
            Client.disconnect ctx |> Async.AwaitTask |> Async.RunSynchronously
}

let runAll () = task {
    do! directToolTest ()
    UI.separator ()
    do! withClaude ()
}
