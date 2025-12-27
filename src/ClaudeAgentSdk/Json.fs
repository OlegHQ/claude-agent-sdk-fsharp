module ClaudeAgentSdk.Json

open Thoth.Json.Net
open ClaudeAgentSdk

// ============================================================================
// Decoders
// ============================================================================

module Decode =
    /// Decode a ContentBlock from JSON
    let contentBlock: Decoder<ContentBlock> =
        Decode.field "type" Decode.string
        |> Decode.andThen (function
            | "text" ->
                Decode.field "text" Decode.string
                |> Decode.map Text

            | "thinking" ->
                Decode.map2
                    (fun t s -> Thinking(t, s))
                    (Decode.field "thinking" Decode.string)
                    (Decode.field "signature" Decode.string)

            | "tool_use" ->
                Decode.map3
                    (fun id name input -> ToolUse(id, name, input))
                    (Decode.field "id" Decode.string)
                    (Decode.field "name" Decode.string)
                    (Decode.field "input" Decode.value)

            | "tool_result" ->
                // Content can be: string, array of content blocks, or missing
                let contentDecoder : Decoder<string option> =
                    Decode.optional "content" (Decode.oneOf [
                        // Simple string content
                        Decode.string
                        // Array of content blocks - extract text from "text" type blocks
                        Decode.list (Decode.object (fun get ->
                            let typ = get.Required.Field "type" Decode.string
                            if typ = "text" then
                                Some (get.Required.Field "text" Decode.string)
                            else
                                None
                        )) |> Decode.map (fun textOpts ->
                            let texts = textOpts |> List.choose id
                            if List.isEmpty texts then ""
                            else String.concat "\n" texts
                        )
                    ])

                Decode.map3
                    (fun id content isErr -> ToolResult(id, content, isErr))
                    (Decode.field "tool_use_id" Decode.string)
                    contentDecoder
                    (Decode.optional "is_error" Decode.bool)

            | t -> Decode.fail $"Unknown content block type: {t}")

    /// Decode assistant message error
    let assistantMessageError: Decoder<AssistantMessageError> =
        Decode.string
        |> Decode.andThen (function
            | "authentication_failed" -> Decode.succeed AuthenticationFailed
            | "billing_error" -> Decode.succeed BillingError
            | "rate_limit" -> Decode.succeed RateLimit
            | "invalid_request" -> Decode.succeed InvalidRequest
            | "server_error" -> Decode.succeed ServerError
            | "unknown" -> Decode.succeed UnknownError
            | e -> Decode.fail $"Unknown assistant message error: {e}")

    /// Decode UserMessage content (can be list of blocks or string)
    let private userMessageContent: Decoder<ContentBlock list> =
        Decode.oneOf [
            Decode.list contentBlock
            Decode.string |> Decode.map (fun s -> [Text s])
        ]

    /// Decode UserMessage
    let userMessage: Decoder<UserMessage> =
        Decode.object (fun get -> {
            Content = get.Required.At ["message"; "content"] userMessageContent
            Uuid = get.Optional.Field "uuid" Decode.string
            ParentToolUseId = get.Optional.Field "parent_tool_use_id" Decode.string
        })

    /// Decode AssistantMessage
    let assistantMessage: Decoder<AssistantMessage> =
        Decode.object (fun get -> {
            Content = get.Required.At ["message"; "content"] (Decode.list contentBlock)
            Model = get.Required.At ["message"; "model"] Decode.string
            ParentToolUseId = get.Optional.Field "parent_tool_use_id" Decode.string
            Error = get.Optional.At ["message"; "error"] assistantMessageError
        })

    /// Decode SystemMessage
    let systemMessage: Decoder<SystemMessage> =
        Decode.object (fun get -> {
            Subtype = get.Required.Field "subtype" Decode.string
            Data = get.Optional.Field "data" Decode.value |> Option.defaultValue Encode.nil
        })

    /// Decode ResultMessage
    let resultMessage: Decoder<ResultMessage> =
        Decode.object (fun get -> {
            Subtype = get.Required.Field "subtype" Decode.string
            DurationMs = get.Required.Field "duration_ms" Decode.int
            DurationApiMs = get.Required.Field "duration_api_ms" Decode.int
            IsError = get.Required.Field "is_error" Decode.bool
            NumTurns = get.Required.Field "num_turns" Decode.int
            SessionId = get.Required.Field "session_id" Decode.string
            TotalCostUsd = get.Optional.Field "total_cost_usd" Decode.float
            Usage = get.Optional.Field "usage" Decode.value
            Result = get.Optional.Field "result" Decode.string
            StructuredOutput = get.Optional.Field "structured_output" Decode.value
        })

    /// Decode StreamEvent
    let streamEvent: Decoder<StreamEvent> =
        Decode.object (fun get -> {
            Uuid = get.Required.Field "uuid" Decode.string
            SessionId = get.Required.Field "session_id" Decode.string
            Event = get.Required.Field "event" Decode.value
            ParentToolUseId = get.Optional.Field "parent_tool_use_id" Decode.string
        })

    /// Decode Message (discriminated by "type" field)
    let message: Decoder<Message> =
        Decode.field "type" Decode.string
        |> Decode.andThen (function
            | "user" -> userMessage |> Decode.map UserMsg
            | "assistant" -> assistantMessage |> Decode.map AssistantMsg
            | "system" -> systemMessage |> Decode.map SystemMsg
            | "result" -> resultMessage |> Decode.map ResultMsg
            | "stream_event" -> streamEvent |> Decode.map StreamMsg
            | t -> Decode.fail $"Unknown message type: {t}")

    /// Decode PermissionBehavior
    let permissionBehavior: Decoder<PermissionBehavior> =
        Decode.string
        |> Decode.andThen (function
            | "allow" -> Decode.succeed Allow
            | "deny" -> Decode.succeed Deny
            | "ask" -> Decode.succeed Ask
            | b -> Decode.fail $"Unknown permission behavior: {b}")

    /// Decode PermissionMode
    let permissionMode: Decoder<PermissionMode> =
        Decode.string
        |> Decode.andThen (function
            | "default" -> Decode.succeed Default
            | "acceptEdits" -> Decode.succeed AcceptEdits
            | "plan" -> Decode.succeed Plan
            | "bypassPermissions" -> Decode.succeed BypassPermissions
            | m -> Decode.fail $"Unknown permission mode: {m}")

    /// Decode PermissionUpdateDestination
    let permissionUpdateDestination: Decoder<PermissionUpdateDestination> =
        Decode.string
        |> Decode.andThen (function
            | "userSettings" -> Decode.succeed UserSettings
            | "projectSettings" -> Decode.succeed ProjectSettings
            | "localSettings" -> Decode.succeed LocalSettings
            | "session" -> Decode.succeed Session
            | d -> Decode.fail $"Unknown permission update destination: {d}")

    /// Decode PermissionRuleValue
    let permissionRuleValue: Decoder<PermissionRuleValue> =
        Decode.object (fun get -> {
            ToolName = get.Required.Field "toolName" Decode.string
            RuleContent = get.Optional.Field "ruleContent" Decode.string
        })

    /// Decode PermissionUpdateType
    let permissionUpdateType: Decoder<PermissionUpdateType> =
        Decode.string
        |> Decode.andThen (function
            | "addRules" -> Decode.succeed AddRules
            | "replaceRules" -> Decode.succeed ReplaceRules
            | "removeRules" -> Decode.succeed RemoveRules
            | "setMode" -> Decode.succeed SetMode
            | "addDirectories" -> Decode.succeed AddDirectories
            | "removeDirectories" -> Decode.succeed RemoveDirectories
            | t -> Decode.fail $"Unknown permission update type: {t}")

    /// Decode PermissionUpdate
    let permissionUpdate: Decoder<PermissionUpdate> =
        Decode.object (fun get -> {
            Type = get.Required.Field "type" permissionUpdateType
            Rules = get.Optional.Field "rules" (Decode.list permissionRuleValue)
            Behavior = get.Optional.Field "behavior" permissionBehavior
            Mode = get.Optional.Field "mode" permissionMode
            Directories = get.Optional.Field "directories" (Decode.list Decode.string)
            Destination = get.Optional.Field "destination" permissionUpdateDestination
        })

    /// Decode HookEvent
    let hookEvent: Decoder<HookEvent> =
        Decode.string
        |> Decode.andThen (function
            | "PreToolUse" -> Decode.succeed PreToolUse
            | "PostToolUse" -> Decode.succeed PostToolUse
            | "UserPromptSubmit" -> Decode.succeed UserPromptSubmit
            | "Stop" -> Decode.succeed Stop
            | "SubagentStop" -> Decode.succeed SubagentStop
            | "PreCompact" -> Decode.succeed PreCompact
            | e -> Decode.fail $"Unknown hook event: {e}")

    /// Decode HookInput (discriminated by hook_event_name)
    let hookInput: Decoder<HookInput> =
        Decode.field "hook_event_name" Decode.string
        |> Decode.andThen (function
            | "PreToolUse" ->
                Decode.map5
                    (fun sid tp cwd tn ti -> PreToolUseInput(sid, tp, cwd, tn, ti))
                    (Decode.field "session_id" Decode.string)
                    (Decode.field "transcript_path" Decode.string)
                    (Decode.field "cwd" Decode.string)
                    (Decode.field "tool_name" Decode.string)
                    (Decode.field "tool_input" Decode.value)

            | "PostToolUse" ->
                Decode.map6
                    (fun sid tp cwd tn ti tr -> PostToolUseInput(sid, tp, cwd, tn, ti, tr))
                    (Decode.field "session_id" Decode.string)
                    (Decode.field "transcript_path" Decode.string)
                    (Decode.field "cwd" Decode.string)
                    (Decode.field "tool_name" Decode.string)
                    (Decode.field "tool_input" Decode.value)
                    (Decode.field "tool_response" Decode.value)

            | "UserPromptSubmit" ->
                Decode.map4
                    (fun sid tp cwd p -> UserPromptSubmitInput(sid, tp, cwd, p))
                    (Decode.field "session_id" Decode.string)
                    (Decode.field "transcript_path" Decode.string)
                    (Decode.field "cwd" Decode.string)
                    (Decode.field "prompt" Decode.string)

            | "Stop" ->
                Decode.map4
                    (fun sid tp cwd sha -> StopInput(sid, tp, cwd, sha))
                    (Decode.field "session_id" Decode.string)
                    (Decode.field "transcript_path" Decode.string)
                    (Decode.field "cwd" Decode.string)
                    (Decode.field "stop_hook_active" Decode.bool)

            | "SubagentStop" ->
                Decode.map4
                    (fun sid tp cwd sha -> SubagentStopInput(sid, tp, cwd, sha))
                    (Decode.field "session_id" Decode.string)
                    (Decode.field "transcript_path" Decode.string)
                    (Decode.field "cwd" Decode.string)
                    (Decode.field "stop_hook_active" Decode.bool)

            | "PreCompact" ->
                Decode.map5
                    (fun sid tp cwd tr ci -> PreCompactInput(sid, tp, cwd, tr, ci))
                    (Decode.field "session_id" Decode.string)
                    (Decode.field "transcript_path" Decode.string)
                    (Decode.field "cwd" Decode.string)
                    (Decode.field "trigger" Decode.string)
                    (Decode.optional "custom_instructions" Decode.string)

            | e -> Decode.fail $"Unknown hook event name: {e}")

    /// Decode ControlRequest
    let controlRequest: Decoder<ControlRequest> =
        Decode.object (fun get ->
            let requestId = get.Required.Field "request_id" Decode.string
            let request = get.Required.Field "request" Decode.value
            match Decode.fromValue "" (Decode.field "subtype" Decode.string) request with
            | Ok "can_use_tool" ->
                let toolName = get.Required.At ["request"; "tool_name"] Decode.string
                let input = get.Required.At ["request"; "input"] Decode.value
                let suggestions =
                    get.Optional.At ["request"; "permission_suggestions"] (Decode.list permissionUpdate)
                    |> Option.defaultValue []
                CanUseToolRequest(requestId, toolName, input, suggestions)

            | Ok "hook_callback" ->
                let callbackId = get.Required.At ["request"; "callback_id"] Decode.string
                let inputJson = get.Required.At ["request"; "input"] Decode.value
                let toolUseId = get.Optional.At ["request"; "tool_use_id"] Decode.string
                match Decode.fromValue "" hookInput inputJson with
                | Ok input -> HookCallbackRequest(requestId, callbackId, input, toolUseId)
                | Error _ -> HookCallbackRequest(requestId, callbackId, StopInput("", "", "", false), toolUseId)

            | Ok "mcp_message" ->
                let serverName = get.Required.At ["request"; "server_name"] Decode.string
                let message = get.Required.At ["request"; "message"] Decode.value
                McpMessageRequest(requestId, serverName, message)

            | Ok "initialize" ->
                InitializeRequest requestId

            | Ok "set_permission_mode" ->
                let modeJson = get.Required.At ["request"; "mode"] Decode.value
                match Decode.fromValue "" permissionMode modeJson with
                | Ok mode -> SetPermissionModeRequest(requestId, mode)
                | Error _ -> SetPermissionModeRequest(requestId, Default)

            | Ok "set_model" ->
                let model = get.Optional.At ["request"; "model"] Decode.string
                SetModelRequest(requestId, model)

            | Ok "rewind_files" ->
                let userMessageId = get.Required.At ["request"; "user_message_id"] Decode.string
                RewindFilesRequest(requestId, userMessageId)

            | Ok "interrupt" ->
                InterruptRequest requestId

            | Ok subtype ->
                InterruptRequest requestId  // fallback

            | Error _ ->
                InterruptRequest requestId)

    /// Decode ControlResponse
    let controlResponse: Decoder<ControlResponse> =
        Decode.object (fun get ->
            let response = get.Required.Field "response" Decode.value
            match Decode.fromValue "" (Decode.field "subtype" Decode.string) response with
            | Ok "success" ->
                let requestId = get.Required.At ["response"; "request_id"] Decode.string
                let resp = get.Optional.At ["response"; "response"] Decode.value
                SuccessResponse(requestId, resp)
            | Ok "error" ->
                let requestId = get.Required.At ["response"; "request_id"] Decode.string
                let error = get.Required.At ["response"; "error"] Decode.string
                ErrorResponse(requestId, error)
            | _ ->
                ErrorResponse("", "Unknown control response"))

    /// Decode a line as either Message, ControlRequest, or ControlResponse
    let parsedLine: Decoder<ParsedLine> =
        Decode.field "type" Decode.string
        |> Decode.andThen (function
            | "control_request" ->
                controlRequest |> Decode.map ControlRequestLine
            | "control_response" ->
                controlResponse |> Decode.map ControlResponseLine
            | _ ->
                message |> Decode.map RegularMessage)

