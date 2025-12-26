/// Example: AI asks user for clarification via custom MCP tool with verbose logging
module Examples.AskUserTool

open System
open FSharp.Control
open Thoth.Json.Net
open ClaudeAgentSdk
open Examples.Logging

// ============================================================================
// Ask User Tool - Claude can call this to get user input
// ============================================================================

/// Create a tool that lets Claude ask the user questions
let createAskUserTool () =
    let schema = Mcp.Schema.object' [
        Mcp.Schema.required "question" (Mcp.Schema.string' "The question to ask the user")
        Mcp.Schema.optional "options" (Mcp.Schema.array Mcp.Schema.string)
        Mcp.Schema.optional "default" Mcp.Schema.string
    ]

    Mcp.tool
        "ask_user"
        "Ask the user a question and wait for their response. Use this when you need clarification or user input."
        schema
        (fun input -> task {
            let question = Mcp.tryGetString "question" input |> Option.defaultValue "?"
            let options = Mcp.tryGetStringList "options" input
            let defaultValue = Mcp.tryGetString "default" input

            Console.WriteLine()
            Console.ForegroundColor <- ConsoleColor.Cyan
            Console.WriteLine("╭─────────────────────────────────────────╮")
            Console.WriteLine("│  Claude needs your input:               │")
            Console.WriteLine("╰─────────────────────────────────────────╯")
            Console.ResetColor()

            Console.ForegroundColor <- ConsoleColor.White
            Console.WriteLine($"  {question}")
            Console.ResetColor()

            // Show options if provided
            match options with
            | Some opts when not (List.isEmpty opts) ->
                Console.WriteLine()
                opts |> List.iteri (fun i opt ->
                    Console.WriteLine($"    [{i + 1}] {opt}"))
                Console.WriteLine()
                Console.Write("  Enter number or type response: ")
            | _ ->
                match defaultValue with
                | Some d -> Console.Write($"  [{d}]: ")
                | None -> Console.Write("  > ")

            let response = Console.ReadLine()

            // Handle numbered selection
            let finalResponse =
                match options with
                | Some opts ->
                    match Int32.TryParse(response) with
                    | true, n when n >= 1 && n <= List.length opts ->
                        opts.[n - 1]
                    | _ when String.IsNullOrWhiteSpace(response) ->
                        defaultValue |> Option.defaultValue ""
                    | _ -> response
                | None when String.IsNullOrWhiteSpace(response) ->
                    defaultValue |> Option.defaultValue ""
                | None -> response

            Console.ForegroundColor <- ConsoleColor.Green
            Console.WriteLine($"  ✓ Response: {finalResponse}")
            Console.ResetColor()

            return Mcp.textResult finalResponse
        })

