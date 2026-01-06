/// Example 11: Ask User Tool - Bidirectional interaction (functional, no mutable state)
module Examples.AskUserTool

open System
open ClaudeAgentSdk
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

        TUI.blank ()
        TUI.box "Claude needs your input" [question]

        // Show options if provided
        match options with
        | Some opts when not (List.isEmpty opts) ->
            TUI.blank ()
            opts |> List.iteri (fun i opt ->
                TUI.indentLn 4 (sprintf "[%d] %s" (i + 1) opt))
            TUI.blank ()
            TUI.text "  Enter number or type response: "
        | _ ->
            match defaultValue with
            | Some d -> TUI.promptDefault "" d
            | None -> TUI.text "  > "

        let response = TUI.readLine ()

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

        TUI.greenLn (sprintf "  ✓ Response: %s" finalResponse)

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

        TUI.blank ()
        TUI.yellow (sprintf "  %s " question)

        match defaultValue with
        | Some true -> TUI.promptYN true
        | Some false -> TUI.promptYN false
        | None -> TUI.text "[y/n]: "

        let key = TUI.readKeyChar ()
        let response =
            match Char.ToLower(key) with
            | 'y' -> true
            | 'n' -> false
            | _ -> defaultValue |> Option.defaultValue false

        TUI.textLn (if response then "yes" else "no")

        return Mcp.textResult (if response then "yes" else "no")
    })

let createSecretInputTool () =
    let schema = Mcp.Schema.object' [
        Mcp.Schema.required "prompt" (Mcp.Schema.string' "What to ask for")
    ]

    Mcp.tool "get_secret" "Get hidden input from user" schema (fun input -> task {
        let prompt = Mcp.tryGetString "prompt" input |> Option.defaultValue "Enter secret"

        TUI.blank ()
        TUI.red (sprintf "  %s (hidden): " prompt)

        // Use functional recursive helper from TUI
        let secret = TUI.readHidden ""

        TUI.textLn "[hidden]"

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

        TUI.blank ()
        TUI.textLn prompt
        let endMsg = if endMarker = "" then "empty line" else endMarker
        TUI.textLn (sprintf "(Enter '%s' to finish)" endMsg)

        // Use functional recursive helper from TUI
        let lines = TUI.readMultiline endMarker []
        let result = String.concat "\n" lines

        TUI.textLn (sprintf "Received %d lines" (List.length lines))

        return Mcp.textResult result
    })

// ============================================================================
// Recursive Interactive Session
// ============================================================================

let rec interactiveSession (ctx: ClientContext) (logger: Logger) = task {
    TUI.prompt "You"
    let prompt = TUI.readLine ()

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
                TUI.blank ()
                return! interactiveSession ctx logger
            | Error e ->
                logError e
                return! interactiveSession ctx logger
}

// ============================================================================
// Example
// ============================================================================

let run () = task {
    TUI.banner "Ask User Tool - Bidirectional Interaction"

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
            TUI.textLn "Connected! Claude can now ask you questions."
            TUI.blank ()
            TUI.textLn "Try: Ask me what my favorite color is"
            TUI.textLn "  Or: Ask me to confirm something"
            TUI.textLn "  Or: Ask me for a password"
            TUI.blank ()

            do! interactiveSession ctx logger
        finally
            Client.disconnect ctx |> Async.AwaitTask |> Async.RunSynchronously
}
