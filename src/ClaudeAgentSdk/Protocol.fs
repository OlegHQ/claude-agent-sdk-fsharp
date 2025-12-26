module ClaudeAgentSdk.Protocol

open System
open System.Collections.Concurrent
open System.Threading
open System.Threading.Tasks
open Thoth.Json.Net
open FsToolkit.ErrorHandling
open ClaudeAgentSdk

// ============================================================================
// Request ID Generation
// ============================================================================

let private requestCounter = ref 0

let private generateRequestId () : string =
    let count = Interlocked.Increment(requestCounter)
    let randomBytes = Array.zeroCreate<byte> 4
    Random.Shared.NextBytes(randomBytes)
    let hex = BitConverter.ToString(randomBytes).Replace("-", "").ToLower()
    $"req_{count}_{hex}"

// ============================================================================
// Control Protocol Handlers
// ============================================================================

/// Handle a CanUseTool control request
let private handleCanUseTool
    (canUseTool: (string -> JsonValue -> PermissionContext -> Task<PermissionResult>) option)
    (requestId: string)
    (toolName: string)
    (input: JsonValue)
    (suggestions: PermissionUpdate list)
    : Task<ControlResponse> = task {
    match canUseTool with
    | None ->
        return ErrorResponse(requestId, "canUseTool callback not provided")
    | Some callback ->
        try
            let ctx = { Signal = None; Suggestions = suggestions }
            let! result = callback toolName input ctx
            let responseJson = Json.Encode.permissionResult result
            return SuccessResponse(requestId, Some responseJson)
        with ex ->
            return ErrorResponse(requestId, ex.Message)
}

/// Handle a HookCallback control request
let private handleHookCallback
    (hookCallbacks: Map<string, HookCallback>)
    (requestId: string)
    (callbackId: string)
    (input: HookInput)
    (toolUseId: string option)
    : Task<ControlResponse> = task {
    match Map.tryFind callbackId hookCallbacks with
    | None ->
        return ErrorResponse(requestId, $"Hook callback not found: {callbackId}")
    | Some callback ->
        try
            let! output = callback input toolUseId
            let responseJson = Json.Encode.hookOutput output
            return SuccessResponse(requestId, Some responseJson)
        with ex ->
            return ErrorResponse(requestId, ex.Message)
}

/// Handle an MCP message control request
let private handleMcpMessage
    (mcpServers: Map<string, McpTool list>)
    (requestId: string)
    (serverName: string)
    (message: JsonValue)
    : Task<ControlResponse> = task {
    match Map.tryFind serverName mcpServers with
    | None ->
        return ErrorResponse(requestId, $"MCP server not found: {serverName}")
    | Some tools ->
        try
            let! response = Mcp.handleMcpRequest tools message
            return SuccessResponse(requestId, Some (Encode.object ["mcp_response", response]))
        with ex ->
            return ErrorResponse(requestId, ex.Message)
}

/// Handle an incoming control request
let handleControlRequest
    (canUseTool: (string -> JsonValue -> PermissionContext -> Task<PermissionResult>) option)
    (hookCallbacks: Map<string, HookCallback>)
    (mcpServers: Map<string, McpTool list>)
    (request: ControlRequest)
    : Task<ControlResponse> = task {
    match request with
    | CanUseToolRequest(requestId, toolName, input, suggestions) ->
        return! handleCanUseTool canUseTool requestId toolName input suggestions

    | HookCallbackRequest(requestId, callbackId, input, toolUseId) ->
        return! handleHookCallback hookCallbacks requestId callbackId input toolUseId

    | McpMessageRequest(requestId, serverName, message) ->
        return! handleMcpMessage mcpServers requestId serverName message

    | InitializeRequest requestId ->
        return SuccessResponse(requestId, None)

    | SetPermissionModeRequest(requestId, _) ->
        return SuccessResponse(requestId, None)

    | SetModelRequest(requestId, _) ->
        return SuccessResponse(requestId, None)

    | RewindFilesRequest(requestId, _) ->
        return SuccessResponse(requestId, None)

    | InterruptRequest requestId ->
        return SuccessResponse(requestId, None)
}

