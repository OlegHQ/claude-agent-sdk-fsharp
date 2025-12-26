module ClaudeAgentSdk.Client

open System.Collections.Generic
open System.Threading.Tasks
open FSharp.Control
open FsToolkit.ErrorHandling
open Thoth.Json.Net
open ClaudeAgentSdk

// ============================================================================
// Connection Management
// ============================================================================

/// Connect to Claude Code CLI with given options
let connect (options: Options) : Task<Result<ClientContext, SdkError>> = taskResult {
    // If canUseTool is provided, set permission prompt tool name
    let options =
        match options.CanUseTool, options.PermissionPromptToolName with
        | Some _, None -> { options with PermissionPromptToolName = Some "stdio" }
        | _ -> options

    // Find CLI path
    let! cliPath = Transport.findCli options.CliPath

    // Build args for streaming mode
    let args = Transport.buildArgs options true

    // Spawn subprocess
    let! conn = Transport.spawn cliPath args options.Env options.Cwd

    // Build hook callbacks map
    let hookCallbacks = Hooks.buildHookCallbacks options.Hooks

    // Create initial context
    let ctx = {
        Connection = conn
        Options = options
        SessionId = "default"
        HookCallbacks = hookCallbacks
    }

    // Send initialize command if we have hooks
    do! Protocol.sendInitialize conn options

    return ctx
}

/// Disconnect and close the connection
let disconnect (ctx: ClientContext) : Task<unit> =
    Transport.close ctx.Connection

// ============================================================================
// Sending Messages
// ============================================================================

/// Send a user message (prompt)
let send (prompt: string) (ctx: ClientContext) : Task<Result<ClientContext, SdkError>> = taskResult {
    let msg = Json.Encode.userMessage prompt ctx.SessionId
    let json = Encode.toString 0 msg
    do! Transport.write json ctx.Connection
    return ctx
}

/// Send a raw JSON message
let sendRaw (message: JsonValue) (ctx: ClientContext) : Task<Result<ClientContext, SdkError>> = taskResult {
    let json = Encode.toString 0 message
    do! Transport.write json ctx.Connection
    return ctx
}

// ============================================================================
// Receiving Messages
// ============================================================================

/// Receive messages as a TaskSeq, automatically handling control requests
let receive (ctx: ClientContext) : IAsyncEnumerable<Result<Message, SdkError>> = taskSeq {
    let mutable isDone = false
    let enumerator = (Transport.readWithBuffer ctx.Connection ctx.Options.MaxBufferSize).GetAsyncEnumerator()

    try
        while not isDone do
            let! hasNext = enumerator.MoveNextAsync()
            if not hasNext then
                isDone <- true
            else
                let line = enumerator.Current
                match Parser.parseLine line with
                | Error e ->
                    yield Error e

                | Ok (RegularMessage msg) ->
                    yield Ok msg

                    // Check for ResultMsg to stop the loop
                    match msg with
                    | ResultMsg _ ->
                        isDone <- true
                    | _ -> ()

                | Ok (ControlRequestLine request) ->
                    // Handle control request and send response (silently)
                    let! response = Protocol.handleControlRequestFromContext ctx request
                    let responseJson = Protocol.encodeControlResponse response
                    match! Transport.write responseJson ctx.Connection with
                    | Ok () -> ()
                    | Error e -> yield Error e

                | Ok (ControlResponseLine _) ->
                    // Ignore control responses during receive (they're for our sent commands)
                    ()
    finally
        ()
}

/// Receive all messages until result and collect into a list
let receiveAll (ctx: ClientContext) : Task<Result<Message list, SdkError>> = task {
    let messages = ResizeArray<Message>()
    let mutable lastError: SdkError option = None

    for result in receive ctx do
        match result with
        | Ok msg -> messages.Add(msg)
        | Error e -> lastError <- Some e

    match lastError with
    | Some e when messages.Count = 0 -> return Error e
    | _ -> return Ok (messages |> List.ofSeq)
}

// ============================================================================
// Control Commands
// ============================================================================

/// Send interrupt command to stop current operation
let interrupt (ctx: ClientContext) : Task<Result<unit, SdkError>> =
    Protocol.sendInterrupt ctx.Connection

/// Set permission mode
let setPermissionMode (mode: PermissionMode) (ctx: ClientContext) : Task<Result<unit, SdkError>> =
    Protocol.sendSetPermissionMode mode ctx.Connection

/// Set model (or None to use default)
let setModel (model: string option) (ctx: ClientContext) : Task<Result<unit, SdkError>> =
    Protocol.sendSetModel model ctx.Connection

/// Rewind files to state at given user message ID
let rewindFiles (userMessageId: string) (ctx: ClientContext) : Task<Result<unit, SdkError>> =
    Protocol.sendRewindFiles userMessageId ctx.Connection

// ============================================================================
// Convenience Helpers
// ============================================================================

/// Send a prompt and receive all messages in one operation
let query (prompt: string) (ctx: ClientContext) : Task<Result<ClientContext * Message list, SdkError>> = taskResult {
    let! ctx = send prompt ctx
    let! messages = receiveAll ctx
    return (ctx, messages)
}

/// Send a prompt and get the final result message
let queryResult (prompt: string) (ctx: ClientContext) : Task<Result<ClientContext * ResultMessage option, SdkError>> = taskResult {
    let! (ctx, messages) = query prompt ctx
    let result = messages |> List.tryPick (function ResultMsg r -> Some r | _ -> None)
    return (ctx, result)
}

/// Send a prompt and get the text result
let queryText (prompt: string) (ctx: ClientContext) : Task<Result<ClientContext * string option, SdkError>> = taskResult {
    let! (ctx, result) = queryResult prompt ctx
    return (ctx, result |> Option.bind (fun r -> r.Result))
}

/// Update session ID in context
let withSessionId (sessionId: string) (ctx: ClientContext) : ClientContext =
    { ctx with SessionId = sessionId }

// ============================================================================
// Message Filtering Helpers
// ============================================================================

/// Get all assistant messages from a message list
let getAssistantMessages (messages: Message list) : AssistantMessage list =
    messages |> List.choose (function AssistantMsg m -> Some m | _ -> None)

/// Get all user messages from a message list
let getUserMessages (messages: Message list) : UserMessage list =
    messages |> List.choose (function UserMsg m -> Some m | _ -> None)

/// Get all system messages from a message list
let getSystemMessages (messages: Message list) : SystemMessage list =
    messages |> List.choose (function SystemMsg m -> Some m | _ -> None)

/// Get the result message from a message list
let getResultMessage (messages: Message list) : ResultMessage option =
    messages |> List.tryPick (function ResultMsg m -> Some m | _ -> None)

/// Get all text blocks from assistant messages
let getAssistantText (messages: Message list) : string list =
    messages
    |> getAssistantMessages
    |> List.collect (fun m -> m.Content)
    |> List.choose (function Text t -> Some t | _ -> None)

/// Get all tool use blocks from assistant messages
let getToolUses (messages: Message list) : (string * string * JsonValue) list =
    messages
    |> getAssistantMessages
    |> List.collect (fun m -> m.Content)
    |> List.choose (function ToolUse(id, name, input) -> Some (id, name, input) | _ -> None)

/// Get all thinking blocks from assistant messages
let getThinking (messages: Message list) : (string * string) list =
    messages
    |> getAssistantMessages
    |> List.collect (fun m -> m.Content)
    |> List.choose (function Thinking(t, s) -> Some (t, s) | _ -> None)