/// Create a confirmation tool (yes/no questions)
let createConfirmTool () =
    let schema = Mcp.Schema.object' [
        Mcp.Schema.required "question" (Mcp.Schema.string' "The yes/no question to ask")
        Mcp.Schema.optional "default" (Mcp.Schema.bool' "Default value if user just presses Enter")
    ]

    Mcp.tool
        "confirm"
        "Ask the user a yes/no question. Returns 'yes' or 'no'."
        schema
        (fun input -> task {
            let question = Mcp.tryGetString "question" input |> Option.defaultValue "Confirm?"
            let defaultValue = Mcp.tryGetBool "default" input

            Console.WriteLine()
            Console.ForegroundColor <- ConsoleColor.Yellow
            Console.Write($"  {question} ")
            Console.ResetColor()

            match defaultValue with
            | Some true -> Console.Write("[Y/n]: ")
            | Some false -> Console.Write("[y/N]: ")
            | None -> Console.Write("[y/n]: ")

            let key = Console.ReadKey(true)
            let response =
                match Char.ToLower(key.KeyChar) with
                | 'y' -> true
                | 'n' -> false
                | _ -> defaultValue |> Option.defaultValue false

            Console.WriteLine(if response then "yes" else "no")

            return Mcp.textResult (if response then "yes" else "no")
        })

/// Create a password/secret input tool (hidden input)
let createSecretInputTool () =
    let schema = Mcp.Schema.object' [
        Mcp.Schema.required "prompt" (Mcp.Schema.string' "What to ask for (e.g., 'API key', 'password')")
    ]

    Mcp.tool
        "get_secret"
        "Securely get a secret value from the user (input is hidden). DO NOT log or display this value."
        schema
        (fun input -> task {
            let prompt = Mcp.tryGetString "prompt" input |> Option.defaultValue "Enter secret"

            Console.WriteLine()
            Console.ForegroundColor <- ConsoleColor.Red
            Console.Write($"  {prompt} (hidden): ")
            Console.ResetColor()

            // Read without echoing
            let mutable secret = ""
            let mutable reading = true
            while reading do
                let key = Console.ReadKey(true)
                if key.Key = ConsoleKey.Enter then
                    reading <- false
                elif key.Key = ConsoleKey.Backspace then
                    if secret.Length > 0 then
                        secret <- secret.Substring(0, secret.Length - 1)
                else
                    secret <- secret + string key.KeyChar

            Console.WriteLine("[hidden]")

            return Mcp.textResult secret
        })

/// Create a multi-line input tool
let createMultiLineInputTool () =
    let schema = Mcp.Schema.object' [
        Mcp.Schema.required "prompt" (Mcp.Schema.string' "What to ask for")
        Mcp.Schema.optional "end_marker" (Mcp.Schema.string' "Text that ends input (default: empty line)")
    ]

    Mcp.tool
        "get_multiline_input"
        "Get multi-line text input from the user. Useful for code, descriptions, etc."
        schema
        (fun input -> task {
            let prompt = Mcp.tryGetString "prompt" input |> Option.defaultValue "Enter text"
            let endMarker = Mcp.tryGetString "end_marker" input |> Option.defaultValue ""

            Console.WriteLine()
            Console.ForegroundColor <- ConsoleColor.Cyan
            Console.WriteLine($"  {prompt}")
            if endMarker = "" then
                Console.WriteLine("  (Enter empty line to finish)")
            else
                Console.WriteLine($"  (Enter '{endMarker}' to finish)")
            Console.ResetColor()
            Console.WriteLine()

            let lines = ResizeArray<string>()
            let mutable reading = true
            while reading do
                Console.Write("  | ")
                let line = Console.ReadLine()
                if (endMarker = "" && String.IsNullOrEmpty(line)) ||
                   (endMarker <> "" && line = endMarker) then
                    reading <- false
                else
                    lines.Add(line)

            let result = String.Join("\n", lines)
            Console.ForegroundColor <- ConsoleColor.Green
            Console.WriteLine($"  ✓ Received {lines.Count} lines")
            Console.ResetColor()

            return Mcp.textResult result
        })

// ============================================================================
// Create MCP Server with all user interaction tools
// ============================================================================

let createUserInteractionServer () =
    Mcp.createSdkServer "user-interaction" [
        createAskUserTool ()
        createConfirmTool ()
        createSecretInputTool ()
        createMultiLineInputTool ()
    ]

// ============================================================================
// Demo: Interactive Session with Ask User
// ============================================================================

/// Run a session where Claude can ask questions
let interactiveSession () = task {
    Console.Clear()
    Console.ForegroundColor <- ConsoleColor.Cyan
    Console.WriteLine("╔═══════════════════════════════════════════════════════════╗")
    Console.WriteLine("║     CLAUDE AGENT SDK - Ask User Tool Demo                 ║")
    Console.WriteLine("╠═══════════════════════════════════════════════════════════╣")
    Console.WriteLine("║  Claude can ask you questions during execution using      ║")
    Console.WriteLine("║  the ask_user, confirm, and other interaction tools.      ║")
    Console.WriteLine("╚═══════════════════════════════════════════════════════════╝")
    Console.ResetColor()
    Console.WriteLine()

    let server = createUserInteractionServer ()

    let options = {
        Options.defaults with
            McpServers = Map.ofList ["user-interaction", server]
            // CRITICAL: Must explicitly allow SDK MCP tools!
            AllowedTools = [
                "mcp__user-interaction__ask_user"
                "mcp__user-interaction__confirm"
                "mcp__user-interaction__get_secret"
                "mcp__user-interaction__get_multiline_input"
            ]
            SystemPrompt = Some (SystemPromptText """You are a helpful assistant.
When you need information from the user, use the ask_user tool.
When you need yes/no confirmation, use the confirm tool.
When you need multi-line input like code, use the get_multiline_input tool.
Always gather necessary information before proceeding with tasks.""")
            // Auto-approve these interaction tools
            PermissionMode = Some AcceptEdits
    }

    info "Connecting with user interaction tools..."
    let! connectResult = verboseConnect options

    match connectResult with
    | Error e ->
        logError e

    | Ok ctx ->
        try
            Console.WriteLine("Connected! Try prompts like:")
            Console.ForegroundColor <- ConsoleColor.DarkGray
            Console.WriteLine("  - 'Write a greeting for me'")
            Console.WriteLine("  - 'Help me write a function'")
            Console.WriteLine("  - 'Create a personalized message'")
            Console.ResetColor()
            Console.WriteLine()

            let mutable ctx = ctx
            let mutable running = true

            while running do
                Console.ForegroundColor <- ConsoleColor.White
                Console.Write("You> ")
                Console.ResetColor()

                let prompt = Console.ReadLine()

                if String.IsNullOrWhiteSpace(prompt) then
                    running <- false
                else
                    Console.WriteLine()
                    info (sprintf "Sending: %s" prompt)

                    // Use streaming so we see output in real-time
                    let! result = streamingClientQuery prompt ctx
                    match result with
                    | Ok (newCtx, messages) ->
                        ctx <- newCtx
                        Console.WriteLine()

                    | Error e ->
                        logError e
                        Console.WriteLine()

            Console.WriteLine("Session ended.")

        finally
            info "Disconnecting..."
            Client.disconnect ctx |> Async.AwaitTask |> Async.RunSynchronously
}

/// Demo the tools directly without Claude
let demoToolsDirectly () = task {
    printfn "=== Ask User Tools Demo ==="
    printfn ""

    printfn "1. Testing ask_user tool:"
    let askTool = createAskUserTool ()
    let input1 = Encode.object [
        "question", Encode.string "What is your name?"
    ]
    let! result1 = askTool.Handler input1
    match result1 with
    | ToolSuccess [TextContent t] -> printfn "   Got: %s" t
    | _ -> ()

    printfn ""

    // Test ask_user with options
    printfn "2. Testing ask_user with options:"
    let input2 = Encode.object [
        "question", Encode.string "What's your favorite color?"
        "options", Encode.list [
            Encode.string "Red"
            Encode.string "Blue"
            Encode.string "Green"
        ]
    ]
    let! result2 = askTool.Handler input2
    match result2 with
    | ToolSuccess [TextContent t] -> printfn "   Got: %s" t
    | _ -> ()

    printfn ""

    // Test confirm
    printfn "3. Testing confirm tool:"
    let confirmTool = createConfirmTool ()
    let input3 = Encode.object [
        "question", Encode.string "Do you want to proceed?"
        "default", Encode.bool true
    ]
    let! result3 = confirmTool.Handler input3
    match result3 with
    | ToolSuccess [TextContent t] -> printfn "   Got: %s" t
    | _ -> ()

    printfn ""

    // Test multiline
    printfn "4. Testing multi-line input:"
    let multiTool = createMultiLineInputTool ()
    let input4 = Encode.object [
        "prompt", Encode.string "Enter some code:"
    ]
    let! result4 = multiTool.Handler input4
    match result4 with
    | ToolSuccess [TextContent t] -> printfn "   Got:\n%s" t
    | _ -> ()
}

/// Run all examples
let runAll () = task {
    info "Starting Ask User Tool examples..."
    printfn ""

    Console.WriteLine("Ask User Tools Example")
    Console.WriteLine("======================")
    Console.WriteLine()
    Console.WriteLine("This example shows how Claude can ask YOU questions!")
    Console.WriteLine()

    Console.ForegroundColor <- ConsoleColor.Yellow
    Console.WriteLine("Available tools for Claude to use:")
    Console.ResetColor()
    Console.WriteLine("""
    ask_user        - Ask any question, optionally with multiple choice
    confirm         - Ask yes/no questions
    get_secret      - Get passwords/API keys (hidden input)
    get_multiline   - Get multi-line text like code snippets
    """)

    Console.ForegroundColor <- ConsoleColor.Yellow
    Console.WriteLine("Example conversation flow:")
    Console.ResetColor()
    Console.WriteLine("""
    You:    "Write a greeting for me"
    Claude: [calls ask_user: "What is your name?"]
    You:    "Alice"
    Claude: [calls ask_user: "What tone - formal or casual?"]
    You:    "casual"
    Claude: "Hey Alice! Great to meet you!"
    """)

    Console.ForegroundColor <- ConsoleColor.Cyan
    Console.Write("Run interactive session with Claude? [Y/n]: ")
    Console.ResetColor()

    let key = Console.ReadKey(true)
    Console.WriteLine()

    if Char.ToLower(key.KeyChar) <> 'n' then
        // Run actual interactive session with Claude
        do! interactiveSession ()
    else
        Console.WriteLine()
        info "Testing the tools directly (without Claude):"
        Console.WriteLine()
        do! demoToolsDirectly ()

    printfn ""
    success "Ask User Tool examples completed!"
}
