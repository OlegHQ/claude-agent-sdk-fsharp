/// Example: Human-in-the-loop approval for tool execution with verbose logging
module Examples.HumanInTheLoop

open System
open FSharp.Control
open Thoth.Json.Net
open ClaudeAgentSdk
open Examples.Common

// ============================================================================
// Interactive Permission Callback
// ============================================================================

/// Prompt user for approval before each tool use
let interactiveApproval: string -> JsonValue -> PermissionContext -> System.Threading.Tasks.Task<PermissionResult> =
    fun toolName input ctx -> task {
        TUI.blank ()
        TUI.approvalBox
            "TOOL APPROVAL REQUEST"
            [
                sprintf "Tool: %s" toolName
                "Input:"
            ]
            ""

        // Pretty print the input
        let inputStr = Encode.toString 2 input
        for line in inputStr.Split('\n') do
            TUI.indentLn 4 line

        TUI.blank ()

        // Show suggestions if any
        if not (List.isEmpty ctx.Suggestions) then
            TUI.cyanLn "  Suggested permissions:"
            for suggestion in ctx.Suggestions do
                TUI.textLn (sprintf "    - %A" suggestion.Type)
            TUI.blank ()

        // Prompt for decision
        TUI.green "  "
        TUI.promptOptions ["A"; "D"; "M"]

        let key = TUI.readKeyChar ()
        TUI.textLn (string key)

        match Char.ToLower(key) with
        | 'a' ->
            TUI.greenLn "  OK Approved"
            debug (sprintf "User approved tool: %s" toolName)
            return PermitAllow(None, None)

        | 'd' ->
            TUI.text "  Reason (optional): "
            let reason = TUI.readLine ()
            let reason = if String.IsNullOrWhiteSpace(reason) then "Denied by user" else reason

            TUI.text "  Interrupt session? "
            TUI.promptYN false
            let interrupt = Char.ToLower(TUI.readKeyChar ()) = 'y'
            TUI.blank ()

            TUI.redLn (sprintf "  X Denied: %s" reason)
            warn (sprintf "User denied tool: %s - %s" toolName reason)
            return PermitDeny(reason, interrupt)

        | 'm' ->
            TUI.textLn "  Enter modified input (JSON):"
            TUI.text "  > "
            let modifiedJson = TUI.readLine ()

            match Decode.fromString Decode.value modifiedJson with
            | Ok newInput ->
                TUI.greenLn "  OK Approved with modified input"
                debug (sprintf "User modified and approved tool: %s" toolName)
                return PermitAllow(Some newInput, None)
            | Error e ->
                TUI.redLn (sprintf "  Invalid JSON: %s" e)
                TUI.textLn "  Falling back to original input..."
                return PermitAllow(None, None)

        | _ ->
            TUI.grayLn "  (defaulting to Allow)"
            return PermitAllow(None, None)
    }

/// Auto-approve safe tools, prompt for dangerous ones
let smartApproval: string -> JsonValue -> PermissionContext -> System.Threading.Tasks.Task<PermissionResult> =
    fun toolName input ctx -> task {
        // Define safe tools that don't need approval
        let safeTool =
            match toolName with
            | "Read" | "Glob" | "Grep" | "LS" -> true
            | _ -> false

        if safeTool then
            TUI.darkGrayLn (sprintf "  [Auto-approved: %s]" toolName)
            debug (sprintf "Auto-approved safe tool: %s" toolName)
            return PermitAllow(None, None)
        else
            // For other tools, ask user
            return! interactiveApproval toolName input ctx
    }

// ============================================================================
// Interactive Hook for Reviewing Tool Results
// ============================================================================

/// Hook that shows tool results and asks if user wants to continue
let reviewResultsHook: HookCallback =
    fun input toolUseId -> task {
        match input with
        | PostToolUseInput(_, _, _, toolName, _, response) ->
            TUI.blank ()
            TUI.cyanLn (sprintf "  Tool '%s' completed." toolName)

            let responseStr = Encode.toString 2 response
            let preview =
                if responseStr.Length > 500 then
                    responseStr.Substring(0, 500) + "...\n  [truncated]"
                else
                    responseStr

            TUI.textLn "  Result:"
            for line in preview.Split('\n') do
                TUI.indentLn 4 line

            TUI.text "  Continue? "
            TUI.promptYN true
            let key = TUI.readKeyChar ()
            TUI.blank ()

            if Char.ToLower(key) = 'n' then
                TUI.text "  Reason: "
                let reason = TUI.readLine ()
                warn (sprintf "User stopped after tool: %s" toolName)
                return Hooks.blockHook (if String.IsNullOrWhiteSpace(reason) then "Stopped by user" else reason)
            else
                return Hooks.continueHook

        | _ ->
            return Hooks.continueHook
    }

/// Hook that lets user add context before tool execution
let addContextHook: HookCallback =
    fun input toolUseId -> task {
        match input with
        | PreToolUseInput(_, _, _, toolName, _) ->
            TUI.blank ()
            TUI.magenta (sprintf "  Add context for '%s'? " toolName)
            TUI.promptYN false

            let key = TUI.readKeyChar ()
            TUI.blank ()

            if Char.ToLower(key) = 'y' then
                TUI.text "  Context: "
                let context = TUI.readLine ()
                debug (sprintf "User added context: %s" context)
                return Hooks.continueHook |> Hooks.withSystemMessage context
            else
                return Hooks.continueHook

        | _ ->
            return Hooks.continueHook
    }

