/// Example: Hooks and permission callbacks with verbose logging
module Examples.HooksAndPermissions

open System
open ClaudeAgentSdk
open Thoth.Json.Net
open Examples.Common

// ============================================================================
// Permission Callback Examples
// ============================================================================

/// Simple permission callback that allows all read operations
let allowReadsOnly: string -> JsonValue -> PermissionContext -> System.Threading.Tasks.Task<PermissionResult> =
    fun toolName input ctx -> task {
        match toolName with
        | "Read" | "Glob" | "Grep" ->
            debug (sprintf "[Permission] Allowing %s" toolName)
            return PermitAllow(None, None)

        | "Write" | "Edit" ->
            warn (sprintf "[Permission] Denying %s - read-only mode" toolName)
            return PermitDeny("This session is read-only", false)

        | "Bash" ->
            // Check if it's a safe command
            let command = Mcp.tryGetString "command" input |> Option.defaultValue ""
            if command.StartsWith("ls") || command.StartsWith("cat") || command.StartsWith("pwd") then
                debug (sprintf "[Permission] Allowing safe bash command: %s" command)
                return PermitAllow(None, None)
            else
                warn (sprintf "[Permission] Denying bash command: %s" command)
                return PermitDeny("Only read-only bash commands allowed", false)

        | _ ->
            debug (sprintf "[Permission] Allowing %s by default" toolName)
            return PermitAllow(None, None)
    }

/// Permission callback that modifies tool input
let sanitizeInputCallback: string -> JsonValue -> PermissionContext -> System.Threading.Tasks.Task<PermissionResult> =
    fun toolName input ctx -> task {
        match toolName with
        | "Bash" ->
            let command = Mcp.tryGetString "command" input |> Option.defaultValue ""

            // Remove any potentially dangerous patterns
            let sanitized =
                command
                    .Replace("rm -rf", "echo 'blocked: rm -rf'")
                    .Replace("sudo", "echo 'blocked: sudo'")

            if command <> sanitized then
                warn (sprintf "[Permission] Sanitized command: %s -> %s" command sanitized)
                let newInput = Encode.object ["command", Encode.string sanitized]
                return PermitAllow(Some newInput, None)
            else
                return PermitAllow(None, None)

        | _ ->
            return PermitAllow(None, None)
    }

/// Permission callback that uses suggestions
let suggestionAwareCallback: string -> JsonValue -> PermissionContext -> System.Threading.Tasks.Task<PermissionResult> =
    fun toolName input ctx -> task {
        debug (sprintf "[Permission] Tool: %s, Suggestions: %d" toolName (List.length ctx.Suggestions))

        // Check if there are any suggested permission updates
        for suggestion in ctx.Suggestions do
            debug (sprintf "  Suggestion type: %A" suggestion.Type)
            match suggestion.Rules with
            | Some rules ->
                for rule in rules do
                    debug (sprintf "    Rule: %s" rule.ToolName)
            | None -> ()

        return PermitAllow(None, None)
    }

/// Interactive permission callback that simulates user prompt
let interactiveCallback: string -> JsonValue -> PermissionContext -> System.Threading.Tasks.Task<PermissionResult> =
    fun toolName input ctx -> task {
        info "[Permission Request]"
        debug (sprintf "  Tool: %s" toolName)
        debug (sprintf "  Input: %s" (Encode.toString 2 input))
        debug "  Allow? (simulating 'yes')"

        // In a real app, you'd prompt the user here
        return PermitAllow(None, None)
    }

// ============================================================================
// Hook Examples
// ============================================================================

/// Pre-tool-use hook that logs all tool invocations
let loggingPreHook: HookCallback =
    fun input toolUseId -> task {
        match input with
        | PreToolUseInput(sessionId, _, cwd, toolName, toolInput) ->
            debug (sprintf "[Hook:PreToolUse] Session=%s Tool=%s CWD=%s" sessionId toolName cwd)
            return Hooks.continueHook
        | _ ->
            return Hooks.continueHook
    }