/// Handle control request from ClientContext
let handleControlRequestFromContext
    (ctx: ClientContext)
    (request: ControlRequest)
    : Task<ControlResponse> =
    let mcpServers =
        ctx.Options.McpServers
        |> Map.toList
        |> List.choose (fun (name, server) ->
            match server with
            | SdkServer(_, tools) -> Some (name, tools)
            | _ -> None)
        |> Map.ofList
    handleControlRequest ctx.Options.CanUseTool ctx.HookCallbacks mcpServers request

// ============================================================================
// Sending Control Commands
// ============================================================================

/// Send a control command and wait for response
let sendControlCommand
    (subtype: string)
    (data: (string * JsonValue) list)
    (conn: Connection)
    : Task<Result<unit, SdkError>> = taskResult {
    let requestId = generateRequestId()
    let request = Json.Encode.controlRequest subtype requestId data
    let json = Encode.toString 0 request
    do! Transport.write json conn
}

/// Send interrupt command
let sendInterrupt (conn: Connection) : Task<Result<unit, SdkError>> =
    sendControlCommand "interrupt" [] conn

/// Send set_permission_mode command
let sendSetPermissionMode (mode: PermissionMode) (conn: Connection) : Task<Result<unit, SdkError>> =
    let modeJson = Json.Encode.permissionMode mode
    sendControlCommand "set_permission_mode" ["mode", modeJson] conn

/// Send set_model command
let sendSetModel (model: string option) (conn: Connection) : Task<Result<unit, SdkError>> =
    let modelJson =
        match model with
        | Some m -> Encode.string m
        | None -> Encode.nil
    sendControlCommand "set_model" ["model", modelJson] conn

/// Send rewind_files command
let sendRewindFiles (userMessageId: string) (conn: Connection) : Task<Result<unit, SdkError>> =
    sendControlCommand "rewind_files" ["user_message_id", Encode.string userMessageId] conn

/// Send initialize command for streaming mode
let sendInitialize (conn: Connection) (options: Options) : Task<Result<unit, SdkError>> = taskResult {
    // Only send initialize if we have hooks
    if Map.isEmpty options.Hooks then
        return ()
    else
        let requestId = generateRequestId()

        // Build hooks configuration
        let hooksConfig =
            options.Hooks
            |> Map.toList
            |> List.map (fun (event, matchers) ->
                let eventName =
                    match event with
                    | PreToolUse -> "PreToolUse"
                    | PostToolUse -> "PostToolUse"
                    | UserPromptSubmit -> "UserPromptSubmit"
                    | Stop -> "Stop"
                    | SubagentStop -> "SubagentStop"
                    | PreCompact -> "PreCompact"
                let matcherConfigs =
                    matchers
                    |> List.mapi (fun i matcher ->
                        let callbackIds =
                            matcher.Hooks
                            |> List.mapi (fun j _ -> $"hook_{eventName}_{i}_{j}")
                        Encode.object [
                            match matcher.Matcher with
                            | Some m -> "matcher", Encode.string m
                            | None -> ()
                            "hookCallbackIds", Encode.list (List.map Encode.string callbackIds)
                            match matcher.Timeout with
                            | Some t -> "timeout", Encode.float t
                            | None -> ()
                        ])
                eventName, Encode.list matcherConfigs)

        let request = Json.Encode.controlRequest "initialize" requestId [
            "hooks", Encode.object hooksConfig
        ]
        let json = Encode.toString 0 request
        do! Transport.write json conn
}

// ============================================================================
// Encode Control Response
// ============================================================================

/// Encode a control response to JSON string
let encodeControlResponse (response: ControlResponse) : string =
    let json =
        match response with
        | SuccessResponse(requestId, resp) ->
            Json.Encode.controlResponseSuccess requestId resp
        | ErrorResponse(requestId, error) ->
            Json.Encode.controlResponseError requestId error
    Encode.toString 0 json