// ============================================================================
// Demo: Human-in-the-Loop Session
// ============================================================================

/// Run an interactive session with human approval
let interactiveSession () = task {
    TUI.clear ()
    TUI.box "CLAUDE AGENT SDK - Human-in-the-Loop Demo" [
        "You will be prompted before Claude executes any tool."
        "Press A to allow, D to deny, M to modify input."
    ]
    TUI.blank ()

    // Configure with human-in-the-loop approval
    let hooks = Map.ofList [
        PostToolUse, [Hooks.matchAll [reviewResultsHook]]
    ]

    let options = {
        Options.defaults with
            CanUseTool = Some interactiveApproval
            PermissionPromptToolName = Some "stdio"
            Hooks = hooks
            SystemPrompt = Some (SystemPromptText "You are a helpful assistant. Use tools when needed.")
    }

    info "Connecting with human-in-the-loop approval..."
    let! connectResult = verboseConnect options

    match connectResult with
    | Error e ->
        logError e

    | Ok ctx ->
        // Recursive interactive loop (no mutable state)
        let rec loop ctx = task {
            TUI.prompt "You"

            let prompt = TUI.readLine ()

            match prompt with
            | null | "" ->
                return ()
            | prompt ->
                TUI.blank ()
                info (sprintf "Sending: %s" prompt)

                let! result = verboseClientQuery prompt ctx
                match result with
                | Ok (newCtx, messages) ->
                    TUI.blank ()
                    TUI.blueLn "Claude:"

                    for text in Client.getAssistantText messages do
                        TUI.indentLn 2 text

                    // Show any tool uses
                    let toolUses = Client.getToolUses messages
                    if not (List.isEmpty toolUses) then
                        TUI.darkGrayLn (sprintf "  [Used %d tool(s)]" (List.length toolUses))

                    TUI.blank ()

                    return! loop newCtx

                | Error e ->
                    logError e
                    TUI.blank ()
                    return! loop ctx
        }

        try
            TUI.textLn "Connected to Claude. Enter prompts (empty to quit):"
            TUI.blank ()
            do! loop ctx

            TUI.textLn "Session ended."

        finally
            info "Disconnecting..."
            Client.disconnect ctx |> Async.AwaitTask |> Async.RunSynchronously
}

/// Demo with smart approval (auto-approve safe tools)
let smartApprovalSession () = task {
    TUI.blank ()
    TUI.banner "Smart Approval Demo"
    TUI.textLn "Safe tools (Read, Glob, Grep, LS) are auto-approved."
    TUI.textLn "Other tools require manual approval."
    TUI.blank ()

    let options = {
        Options.defaults with
            CanUseTool = Some smartApproval
            PermissionPromptToolName = Some "stdio"
            SystemPrompt = Some (SystemPromptText "You are a helpful assistant.")
    }

    info "Connecting with smart approval..."
    let! connectResult = verboseConnect options

    match connectResult with
    | Error e ->
        logError e

    | Ok ctx ->
        try
            // This will likely use Read (auto-approved) and maybe Edit (needs approval)
            info "Asking Claude to read and suggest an improvement..."
            let! result = streamingClientQuery "Read the file CLAUDE.md and tell me what it's about" ctx

            match result with
            | Ok (_, messages) ->
                TUI.blank ()
                TUI.blueLn "Claude:"
                for text in Client.getAssistantText messages do
                    TUI.indentLn 2 text
            | Error e ->
                logError e

        finally
            info "Disconnecting..."
            Client.disconnect ctx |> Async.AwaitTask |> Async.RunSynchronously
}

/// Run all human-in-the-loop examples
let runAll () = task {
    info "Starting Human-in-the-Loop examples..."
    TUI.blank ()

    TUI.banner "Human-in-the-Loop Examples"
    TUI.textLn "This example demonstrates interactive approval for tool execution."
    TUI.blank ()

    // Show how to configure
    info "Configuration for interactive approval:"
    debug """
    let options = {
        Options.defaults with
            CanUseTool = Some interactiveApproval
            PermissionPromptToolName = Some "stdio"
    }
    """

    info "The interactiveApproval callback:"
    debug """
    - Displays tool name and input
    - Prompts user: [A]llow / [D]eny / [M]odify
    - Returns PermitAllow or PermitDeny based on user input
    - Supports modifying tool input before execution
    """

    TUI.blank ()

    // Demo the approval callback directly
    info "Testing the approval callback directly:"
    info "(Press A to allow, D to deny, or M to modify)"
    TUI.blank ()

    let testInput = Encode.object [
        "command", Encode.string "echo 'Hello World'"
    ]

    let! result = interactiveApproval "Bash" testInput { Signal = None; Suggestions = [] }

    match result with
    | PermitAllow(modified, _) ->
        match modified with
        | Some m -> success (sprintf "Result: Allowed with modified input: %s" (Encode.toString 0 m))
        | None -> success "Result: Allowed"
    | PermitDeny(msg, interrupt) ->
        warn (sprintf "Result: Denied - %s (interrupt: %b)" msg interrupt)

    TUI.blank ()

    // Run smart approval demo with actual Claude
    do! smartApprovalSession ()

    TUI.blank ()
    success "Human-in-the-Loop examples completed!"
    TUI.blank ()
    info "To run the full interactive session:"
    info "  do! Examples.HumanInTheLoop.interactiveSession ()"
}
