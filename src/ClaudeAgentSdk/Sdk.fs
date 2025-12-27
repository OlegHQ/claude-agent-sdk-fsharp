/// Claude Agent SDK for F# - Public API
[<AutoOpen>]
module ClaudeAgentSdk.Sdk

open ClaudeAgentSdk

// ============================================================================
// Re-export Core Types
// ============================================================================

// Error type
type SdkError = ClaudeAgentSdk.SdkError

// Content and message types
type ContentBlock = ClaudeAgentSdk.ContentBlock
type Message = ClaudeAgentSdk.Message
type UserMessage = ClaudeAgentSdk.UserMessage
type AssistantMessage = ClaudeAgentSdk.AssistantMessage
type SystemMessage = ClaudeAgentSdk.SystemMessage
type ResultMessage = ClaudeAgentSdk.ResultMessage
type StreamEvent = ClaudeAgentSdk.StreamEvent
type AssistantMessageError = ClaudeAgentSdk.AssistantMessageError

// Permission types
type PermissionBehavior = ClaudeAgentSdk.PermissionBehavior
type PermissionMode = ClaudeAgentSdk.PermissionMode
type PermissionResult = ClaudeAgentSdk.PermissionResult
type PermissionContext = ClaudeAgentSdk.PermissionContext
type PermissionUpdate = ClaudeAgentSdk.PermissionUpdate
type PermissionUpdateType = ClaudeAgentSdk.PermissionUpdateType
type PermissionUpdateDestination = ClaudeAgentSdk.PermissionUpdateDestination
type PermissionRuleValue = ClaudeAgentSdk.PermissionRuleValue

// Hook types
type HookEvent = ClaudeAgentSdk.HookEvent
type HookInput = ClaudeAgentSdk.HookInput
type HookOutput = ClaudeAgentSdk.HookOutput
type HookSpecificOutput = ClaudeAgentSdk.HookSpecificOutput
type HookCallback = ClaudeAgentSdk.HookCallback
type HookMatcher = ClaudeAgentSdk.HookMatcher

// Schema and MCP types
type Schema = ClaudeAgentSdk.Schema
type ToolContent = ClaudeAgentSdk.ToolContent
type ToolOutput = ClaudeAgentSdk.ToolOutput
type McpTool = ClaudeAgentSdk.McpTool
type McpServer = ClaudeAgentSdk.McpServer

// Agent types
type AgentModel = ClaudeAgentSdk.AgentModel
type AgentDefinition = ClaudeAgentSdk.AgentDefinition
type SettingSource = ClaudeAgentSdk.SettingSource

// Sandbox types
type SandboxSettings = ClaudeAgentSdk.SandboxSettings
type SandboxNetworkConfig = ClaudeAgentSdk.SandboxNetworkConfig
type SandboxIgnoreViolations = ClaudeAgentSdk.SandboxIgnoreViolations

// System prompt types
type SystemPromptConfig = ClaudeAgentSdk.SystemPromptConfig
type SystemPromptPreset = ClaudeAgentSdk.SystemPromptPreset

// Tools config types
type ToolsConfig = ClaudeAgentSdk.ToolsConfig
type ToolsPreset = ClaudeAgentSdk.ToolsPreset

// Options and context
type Options = ClaudeAgentSdk.Options
type ClientContext = ClaudeAgentSdk.ClientContext

// ============================================================================
// Re-export Options Module
// ============================================================================

module Options =
    /// Default options with sensible defaults
    let defaults = ClaudeAgentSdk.Options.defaults

// ============================================================================
// Re-export Query Functions
// ============================================================================

/// Execute a one-shot query (non-interactive)
let query = Query.query

/// Execute a query with streaming input
let queryStreaming = Query.queryStreaming

/// Execute a query and collect all messages
let queryCollect = Query.queryCollect

/// Execute a query and return the result message
let queryResult = Query.queryResult

/// Execute a query and return the text result
let queryText = Query.queryText

/// Execute a query and return structured output
let queryStructured = Query.queryStructured

// ============================================================================
// Re-export Client Functions
// ============================================================================

