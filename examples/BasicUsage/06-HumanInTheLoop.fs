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
        Console.WriteLine()
        Console.ForegroundColor <- ConsoleColor.Yellow
        Console.WriteLine("═══════════════════════════════════════════════════════")
        Console.WriteLine("  TOOL APPROVAL REQUEST")
        Console.WriteLine("═══════════════════════════════════════════════════════")
        Console.ResetColor()

        Console.WriteLine($"  Tool: {toolName}")
        Console.WriteLine($"  Input:")

        // Pretty print the input
        let inputStr = Encode.toString 2 input
        for line in inputStr.Split('\n') do
            Console.WriteLine($"    {line}")

        Console.WriteLine()

        // Show suggestions if any
        if not (List.isEmpty ctx.Suggestions) then
            Console.ForegroundColor <- ConsoleColor.Cyan
            Console.WriteLine("  Suggested permissions:")
            for suggestion in ctx.Suggestions do
                Console.WriteLine($"    - {suggestion.Type}")
            Console.ResetColor()
            Console.WriteLine()

        // Prompt for decision
        Console.ForegroundColor <- ConsoleColor.Green
        Console.Write("  [A]llow / [D]eny / [M]odify input? ")
        Console.ResetColor()

        let key = Console.ReadKey(true)
        Console.WriteLine(key.KeyChar)

        match Char.ToLower(key.KeyChar) with
        | 'a' ->
            Console.ForegroundColor <- ConsoleColor.Green
            Console.WriteLine("  ✓ Approved")
            Console.ResetColor()
            debug $"User approved tool: {toolName}"
            return PermitAllow(None, None)

        | 'd' ->
            Console.Write("  Reason (optional): ")
            let reason = Console.ReadLine()
            let reason = if String.IsNullOrWhiteSpace(reason) then "Denied by user" else reason

            Console.Write("  Interrupt session? [y/N]: ")
            let interrupt = Console.ReadKey(true).KeyChar |> Char.ToLower = 'y'
            Console.WriteLine()

            Console.ForegroundColor <- ConsoleColor.Red
            Console.WriteLine($"  ✗ Denied: {reason}")
            Console.ResetColor()
            warn $"User denied tool: {toolName} - {reason}"
            return PermitDeny(reason, interrupt)

        | 'm' ->
            Console.WriteLine("  Enter modified input (JSON):")
            Console.Write("  > ")
            let modifiedJson = Console.ReadLine()

            match Decode.fromString Decode.value modifiedJson with
            | Ok newInput ->
                Console.ForegroundColor <- ConsoleColor.Green
                Console.WriteLine("  ✓ Approved with modified input")
                Console.ResetColor()
                debug $"User modified and approved tool: {toolName}"
                return PermitAllow(Some newInput, None)
            | Error e ->
                Console.ForegroundColor <- ConsoleColor.Red
                Console.WriteLine($"  Invalid JSON: {e}")
                Console.WriteLine("  Falling back to original input...")
                Console.ResetColor()
                return PermitAllow(None, None)

        | _ ->
            Console.ForegroundColor <- ConsoleColor.Gray
            Console.WriteLine("  (defaulting to Allow)")
            Console.ResetColor()
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
            Console.ForegroundColor <- ConsoleColor.DarkGray
            Console.WriteLine($"  [Auto-approved: {toolName}]")
            Console.ResetColor()
            debug $"Auto-approved safe tool: {toolName}"
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
            Console.WriteLine()
            Console.ForegroundColor <- ConsoleColor.Cyan
            Console.WriteLine($"  Tool '{toolName}' completed.")
            Console.ResetColor()

            let responseStr = Encode.toString 2 response
            let preview =
                if responseStr.Length > 500 then
                    responseStr.Substring(0, 500) + "...\n  [truncated]"
                else
                    responseStr

            Console.WriteLine("  Result:")
            for line in preview.Split('\n') do
                Console.WriteLine($"    {line}")

            Console.Write("  Continue? [Y/n]: ")
            let key = Console.ReadKey(true)
            Console.WriteLine()

            if Char.ToLower(key.KeyChar) = 'n' then
                Console.Write("  Reason: ")
                let reason = Console.ReadLine()
                warn $"User stopped after tool: {toolName}"
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
            Console.WriteLine()
            Console.ForegroundColor <- ConsoleColor.Magenta
            Console.Write($"  Add context for '{toolName}'? [y/N]: ")
            Console.ResetColor()

            let key = Console.ReadKey(true)
            Console.WriteLine()

            if Char.ToLower(key.KeyChar) = 'y' then
                Console.Write("  Context: ")
                let context = Console.ReadLine()
                debug $"User added context: {context}"
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
    Console.Clear()
    Console.ForegroundColor <- ConsoleColor.Cyan
    Console.WriteLine("╔═══════════════════════════════════════════════════════════╗")
    Console.WriteLine("║     CLAUDE AGENT SDK - Human-in-the-Loop Demo             ║")
    Console.WriteLine("╠═══════════════════════════════════════════════════════════╣")
    Console.WriteLine("║  You will be prompted before Claude executes any tool.    ║")
    Console.WriteLine("║  Press A to allow, D to deny, M to modify input.          ║")
    Console.WriteLine("╚═══════════════════════════════════════════════════════════╝")
    Console.ResetColor()
    Console.WriteLine()

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
            Console.ForegroundColor <- ConsoleColor.White
            Console.Write("You> ")
            Console.ResetColor()

            let prompt = Console.ReadLine()

            match prompt with
            | null | "" ->
                return ()
            | prompt ->
                Console.WriteLine()
                info (sprintf "Sending: %s" prompt)

                let! result = verboseClientQuery prompt ctx
                match result with
                | Ok (newCtx, messages) ->
                    Console.WriteLine()
                    Console.ForegroundColor <- ConsoleColor.Blue
                    Console.WriteLine("Claude:")
                    Console.ResetColor()

                    for text in Client.getAssistantText messages do
                        Console.WriteLine(sprintf "  %s" text)

                    // Show any tool uses
                    let toolUses = Client.getToolUses messages
                    if not (List.isEmpty toolUses) then
                        Console.ForegroundColor <- ConsoleColor.DarkGray
                        Console.WriteLine(sprintf "  [Used %d tool(s)]" (List.length toolUses))
                        Console.ResetColor()

                    Console.WriteLine()

                    return! loop newCtx

                | Error e ->
                    logError e
                    Console.WriteLine()
                    return! loop ctx
        }

        try
            Console.WriteLine("Connected to Claude. Enter prompts (empty to quit):")
            Console.WriteLine()
            do! loop ctx

            Console.WriteLine("Session ended.")

        finally
            info "Disconnecting..."
            Client.disconnect ctx |> Async.AwaitTask |> Async.RunSynchronously
}

