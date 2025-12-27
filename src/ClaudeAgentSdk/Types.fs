namespace ClaudeAgentSdk

open System
open System.Diagnostics
open System.IO
open System.Threading.Tasks
open Thoth.Json.Net

// ============================================================================
// Error Types
// ============================================================================

type SdkError =
    | CliNotFound of message: string
    | ConnectionFailed of message: string
    | ProcessFailed of exitCode: int * stderr: string
    | JsonError of message: string
    | ParseError of message: string
    | ProtocolError of message: string
    | Timeout of message: string

// ============================================================================
// Content Blocks
// ============================================================================

type ContentBlock =
    | Text of text: string
    | Thinking of thinking: string * signature: string
    | ToolUse of id: string * name: string * input: JsonValue
    | ToolResult of toolUseId: string * content: string option * isError: bool option

// ============================================================================
// Messages
// ============================================================================

type AssistantMessageError =
    | AuthenticationFailed
    | BillingError
    | RateLimit
    | InvalidRequest
    | ServerError
    | UnknownError

type UserMessage = {
    Content: ContentBlock list
    Uuid: string option
    ParentToolUseId: string option
}

type AssistantMessage = {
    Content: ContentBlock list
    Model: string
    ParentToolUseId: string option
    Error: AssistantMessageError option
}

type SystemMessage = {
    Subtype: string
    Data: JsonValue
}

type ResultMessage = {
    Subtype: string
    DurationMs: int
    DurationApiMs: int
    IsError: bool
    NumTurns: int
    SessionId: string
    TotalCostUsd: float option
    Usage: JsonValue option
    Result: string option
    StructuredOutput: JsonValue option
}

type StreamEvent = {
    Uuid: string
    SessionId: string
    Event: JsonValue
    ParentToolUseId: string option
}

type Message =
    | UserMsg of UserMessage
    | AssistantMsg of AssistantMessage
    | SystemMsg of SystemMessage
    | ResultMsg of ResultMessage
    | StreamMsg of StreamEvent

// ============================================================================
// Permission Types
// ============================================================================

type PermissionBehavior =
    | Allow
    | Deny
    | Ask

type PermissionMode =
    | Default
    | AcceptEdits
    | Plan
    | BypassPermissions

type PermissionUpdateDestination =
    | UserSettings
    | ProjectSettings
    | LocalSettings
    | Session

type PermissionRuleValue = {
    ToolName: string
    RuleContent: string option
}

type PermissionUpdateType =
    | AddRules
    | ReplaceRules
    | RemoveRules
    | SetMode
    | AddDirectories
    | RemoveDirectories

type PermissionUpdate = {
    Type: PermissionUpdateType
    Rules: PermissionRuleValue list option
    Behavior: PermissionBehavior option
    Mode: PermissionMode option
    Directories: string list option
    Destination: PermissionUpdateDestination option
}

type PermissionContext = {
    Signal: obj option
    Suggestions: PermissionUpdate list
}

type PermissionResult =
    | PermitAllow of updatedInput: JsonValue option * updatedPermissions: PermissionUpdate list option
    | PermitDeny of message: string * interrupt: bool

// ============================================================================
// Hook Types
// ============================================================================

type HookEvent =
    | PreToolUse
    | PostToolUse
    | UserPromptSubmit
    | Stop
    | SubagentStop
    | PreCompact

type HookInput =
    | PreToolUseInput of sessionId: string * transcriptPath: string * cwd: string * toolName: string * toolInput: JsonValue
    | PostToolUseInput of sessionId: string * transcriptPath: string * cwd: string * toolName: string * toolInput: JsonValue * toolResponse: JsonValue
    | UserPromptSubmitInput of sessionId: string * transcriptPath: string * cwd: string * prompt: string
    | StopInput of sessionId: string * transcriptPath: string * cwd: string * stopHookActive: bool
    | SubagentStopInput of sessionId: string * transcriptPath: string * cwd: string * stopHookActive: bool
    | PreCompactInput of sessionId: string * transcriptPath: string * cwd: string * trigger: string * customInstructions: string option