/// Pre-tool-use hook that blocks dangerous operations
let securityPreHook: HookCallback =
    fun input toolUseId -> task {
        match input with
        | PreToolUseInput(_, _, _, "Bash", toolInput) ->
            let command = Mcp.tryGetString "command" toolInput |> Option.defaultValue ""
            if command.Contains("rm -rf /") || command.Contains("format") then
                error (sprintf "[Hook:Security] BLOCKED dangerous command: %s" command)
                return Hooks.blockHook "Dangerous command blocked by security hook"
            else
                return Hooks.continueHook
        | _ ->
            return Hooks.continueHook
    }

/// Pre-tool-use hook that modifies permission decision
let permissionDecisionHook: HookCallback =
    fun input toolUseId -> task {
        match input with
        | PreToolUseInput(_, _, _, toolName, _) when toolName.StartsWith("Mcp") ->
            // Allow all MCP tools without asking
            debug (sprintf "[Hook] MCP tool auto-approved: %s" toolName)
            return Hooks.preToolUseResponse Allow (Some "MCP tools auto-approved")
        | _ ->
            return Hooks.continueHook
    }

/// Post-tool-use hook that adds context
let contextPostHook: HookCallback =
    fun input toolUseId -> task {
        match input with
        | PostToolUseInput(_, _, _, toolName, _, response) ->
            let responsePreview =
                let s = Encode.toString 0 response
                if s.Length > 100 then s.Substring(0, 100) + "..." else s
            debug (sprintf "[Hook:PostToolUse] %s completed: %s" toolName responsePreview)
            return Hooks.postToolUseResponse (Some "Tool execution logged")
        | _ ->
            return Hooks.continueHook
    }

/// User prompt submit hook that validates prompts
let promptValidationHook: HookCallback =
    fun input toolUseId -> task {
        match input with
        | UserPromptSubmitInput(_, _, _, prompt) ->
            if prompt.Length < 5 then
                warn "[Hook:Prompt] Prompt too short, adding context"
                return Hooks.userPromptSubmitResponse (Some "User prompt was very brief")
            else
                return Hooks.continueHook
        | _ ->
            return Hooks.continueHook
    }

/// Stop hook that performs cleanup
let cleanupStopHook: HookCallback =
    fun input toolUseId -> task {
        match input with
        | StopInput(sessionId, _, _, _) ->
            info (sprintf "[Hook:Stop] Session %s stopping, performing cleanup..." sessionId)
            // Perform any cleanup here
            return Hooks.continueHook
        | _ ->
            return Hooks.continueHook
    }

/// Async hook example
let asyncHook: HookCallback =
    fun input toolUseId -> task {
        debug "[Hook] Starting async processing..."
        // Return immediately, let hook run in background
        return Hooks.asyncHook (Some 30000) // 30 second timeout
    }

// ============================================================================
// Configuration Examples
// ============================================================================

/// Configure options with permission callback
let optionsWithPermissions () =
    { Options.defaults with
        CanUseTool = Some allowReadsOnly
        PermissionPromptToolName = Some "stdio"
    }

/// Configure options with hooks
let optionsWithHooks () =
    let hooks = Map.ofList [
        PreToolUse, [
            Hooks.matchAll [loggingPreHook; securityPreHook]
            Hooks.matchTool "Bash" [permissionDecisionHook]
        ]
        PostToolUse, [
            Hooks.matchAll [contextPostHook]
        ]
        UserPromptSubmit, [
            Hooks.matchAll [promptValidationHook]
        ]
        Stop, [
            Hooks.matchAll [cleanupStopHook]
        ]
    ]

    { Options.defaults with
        Hooks = hooks
    }

/// Configure options with both permissions and hooks
let fullSecurityConfig () =
    let hooks = Map.ofList [
        PreToolUse, [
            Hooks.matchAll [loggingPreHook]
            Hooks.matchTool "Bash" [securityPreHook] |> Hooks.withTimeout 5000.0
        ]
        PostToolUse, [
            Hooks.matchAll [contextPostHook]
        ]
    ]

    { Options.defaults with
        CanUseTool = Some sanitizeInputCallback
        PermissionPromptToolName = Some "stdio"
        Hooks = hooks
        PermissionMode = Some Default
    }

// ============================================================================
// Demo
// ============================================================================

