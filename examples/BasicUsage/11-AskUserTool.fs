/// Example 11: Ask User Tool - Bidirectional interaction (functional, no mutable state)
module Examples.AskUserTool

open System
open ClaudeAgentSdk
open Thoth.Json.Net
open Examples.Common

// ============================================================================
// MCP Tools for User Interaction (Functional Implementations)
// ============================================================================

let createAskUserTool () =
    let schema = Mcp.Schema.object' [
        Mcp.Schema.required "question" (Mcp.Schema.string' "The question to ask the user")
        Mcp.Schema.optional "options" (Mcp.Schema.array Mcp.Schema.string)
        Mcp.Schema.optional "default" Mcp.Schema.string
    ]

    Mcp.tool "ask_user" "Ask the user a question and wait for response" schema (fun input -> task {
        let question = Mcp.tryGetString "question" input |> Option.defaultValue "?"
        let options = Mcp.tryGetStringList "options" input
        let defaultValue = Mcp.tryGetString "default" input

        printfn ""
        Console.ForegroundColor <- ConsoleColor.Cyan
        printfn "╭─────────────────────────────────────────╮"
        printfn "│  Claude needs your input:               │"
        printfn "╰─────────────────────────────────────────╯"
        Console.ResetColor()

        Console.ForegroundColor <- ConsoleColor.White
        printfn "  %s" question
        Console.ResetColor()

        // Show options if provided
        match options with
        | Some opts when not (List.isEmpty opts) ->
            printfn ""
            opts |> List.iteri (fun i opt ->
                printfn "    [%d] %s" (i + 1) opt)
            printfn ""
            printf "  Enter number or type response: "
        | _ ->
            match defaultValue with
            | Some d -> printf "  [%s]: " d
            | None -> printf "  > "

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
        printfn "  ✓ Response: %s" finalResponse
        Console.ResetColor()

        return Mcp.textResult finalResponse
    })

let createConfirmTool () =
    let schema = Mcp.Schema.object' [
        Mcp.Schema.required "question" (Mcp.Schema.string' "The yes/no question")
        Mcp.Schema.optional "default" (Mcp.Schema.bool' "Default value")
    ]

    Mcp.tool "confirm" "Ask a yes/no question" schema (fun input -> task {
        let question = Mcp.tryGetString "question" input |> Option.defaultValue "Confirm?"
        let defaultValue = Mcp.tryGetBool "default" input

        printfn ""
        Console.ForegroundColor <- ConsoleColor.Yellow
        printf "  %s " question
        Console.ResetColor()

        match defaultValue with
        | Some true -> printf "[Y/n]: "
        | Some false -> printf "[y/N]: "
        | None -> printf "[y/n]: "

        let key = Console.ReadKey(true)
        let response =
            match Char.ToLower(key.KeyChar) with
            | 'y' -> true
            | 'n' -> false
            | _ -> defaultValue |> Option.defaultValue false

        printfn "%s" (if response then "yes" else "no")

        return Mcp.textResult (if response then "yes" else "no")
    })

let createSecretInputTool () =
    let schema = Mcp.Schema.object' [
        Mcp.Schema.required "prompt" (Mcp.Schema.string' "What to ask for")
    ]

    Mcp.tool "get_secret" "Get hidden input from user" schema (fun input -> task {
        let prompt = Mcp.tryGetString "prompt" input |> Option.defaultValue "Enter secret"

        printfn ""
        Console.ForegroundColor <- ConsoleColor.Red
        printf "  %s (hidden): " prompt
        Console.ResetColor()

        // Use functional recursive helper from Common
        let secret = Console.readHiddenInput ""

        printfn "[hidden]"

        return Mcp.textResult secret
    })

let createMultiLineInputTool () =
    let schema = Mcp.Schema.object' [
        Mcp.Schema.required "prompt" (Mcp.Schema.string' "What to ask for")
        Mcp.Schema.optional "end_marker" (Mcp.Schema.string' "Line to end input")
    ]

    Mcp.tool "get_multiline_input" "Get multi-line input from user" schema (fun input -> task {
        let prompt = Mcp.tryGetString "prompt" input |> Option.defaultValue "Enter text"
        let endMarker = Mcp.tryGetString "end_marker" input |> Option.defaultValue ""

        printfn ""
        printfn "%s" prompt
        let endMsg = if endMarker = "" then "empty line" else endMarker
        printfn "(Enter '%s' to finish)" endMsg

        // Use functional recursive helper from Common
        let lines = Console.readMultilineUntil endMarker []
        let result = String.concat "\n" lines

        printfn "Received %d lines" (List.length lines)

        return Mcp.textResult result
    })

// ============================================================================
// Recursive Interactive Session
// ============================================================================

let rec interactiveSession (ctx: ClientContext) (logger: Logger) = task {
    printf "You> "
    let prompt = Console.ReadLine()

    match prompt with
    | null | "" | "exit" ->
        Logger.info "Goodbye!" logger
        return ()

    | prompt ->
        let! sendResult = Client.send prompt ctx

        match sendResult with
        | Error e ->
            logError e
            return! interactiveSession ctx logger

        | Ok ctx ->
            let! result = Stream.streamWithDisplay logger (Client.receive ctx)

            match result with
            | Ok _ ->
                printfn ""
                return! interactiveSession ctx logger
            | Error e ->
                logError e
                return! interactiveSession ctx logger
}

// ============================================================================
// Example
// ============================================================================

let run () = task {
    UI.banner "Ask User Tool - Bidirectional Interaction"

    let logger = Logger.normal

    let server = Mcp.createSdkServer "user-interaction" [
        createAskUserTool ()
        createConfirmTool ()
        createSecretInputTool ()
        createMultiLineInputTool ()
    ]

    let options = {
        Options.defaults with
            McpServers = Map.ofList ["user-interaction", server]
            // No need for AllowedTools! Auto-allowed with AutoAllowMcp
            ToolAllowMode = AutoAllowMcp
            PermissionMode = Some AcceptEdits
            SystemPrompt = Some (SystemPromptText "You can ask the user for input using the available tools.")
    }

    let! connectResult = Client.connect options

    match connectResult with
    | Error e ->
        logError e
    | Ok ctx ->
        try
            printfn "Connected! Claude can now ask you questions."
            printfn ""
            printfn "Try: Ask me what my favorite color is"
            printfn "  Or: Ask me to confirm something"
            printfn "  Or: Ask me for a password"
            printfn ""

            do! interactiveSession ctx logger
        finally
            Client.disconnect ctx |> Async.AwaitTask |> Async.RunSynchronously
}