type HookSpecificOutput =
    | PreToolUseOutput of permissionDecision: PermissionBehavior option * reason: string option * updatedInput: JsonValue option
    | PostToolUseOutput of additionalContext: string option
    | UserPromptSubmitOutput of additionalContext: string option

type HookOutput =
    | AsyncHook of timeout: int option
    | SyncHook of
        continue': bool option *
        suppressOutput: bool option *
        stopReason: string option *
        decision: string option *
        systemMessage: string option *
        reason: string option *
        hookSpecificOutput: HookSpecificOutput option

type HookCallback = HookInput -> string option -> Task<HookOutput>

type HookMatcher = {
    Matcher: string option
    Hooks: HookCallback list
    Timeout: float option
}

// ============================================================================
// Schema Types (for typed MCP tools)
// ============================================================================

type Schema =
    | SString of description: string option
    | SNumber of description: string option
    | SBool of description: string option
    | SObject of description: string option * properties: (string * Schema * bool) list
    | SArray of description: string option * itemSchema: Schema
    | SAny

// ============================================================================
// MCP Types
// ============================================================================

type ToolContent =
    | TextContent of string
    | ImageContent of data: byte[] * mimeType: string

type ToolOutput =
    | ToolSuccess of ToolContent list
    | ToolFailure of string

type McpTool = {
    Name: string
    Description: string
    InputSchema: Schema
    Handler: JsonValue -> Task<ToolOutput>
}

type McpServer =
    | StdioServer of command: string * args: string list * env: Map<string, string>
    | SseServer of url: string * headers: Map<string, string>
    | HttpServer of url: string * headers: Map<string, string>
    | SdkServer of name: string * tools: McpTool list

// ============================================================================
// Agent Types
// ============================================================================

type AgentModel =
    | Sonnet
    | Opus
    | Haiku
    | Inherit

type AgentDefinition = {
    Description: string
    Prompt: string
    Tools: string list option
    Model: AgentModel option
}

type SettingSource =
    | UserSource
    | ProjectSource
    | LocalSource

// ============================================================================
// Sandbox Types
// ============================================================================

type SandboxNetworkConfig = {
    AllowUnixSockets: string list option
    AllowAllUnixSockets: bool option
    AllowLocalBinding: bool option
    HttpProxyPort: int option
    SocksProxyPort: int option
}

type SandboxIgnoreViolations = {
    File: string list option
    Network: string list option
}

type SandboxSettings = {
    Enabled: bool option
    AutoAllowBashIfSandboxed: bool option
    ExcludedCommands: string list option
    AllowUnsandboxedCommands: bool option
    Network: SandboxNetworkConfig option
    IgnoreViolations: SandboxIgnoreViolations option
    EnableWeakerNestedSandbox: bool option
}

// ============================================================================
// System Prompt Types
// ============================================================================

type SystemPromptPreset =
    | ClaudeCodePreset of append: string option

type SystemPromptConfig =
    | SystemPromptText of string
    | SystemPromptPresetConfig of SystemPromptPreset

// ============================================================================
// Tools Preset Types
// ============================================================================

type ToolsPreset =
    | ClaudeCodeToolsPreset

type ToolsConfig =
    | ToolsList of string list
    | ToolsPresetConfig of ToolsPreset

type ToolAllowMode =
    | AutoAllowMcp        // Auto-allow all MCP tools (default)
    | ManualControl       // Explicit AllowedTools only

// ============================================================================
// Event Types
// ============================================================================

type SdkEvent =
    | MessageReceived of Message
    | MessageSent of prompt: string * sessionId: string
    | ToolUseStarted of toolName: string * toolId: string * input: JsonValue
    | ToolUseCompleted of toolName: string * toolId: string * output: JsonValue
    | ThinkingStarted of content: string
    | SessionStarted of sessionId: string
    | SessionEnded of sessionId: string * result: ResultMessage option
    | ErrorOccurred of SdkError
    | ConnectionEstablished of cliPath: string
    | ConnectionClosed

type EventHandler = SdkEvent -> unit

type EventSubscription = {
    Id: Guid
    Handler: EventHandler
}

