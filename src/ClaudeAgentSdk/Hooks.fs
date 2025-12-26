module ClaudeAgentSdk.Hooks

open ClaudeAgentSdk

// ============================================================================
// Hook Configuration Builders
// ============================================================================

/// Build hook callback map from hook matchers for initialization
let buildHookCallbacks (hooks: Map<HookEvent, HookMatcher list>) : Map<string, HookCallback> =
    hooks
    |> Map.toList
    |> List.collect (fun (event, matchers) ->
        let eventName =
            match event with
            | PreToolUse -> "PreToolUse"
            | PostToolUse -> "PostToolUse"
            | UserPromptSubmit -> "UserPromptSubmit"
            | Stop -> "Stop"
            | SubagentStop -> "SubagentStop"
            | PreCompact -> "PreCompact"
        matchers
        |> List.mapi (fun i matcher ->
            matcher.Hooks
            |> List.mapi (fun j callback ->
                let callbackId = $"hook_{eventName}_{i}_{j}"
                (callbackId, callback)))
        |> List.concat)
    |> Map.ofList

// ============================================================================
// Hook Matcher DSL
// ============================================================================

/// Create a hook matcher for all tools
let matchAll (hooks: HookCallback list) : HookMatcher =
    { Matcher = None; Hooks = hooks; Timeout = None }

/// Create a hook matcher for specific tool pattern
let matchTool (pattern: string) (hooks: HookCallback list) : HookMatcher =
    { Matcher = Some pattern; Hooks = hooks; Timeout = None }

/// Add timeout to a hook matcher
let withTimeout (timeout: float) (matcher: HookMatcher) : HookMatcher =
    { matcher with Timeout = Some timeout }

// ============================================================================
// Hook Input Helpers
// ============================================================================

/// Get session ID from any hook input
let getSessionId (input: HookInput) : string =
    match input with
    | PreToolUseInput(sessionId, _, _, _, _) -> sessionId
    | PostToolUseInput(sessionId, _, _, _, _, _) -> sessionId
    | UserPromptSubmitInput(sessionId, _, _, _) -> sessionId
    | StopInput(sessionId, _, _, _) -> sessionId
    | SubagentStopInput(sessionId, _, _, _) -> sessionId
    | PreCompactInput(sessionId, _, _, _, _) -> sessionId

/// Get transcript path from any hook input
let getTranscriptPath (input: HookInput) : string =
    match input with
    | PreToolUseInput(_, transcriptPath, _, _, _) -> transcriptPath
    | PostToolUseInput(_, transcriptPath, _, _, _, _) -> transcriptPath
    | UserPromptSubmitInput(_, transcriptPath, _, _) -> transcriptPath
    | StopInput(_, transcriptPath, _, _) -> transcriptPath
    | SubagentStopInput(_, transcriptPath, _, _) -> transcriptPath
    | PreCompactInput(_, transcriptPath, _, _, _) -> transcriptPath

/// Get current working directory from any hook input
let getCwd (input: HookInput) : string =
    match input with
    | PreToolUseInput(_, _, cwd, _, _) -> cwd
    | PostToolUseInput(_, _, cwd, _, _, _) -> cwd
    | UserPromptSubmitInput(_, _, cwd, _) -> cwd
    | StopInput(_, _, cwd, _) -> cwd
    | SubagentStopInput(_, _, cwd, _) -> cwd
    | PreCompactInput(_, _, cwd, _, _) -> cwd

// ============================================================================
// Hook Output Builders
// ============================================================================

/// Create a continue response (allow the operation to proceed)
let continueHook : HookOutput =
    SyncHook(Some true, None, None, None, None, None, None)

/// Create a block response (stop the operation)
let blockHook (reason: string) : HookOutput =
    SyncHook(Some false, None, Some reason, None, None, None, None)

/// Create an async hook response (run in background)
let asyncHook (timeout: int option) : HookOutput =
    AsyncHook timeout

/// Create a pre-tool-use hook response with permission decision
let preToolUseResponse (decision: PermissionBehavior) (reason: string option) : HookOutput =
    let decisionStr =
        match decision with
        | Allow -> "allow"
        | Deny -> "deny"
        | Ask -> "ask"
    SyncHook(Some true, None, None, Some decisionStr, None, reason,
             Some (PreToolUseOutput(Some decision, reason, None)))

/// Create a post-tool-use hook response with additional context
let postToolUseResponse (context: string option) : HookOutput =
    SyncHook(Some true, None, None, None, None, None,
             Some (PostToolUseOutput context))

/// Create a user prompt submit hook response with additional context
let userPromptSubmitResponse (context: string option) : HookOutput =
    SyncHook(Some true, None, None, None, None, None,
             Some (UserPromptSubmitOutput context))

/// Create a hook response with a system message
let withSystemMessage (message: string) (output: HookOutput) : HookOutput =
    match output with
    | SyncHook(cont, suppress, stop, decision, _, reason, specific) ->
        SyncHook(cont, suppress, stop, decision, Some message, reason, specific)
    | AsyncHook _ -> output

/// Suppress output in a hook response
let suppressOutput (output: HookOutput) : HookOutput =
    match output with
    | SyncHook(cont, _, stop, decision, sysMsg, reason, specific) ->
        SyncHook(cont, Some true, stop, decision, sysMsg, reason, specific)
    | AsyncHook _ -> output