// ============================================================================
// Encoders
// ============================================================================

module Encode =
    /// Encode a user message for sending to CLI
    let userMessage (prompt: string) (sessionId: string) : JsonValue =
        Encode.object [
            "type", Encode.string "user"
            "message", Encode.object [
                "role", Encode.string "user"
                "content", Encode.string prompt
            ]
            "session_id", Encode.string sessionId
        ]

    /// Encode PermissionBehavior
    let permissionBehavior (b: PermissionBehavior) : JsonValue =
        match b with
        | Allow -> Encode.string "allow"
        | Deny -> Encode.string "deny"
        | Ask -> Encode.string "ask"

    /// Encode PermissionMode
    let permissionMode (m: PermissionMode) : JsonValue =
        match m with
        | Default -> Encode.string "default"
        | AcceptEdits -> Encode.string "acceptEdits"
        | Plan -> Encode.string "plan"
        | BypassPermissions -> Encode.string "bypassPermissions"

    /// Encode PermissionUpdateDestination
    let permissionUpdateDestination (d: PermissionUpdateDestination) : JsonValue =
        match d with
        | UserSettings -> Encode.string "userSettings"
        | ProjectSettings -> Encode.string "projectSettings"
        | LocalSettings -> Encode.string "localSettings"
        | Session -> Encode.string "session"

    /// Encode PermissionUpdateType
    let permissionUpdateType (t: PermissionUpdateType) : JsonValue =
        match t with
        | AddRules -> Encode.string "addRules"
        | ReplaceRules -> Encode.string "replaceRules"
        | RemoveRules -> Encode.string "removeRules"
        | SetMode -> Encode.string "setMode"
        | AddDirectories -> Encode.string "addDirectories"
        | RemoveDirectories -> Encode.string "removeDirectories"

    /// Encode PermissionRuleValue
    let permissionRuleValue (r: PermissionRuleValue) : JsonValue =
        Encode.object [
            "toolName", Encode.string r.ToolName
            match r.RuleContent with
            | Some rc -> "ruleContent", Encode.string rc
            | None -> ()
        ]

    /// Encode PermissionUpdate
    let permissionUpdate (u: PermissionUpdate) : JsonValue =
        Encode.object [
            "type", permissionUpdateType u.Type
            match u.Rules with
            | Some rules -> "rules", Encode.list (List.map permissionRuleValue rules)
            | None -> ()
            match u.Behavior with
            | Some b -> "behavior", permissionBehavior b
            | None -> ()
            match u.Mode with
            | Some m -> "mode", permissionMode m
            | None -> ()
            match u.Directories with
            | Some dirs -> "directories", Encode.list (List.map Encode.string dirs)
            | None -> ()
            match u.Destination with
            | Some d -> "destination", permissionUpdateDestination d
            | None -> ()
        ]

    /// Encode PermissionResult for control response
    let permissionResult (r: PermissionResult) : JsonValue =
        match r with
        | PermitAllow(updatedInput, updatedPermissions) ->
            Encode.object [
                "behavior", Encode.string "allow"
                match updatedInput with
                | Some input -> "updatedInput", input
                | None -> ()
                match updatedPermissions with
                | Some perms -> "updatedPermissions", Encode.list (List.map permissionUpdate perms)
                | None -> ()
            ]
        | PermitDeny(message, interrupt) ->
            Encode.object [
                "behavior", Encode.string "deny"
                "message", Encode.string message
                if interrupt then "interrupt", Encode.bool true
            ]

    /// Encode HookOutput for control response
    let hookOutput (o: HookOutput) : JsonValue =
        match o with
        | AsyncHook timeout ->
            Encode.object [
                "async", Encode.bool true
                match timeout with
                | Some t -> "asyncTimeout", Encode.int t
                | None -> ()
            ]
        | SyncHook(continue', suppressOutput, stopReason, decision, systemMessage, reason, hookSpecificOutput) ->
            Encode.object [
                match continue' with
                | Some c -> "continue", Encode.bool c
                | None -> ()
                match suppressOutput with
                | Some s -> "suppressOutput", Encode.bool s
                | None -> ()
                match stopReason with
                | Some r -> "stopReason", Encode.string r
                | None -> ()
                match decision with
                | Some d -> "decision", Encode.string d
                | None -> ()
                match systemMessage with
                | Some m -> "systemMessage", Encode.string m
                | None -> ()
                match reason with
                | Some r -> "reason", Encode.string r
                | None -> ()
                match hookSpecificOutput with
                | Some (PreToolUseOutput(pd, r, ui)) ->
                    "hookSpecificOutput", Encode.object [
                        "hookEventName", Encode.string "PreToolUse"
                        match pd with
                        | Some Allow -> "permissionDecision", Encode.string "allow"
                        | Some Deny -> "permissionDecision", Encode.string "deny"
                        | Some Ask -> "permissionDecision", Encode.string "ask"
                        | None -> ()
                        match r with
                        | Some reason -> "permissionDecisionReason", Encode.string reason
                        | None -> ()
                        match ui with
                        | Some input -> "updatedInput", input
                        | None -> ()
                    ]
                | Some (PostToolUseOutput ac) ->
                    "hookSpecificOutput", Encode.object [
                        "hookEventName", Encode.string "PostToolUse"
                        match ac with
                        | Some c -> "additionalContext", Encode.string c
                        | None -> ()
                    ]
                | Some (UserPromptSubmitOutput ac) ->
                    "hookSpecificOutput", Encode.object [
                        "hookEventName", Encode.string "UserPromptSubmit"
                        match ac with
                        | Some c -> "additionalContext", Encode.string c
                        | None -> ()
                    ]
                | None -> ()
            ]

    /// Encode a control request
    let controlRequest (subtype: string) (requestId: string) (data: (string * JsonValue) list) : JsonValue =
        Encode.object [
            "type", Encode.string "control_request"
            "request_id", Encode.string requestId
            "request", Encode.object (("subtype", Encode.string subtype) :: data)
        ]

    /// Encode a control response (success)
    let controlResponseSuccess (requestId: string) (response: JsonValue option) : JsonValue =
        Encode.object [
            "type", Encode.string "control_response"
            "response", Encode.object [
                "subtype", Encode.string "success"
                "request_id", Encode.string requestId
                "response", response |> Option.defaultValue Encode.nil
            ]
        ]

    /// Encode a control response (error)
    let controlResponseError (requestId: string) (error: string) : JsonValue =
        Encode.object [
            "type", Encode.string "control_response"
            "response", Encode.object [
                "subtype", Encode.string "error"
                "request_id", Encode.string requestId
                "error", Encode.string error
            ]
        ]

    /// Encode Schema to JSON Schema
    let rec schema (s: Schema) : JsonValue =
        match s with
        | SString desc ->
            Encode.object [
                "type", Encode.string "string"
                match desc with
                | Some d -> "description", Encode.string d
                | None -> ()
            ]
        | SNumber desc ->
            Encode.object [
                "type", Encode.string "number"
                match desc with
                | Some d -> "description", Encode.string d
                | None -> ()
            ]
        | SBool desc ->
            Encode.object [
                "type", Encode.string "boolean"
                match desc with
                | Some d -> "description", Encode.string d
                | None -> ()
            ]
        | SObject (desc, props) ->
            let properties =
                props
                |> List.map (fun (name, s, _) -> name, schema s)
            let required =
                props
                |> List.filter (fun (_, _, req) -> req)
                |> List.map (fun (name, _, _) -> name)
            Encode.object [
                "type", Encode.string "object"
                match desc with
                | Some d -> "description", Encode.string d
                | None -> ()
                "properties", Encode.object properties
                if not (List.isEmpty required) then
                    "required", Encode.list (List.map Encode.string required)
            ]
        | SArray (desc, itemSchema) ->
            Encode.object [
                "type", Encode.string "array"
                match desc with
                | Some d -> "description", Encode.string d
                | None -> ()
                "items", schema itemSchema
            ]
        | SAny ->
            Encode.object []

    /// Encode HookEvent
    let hookEvent (e: HookEvent) : JsonValue =
        match e with
        | PreToolUse -> Encode.string "PreToolUse"
        | PostToolUse -> Encode.string "PostToolUse"
        | UserPromptSubmit -> Encode.string "UserPromptSubmit"
        | Stop -> Encode.string "Stop"
        | SubagentStop -> Encode.string "SubagentStop"
        | PreCompact -> Encode.string "PreCompact"

    /// Encode AgentModel
    let agentModel (m: AgentModel) : JsonValue =
        match m with
        | Sonnet -> Encode.string "sonnet"
        | Opus -> Encode.string "opus"
        | Haiku -> Encode.string "haiku"
        | Inherit -> Encode.string "inherit"

    /// Encode AgentDefinition
    let agentDefinition (a: AgentDefinition) : JsonValue =
        Encode.object [
            "description", Encode.string a.Description
            "prompt", Encode.string a.Prompt
            match a.Tools with
            | Some tools -> "tools", Encode.list (List.map Encode.string tools)
            | None -> ()
            match a.Model with
            | Some m -> "model", agentModel m
            | None -> ()
        ]

    /// Encode SettingSource
    let settingSource (s: SettingSource) : string =
        match s with
        | UserSource -> "user"
        | ProjectSource -> "project"
        | LocalSource -> "local"

    /// Encode SandboxNetworkConfig
    let sandboxNetworkConfig (c: SandboxNetworkConfig) : JsonValue =
        Encode.object [
            match c.AllowUnixSockets with
            | Some sockets -> "allowUnixSockets", Encode.list (List.map Encode.string sockets)
            | None -> ()
            match c.AllowAllUnixSockets with
            | Some b -> "allowAllUnixSockets", Encode.bool b
            | None -> ()
            match c.AllowLocalBinding with
            | Some b -> "allowLocalBinding", Encode.bool b
            | None -> ()
            match c.HttpProxyPort with
            | Some p -> "httpProxyPort", Encode.int p
            | None -> ()
            match c.SocksProxyPort with
            | Some p -> "socksProxyPort", Encode.int p
            | None -> ()
        ]

    /// Encode SandboxIgnoreViolations
    let sandboxIgnoreViolations (v: SandboxIgnoreViolations) : JsonValue =
        Encode.object [
            match v.File with
            | Some files -> "file", Encode.list (List.map Encode.string files)
            | None -> ()
            match v.Network with
            | Some hosts -> "network", Encode.list (List.map Encode.string hosts)
            | None -> ()
        ]

    /// Encode SandboxSettings
    let sandboxSettings (s: SandboxSettings) : JsonValue =
        Encode.object [
            match s.Enabled with
            | Some b -> "enabled", Encode.bool b
            | None -> ()
            match s.AutoAllowBashIfSandboxed with
            | Some b -> "autoAllowBashIfSandboxed", Encode.bool b
            | None -> ()
            match s.ExcludedCommands with
            | Some cmds -> "excludedCommands", Encode.list (List.map Encode.string cmds)
            | None -> ()
            match s.AllowUnsandboxedCommands with
            | Some b -> "allowUnsandboxedCommands", Encode.bool b
            | None -> ()
            match s.Network with
            | Some nc -> "network", sandboxNetworkConfig nc
            | None -> ()
            match s.IgnoreViolations with
            | Some iv -> "ignoreViolations", sandboxIgnoreViolations iv
            | None -> ()
            match s.EnableWeakerNestedSandbox with
            | Some b -> "enableWeakerNestedSandbox", Encode.bool b
            | None -> ()
        ]