type EventBus = {
    Subscriptions: EventSubscription list ref
}

// ============================================================================
// Options Record
// ============================================================================

type Options = {
    Tools: ToolsConfig option
    ToolAllowMode: ToolAllowMode
    AllowedTools: string list
    DisallowedTools: string list
    SystemPrompt: SystemPromptConfig option
    Model: string option
    FallbackModel: string option
    PermissionMode: PermissionMode option
    PermissionPromptToolName: string option
    CanUseTool: (string -> JsonValue -> PermissionContext -> Task<PermissionResult>) option
    ContinueConversation: bool
    Resume: string option
    ForkSession: bool
    MaxTurns: int option
    MaxBudgetUsd: float option
    MaxThinkingTokens: int option
    Cwd: string option
    CliPath: string option
    Env: Map<string, string>
    ExtraArgs: Map<string, string option>
    User: string option
    McpServers: Map<string, McpServer>
    Hooks: Map<HookEvent, HookMatcher list>
    Settings: string option
    Sandbox: SandboxSettings option
    AddDirs: string list
    Agents: Map<string, AgentDefinition>
    SettingSources: SettingSource list option
    IncludePartialMessages: bool
    OutputFormat: Schema option
    EnableFileCheckpointing: bool
    MaxBufferSize: int option
    Events: EventBus option
    Stderr: (string -> unit) option
}

module Options =
    let defaults = {
        Tools = None
        ToolAllowMode = AutoAllowMcp
        AllowedTools = []
        DisallowedTools = []
        SystemPrompt = None
        Model = None
        FallbackModel = None
        PermissionMode = None
        PermissionPromptToolName = None
        CanUseTool = None
        ContinueConversation = false
        Resume = None
        ForkSession = false
        MaxTurns = None
        MaxBudgetUsd = None
        MaxThinkingTokens = None
        Cwd = None
        CliPath = None
        Env = Map.empty
        ExtraArgs = Map.empty
        User = None
        McpServers = Map.empty
        Hooks = Map.empty
        Settings = None
        Sandbox = None
        AddDirs = []
        Agents = Map.empty
        SettingSources = None
        IncludePartialMessages = false
        OutputFormat = None
        EnableFileCheckpointing = false
        MaxBufferSize = None
        Events = None
        Stderr = None
    }

    /// Enumerate MCP tool names from registered servers for auto-allowing
    let enumerateMcpTools (mcpServers: Map<string, McpServer>) : string list =
        mcpServers
        |> Map.toList
        |> List.collect (fun (serverName, server) ->
            match server with
            | SdkServer(_, tools) ->
                tools |> List.map (fun t -> $"mcp__{serverName}__{t.Name}")
            | _ -> []  // External servers discover tools dynamically
        )

// ============================================================================
// Connection Types
// ============================================================================

type Connection = {
    Process: Process
    Stdin: StreamWriter
    Stdout: StreamReader
    Stderr: StreamReader option
}

// ============================================================================
// Control Protocol Types
// ============================================================================

type ControlRequest =
    | CanUseToolRequest of requestId: string * toolName: string * input: JsonValue * suggestions: PermissionUpdate list
    | HookCallbackRequest of requestId: string * callbackId: string * input: HookInput * toolUseId: string option
    | McpMessageRequest of requestId: string * serverName: string * message: JsonValue
    | InitializeRequest of requestId: string
    | SetPermissionModeRequest of requestId: string * mode: PermissionMode
    | SetModelRequest of requestId: string * model: string option
    | RewindFilesRequest of requestId: string * userMessageId: string
    | InterruptRequest of requestId: string

type ControlResponse =
    | SuccessResponse of requestId: string * response: JsonValue option
    | ErrorResponse of requestId: string * error: string

/// Represents either a regular message or a control protocol message
type ParsedLine =
    | RegularMessage of Message
    | ControlRequestLine of ControlRequest
    | ControlResponseLine of ControlResponse

// ============================================================================
// Client Context (immutable, passed through functions)
// ============================================================================

type ClientContext = {
    Connection: Connection
    Options: Options
    SessionId: string
    HookCallbacks: Map<string, HookCallback>
}