module Client =
    /// Connect to Claude Code CLI
    let connect = Client.connect

    /// Disconnect and close the connection
    let disconnect = Client.disconnect

    /// Send a user message
    let send = Client.send

    /// Send a raw JSON message
    let sendRaw = Client.sendRaw

    /// Receive messages as a TaskSeq
    let receive = Client.receive

    /// Receive all messages until result
    let receiveAll = Client.receiveAll

    /// Send interrupt command
    let interrupt = Client.interrupt

    /// Set permission mode
    let setPermissionMode = Client.setPermissionMode

    /// Set model
    let setModel = Client.setModel

    /// Rewind files to state at user message ID
    let rewindFiles = Client.rewindFiles

    /// Send prompt and receive all messages
    let query = Client.query

    /// Send prompt and get result message
    let queryResult = Client.queryResult

    /// Send prompt and get text result
    let queryText = Client.queryText

    /// Update session ID
    let withSessionId = Client.withSessionId

    /// Get assistant messages from list
    let getAssistantMessages = Client.getAssistantMessages

    /// Get user messages from list
    let getUserMessages = Client.getUserMessages

    /// Get system messages from list
    let getSystemMessages = Client.getSystemMessages

    /// Get result message from list
    let getResultMessage = Client.getResultMessage

    /// Get text from assistant messages
    let getAssistantText = Client.getAssistantText

    /// Get tool uses from messages
    let getToolUses = Client.getToolUses

    /// Get thinking blocks from messages
    let getThinking = Client.getThinking

// ============================================================================
// Re-export MCP Functions
// ============================================================================

module Mcp =
    /// Create an MCP tool
    let tool = Mcp.tool

    /// Create an SDK MCP server
    let createSdkServer = Mcp.createSdkServer

    /// Create successful text result
    let textResult = Mcp.textResult

    /// Create successful image result
    let imageResult = Mcp.imageResult

    /// Create successful multi-content result
    let multiResult = Mcp.multiResult

    /// Create error result
    let errorResult = Mcp.errorResult

    /// Handle MCP JSONRPC request
    let handleMcpRequest = Mcp.handleMcpRequest

    // Input parsing helpers
    let getString = Mcp.getString
    let tryGetString = Mcp.tryGetString
    let getInt = Mcp.getInt
    let tryGetInt = Mcp.tryGetInt
    let getFloat = Mcp.getFloat
    let tryGetFloat = Mcp.tryGetFloat
    let getBool = Mcp.getBool
    let tryGetBool = Mcp.tryGetBool
    let getStringList = Mcp.getStringList
    let tryGetStringList = Mcp.tryGetStringList

    /// Schema DSL
    module Schema = Mcp.Schema

// ============================================================================
// Re-export Hooks Functions
// ============================================================================

module Hooks =
    /// Build hook callbacks from matchers
    let buildHookCallbacks = Hooks.buildHookCallbacks

    /// Create a matcher for all tools
    let matchAll = Hooks.matchAll

    /// Create a matcher for specific tool pattern
    let matchTool = Hooks.matchTool

    /// Add timeout to a matcher
    let withTimeout = Hooks.withTimeout

    /// Get session ID from hook input
    let getSessionId = Hooks.getSessionId

    /// Get transcript path from hook input
    let getTranscriptPath = Hooks.getTranscriptPath

    /// Get cwd from hook input
    let getCwd = Hooks.getCwd

    /// Continue hook response
    let continueHook = Hooks.continueHook

    /// Block hook response
    let blockHook = Hooks.blockHook

    /// Async hook response
    let asyncHook = Hooks.asyncHook

    /// Pre-tool-use response with permission decision
    let preToolUseResponse = Hooks.preToolUseResponse

    /// Post-tool-use response with context
    let postToolUseResponse = Hooks.postToolUseResponse

    /// User prompt submit response with context
    let userPromptSubmitResponse = Hooks.userPromptSubmitResponse

    /// Add system message to hook response
    let withSystemMessage = Hooks.withSystemMessage

    /// Suppress output in hook response
    let suppressOutput = Hooks.suppressOutput

// ============================================================================
// Event Bus
// ============================================================================

module Events =
    /// Event types
    type SdkEvent = SdkEvent
    type EventHandler = EventHandler
    type EventBus = EventBus

    /// Create a new event bus
    let createBus = Events.EventBus.create

    /// Subscribe to events
    let subscribe = Events.EventBus.subscribe

    /// Unsubscribe from events
    let unsubscribe = Events.EventBus.unsubscribe

    /// Publish an event (internal use)
    let publish = Events.EventBus.publish

// ============================================================================
// Streaming Helpers
// ============================================================================

module Streaming =
    /// Iterate with side effects
    let forEach = Streaming.forEach

    /// Iterate until condition is false
    let forEachWhile = Streaming.forEachWhile

    /// Collect all messages and errors
    let collect = Streaming.collect

    /// Filter messages by predicate
    let filterMessages = Streaming.filterMessages

    /// Take messages until result
    let takeUntilResult = Streaming.takeUntilResult