/// Demonstrate hooks and permissions configuration
let demo () = task {
    TUI.banner "Hooks and Permissions Example"

    info "1. Permission Callback Configuration:"
    let opts1 = optionsWithPermissions ()
    let canUse = if opts1.CanUseTool.IsSome then "configured" else "not configured"
    debug (sprintf "   - CanUseTool: %s" canUse)
    debug (sprintf "   - PermissionPromptToolName: %A" opts1.PermissionPromptToolName)

    TUI.blank ()
    info "2. Hooks Configuration:"
    let opts2 = optionsWithHooks ()
    debug (sprintf "   - Hook events configured: %d" (Map.count opts2.Hooks))
    for KeyValue(event, matchers) in opts2.Hooks do
        debug (sprintf "     - %A: %d matchers" event (List.length matchers))

    TUI.blank ()
    info "3. Full Security Configuration:"
    let opts3 = fullSecurityConfig ()
    debug "   - Permission callback: configured"
    debug (sprintf "   - Hooks: %d events" (Map.count opts3.Hooks))
    debug (sprintf "   - Permission mode: %A" opts3.PermissionMode)

    TUI.blank ()
    info "4. Testing permission callback directly:"

    // Test the permission callback
    let testInput = Encode.object ["command", Encode.string "ls -la"]
    send "Testing: Bash 'ls -la'"
    let! result = allowReadsOnly "Bash" testInput { Signal = None; Suggestions = [] }
    match result with
    | PermitAllow _ -> success "   - Bash 'ls -la': ALLOWED"
    | PermitDeny (msg, _) -> warn (sprintf "   - Bash 'ls -la': DENIED (%s)" msg)

    let testInput2 = Encode.object ["command", Encode.string "rm -rf /"]
    send "Testing: Bash 'rm -rf /'"
    let! result2 = allowReadsOnly "Bash" testInput2 { Signal = None; Suggestions = [] }
    match result2 with
    | PermitAllow _ -> warn "   - Bash 'rm -rf /': ALLOWED (unexpected!)"
    | PermitDeny (msg, _) -> success (sprintf "   - Bash 'rm -rf /': DENIED (%s)" msg)

    TUI.blank ()
    info "5. Testing hook directly:"
    let hookInput = PreToolUseInput("sess_1", "/tmp/t", "/home", "Bash",
                                     Encode.object ["command", Encode.string "rm -rf /"])
    send "Testing: securityPreHook with 'rm -rf /'"
    let! hookResult = securityPreHook hookInput None
    match hookResult with
    | SyncHook(Some false, _, Some reason, _, _, _, _) ->
        success (sprintf "   - Security hook blocked: %s" reason)
    | _ ->
        warn "   - Security hook allowed (unexpected!)"
}

/// Demonstrate hooks with actual Claude connection
let liveDemo () = task {
    TUI.blank ()
    TUI.banner "Live Hooks Demo with Claude"

    let hooks = Map.ofList [
        PreToolUse, [
            Hooks.matchAll [loggingPreHook]
        ]
        PostToolUse, [
            Hooks.matchAll [contextPostHook]
        ]
    ]

    let options = {
        Options.defaults with
            Hooks = hooks
            CanUseTool = Some interactiveCallback
            PermissionPromptToolName = Some "stdio"
            SystemPrompt = Some (SystemPromptText "You are a helpful assistant. Be brief.")
    }

    info "Connecting with hooks and permissions..."
    let! connectResult = verboseConnect options

    match connectResult with
    | Error e ->
        logError e
    | Ok ctx ->
        try
            info "Sending query that may trigger tool use..."
            let! result = verboseClientQuery "What files are in the current directory? Use ls." ctx

            match result with
            | Ok (_, msgs) ->
                TUI.blank ()
                TUI.blueLn "Claude:"
                for t in Client.getAssistantText msgs do
                    TUI.indentLn 2 t
            | Error e ->
                logError e
        finally
            info "Disconnecting..."
            Client.disconnect ctx |> Async.AwaitTask |> Async.RunSynchronously
            success "Session ended."
}

/// Run all hook and permission examples
let runAll () = task {
    info "Starting Hooks and Permissions examples..."
    TUI.blank ()

    do! demo ()

    TUI.blank ()
    do! liveDemo ()

    TUI.blank ()
    success "Hooks and Permissions examples completed!"
}