/// Demo with smart approval (auto-approve safe tools)
let smartApprovalSession () = task {
    printfn ""
    printfn "=== Smart Approval Demo ==="
    printfn "Safe tools (Read, Glob, Grep, LS) are auto-approved."
    printfn "Other tools require manual approval."
    printfn ""

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
                Console.WriteLine()
                Console.ForegroundColor <- ConsoleColor.Blue
                Console.WriteLine("Claude:")
                Console.ResetColor()
                for text in Client.getAssistantText messages do
                    Console.WriteLine($"  {text}")
            | Error e ->
                logError e

        finally
            info "Disconnecting..."
            Client.disconnect ctx |> Async.AwaitTask |> Async.RunSynchronously
}

/// Run all human-in-the-loop examples
let runAll () = task {
    info "Starting Human-in-the-Loop examples..."
    printfn ""

    printfn "Human-in-the-Loop Examples"
    printfn "=========================="
    printfn ""
    printfn "This example demonstrates interactive approval for tool execution."
    printfn ""

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

    printfn ""

    // Demo the approval callback directly
    info "Testing the approval callback directly:"
    info "(Press A to allow, D to deny, or M to modify)"
    printfn ""

    let testInput = Encode.object [
        "command", Encode.string "echo 'Hello World'"
    ]

    let! result = interactiveApproval "Bash" testInput { Signal = None; Suggestions = [] }

    match result with
    | PermitAllow(modified, _) ->
        match modified with
        | Some m -> success $"Result: Allowed with modified input: {Encode.toString 0 m}"
        | None -> success "Result: Allowed"
    | PermitDeny(msg, interrupt) ->
        warn $"Result: Denied - {msg} (interrupt: {interrupt})"

    printfn ""

    // Run smart approval demo with actual Claude
    do! smartApprovalSession ()

    printfn ""
    success "Human-in-the-Loop examples completed!"
    printfn ""
    info "To run the full interactive session:"
    info "  do! Examples.HumanInTheLoop.interactiveSession ()"
}
