module ClaudeAgentSdk.Mcp

open System.Threading.Tasks
open Thoth.Json.Net
open ClaudeAgentSdk

// ============================================================================
// Schema DSL
// ============================================================================

module Schema =
    /// String schema without description
    let string : Schema = SString None

    /// String schema with description
    let string' (desc: string) : Schema = SString (Some desc)

    /// Number schema without description
    let number : Schema = SNumber None

    /// Number schema with description
    let number' (desc: string) : Schema = SNumber (Some desc)

    /// Boolean schema without description
    let bool : Schema = SBool None

    /// Boolean schema with description
    let bool' (desc: string) : Schema = SBool (Some desc)

    /// Object schema with properties
    let object' (props: (string * Schema * bool) list) : Schema = SObject props

    /// Array schema
    let array (itemSchema: Schema) : Schema = SArray itemSchema

    /// Any schema (no validation)
    let any : Schema = SAny

    /// Required property helper
    let required (name: string) (schema: Schema) : (string * Schema * bool) =
        (name, schema, true)

    /// Optional property helper
    let optional (name: string) (schema: Schema) : (string * Schema * bool) =
        (name, schema, false)

// ============================================================================
// Tool Creation
// ============================================================================

/// Create an MCP tool with typed schema
let tool
    (name: string)
    (description: string)
    (inputSchema: Schema)
    (handler: JsonValue -> Task<ToolOutput>)
    : McpTool =
    {
        Name = name
        Description = description
        InputSchema = inputSchema
        Handler = handler
    }

/// Create an SDK MCP server with tools
let createSdkServer (name: string) (tools: McpTool list) : McpServer =
    SdkServer(name, tools)

// ============================================================================
// Tool Output Helpers
// ============================================================================

/// Create a successful text tool output
let textResult (text: string) : ToolOutput =
    ToolSuccess [TextContent text]

/// Create a successful image tool output
let imageResult (data: byte[]) (mimeType: string) : ToolOutput =
    ToolSuccess [ImageContent(data, mimeType)]

/// Create a successful multi-content tool output
let multiResult (contents: ToolContent list) : ToolOutput =
    ToolSuccess contents

/// Create a failed tool output
let errorResult (message: string) : ToolOutput =
    ToolFailure message

// ============================================================================
// MCP JSONRPC Protocol Handling
// ============================================================================

/// Encode MCP tool info for tools/list response
let private encodeToolInfo (tool: McpTool) : JsonValue =
    Encode.object [
        "name", Encode.string tool.Name
        "description", Encode.string tool.Description
        "inputSchema", Json.Encode.schema tool.InputSchema
    ]

/// Encode initialize response
let private encodeInitializeResponse (requestId: JsonValue option) (tools: McpTool list) : JsonValue =
    let fields = [
        "jsonrpc", Encode.string "2.0"
        yield! match requestId with
               | Some id -> ["id", id]
               | None -> []
        "result", Encode.object [
            "protocolVersion", Encode.string "2024-11-05"
            "capabilities", Encode.object [
                "tools", Encode.object []
            ]
            "serverInfo", Encode.object [
                "name", Encode.string "claude-agent-sdk-fsharp"
                "version", Encode.string "1.0.0"
            ]
        ]
    ]
    Encode.object fields

/// Encode tools/list response
let private encodeToolListResponse (requestId: JsonValue option) (tools: McpTool list) : JsonValue =
    let fields = [
        "jsonrpc", Encode.string "2.0"
        yield! match requestId with
               | Some id -> ["id", id]
               | None -> []
        "result", Encode.object [
            "tools", Encode.list (List.map encodeToolInfo tools)
        ]
    ]
    Encode.object fields

/// Encode ToolContent for MCP response
let private encodeToolContent (content: ToolContent) : JsonValue =
    match content with
    | TextContent text ->
        Encode.object [
            "type", Encode.string "text"
            "text", Encode.string text
        ]
    | ImageContent(data, mimeType) ->
        let base64 = System.Convert.ToBase64String(data)
        Encode.object [
            "type", Encode.string "image"
            "data", Encode.string base64
            "mimeType", Encode.string mimeType
        ]

/// Encode tools/call result
let private encodeCallToolResponse (requestId: JsonValue option) (output: ToolOutput) : JsonValue =
    let result =
        match output with
        | ToolSuccess contents ->
            Encode.object [
                "content", Encode.list (List.map encodeToolContent contents)
                "isError", Encode.bool false
            ]
        | ToolFailure message ->
            Encode.object [
                "content", Encode.list [
                    Encode.object [
                        "type", Encode.string "text"
                        "text", Encode.string message
                    ]
                ]
                "isError", Encode.bool true
            ]
    Encode.object [
        "jsonrpc", Encode.string "2.0"
        match requestId with
        | Some id -> "id", id
        | None -> ()
        "result", result
    ]

/// Encode MCP error response
let private encodeErrorResponse (requestId: JsonValue option) (code: int) (message: string) : JsonValue =
    Encode.object [
        "jsonrpc", Encode.string "2.0"
        match requestId with
        | Some id -> "id", id
        | None -> ()
        "error", Encode.object [
            "code", Encode.int code
            "message", Encode.string message
        ]
    ]

/// Find tool by name
let private findTool (name: string) (tools: McpTool list) : McpTool option =
    tools |> List.tryFind (fun t -> t.Name = name)

/// Execute a tool call
let private executeToolCall (tools: McpTool list) (name: string) (args: JsonValue) : Task<ToolOutput> = task {
    match findTool name tools with
    | None ->
        return ToolFailure $"Tool not found: {name}"
    | Some tool ->
        try
            return! tool.Handler args
        with ex ->
            return ToolFailure $"Tool execution failed: {ex.Message}"
}

/// Handle an MCP JSONRPC request
let handleMcpRequest (tools: McpTool list) (message: JsonValue) : Task<JsonValue> = task {
    let requestId = Decode.fromValue "" (Decode.optional "id" Decode.value) message |> Result.toOption |> Option.flatten
    let method = Decode.fromValue "" (Decode.field "method" Decode.string) message

    match method with
    | Ok "initialize" ->
        return encodeInitializeResponse requestId tools

    | Ok "tools/list" ->
        return encodeToolListResponse requestId tools

    | Ok "tools/call" ->
        let nameResult = Decode.fromValue "" (Decode.at ["params"; "name"] Decode.string) message
        let argsResult = Decode.fromValue "" (Decode.at ["params"; "arguments"] Decode.value) message

        match nameResult, argsResult with
        | Ok name, Ok args ->
            let! output = executeToolCall tools name args
            return encodeCallToolResponse requestId output
        | Ok name, Error _ ->
            let! output = executeToolCall tools name (Encode.object [])
            return encodeCallToolResponse requestId output
        | Error _, _ ->
            return encodeErrorResponse requestId -32602 "Invalid params: missing tool name"

    | Ok "notifications/initialized" ->
        return Encode.object [
            "jsonrpc", Encode.string "2.0"
        ]

    | Ok "notifications/cancelled" ->
        return Encode.object [
            "jsonrpc", Encode.string "2.0"
        ]

    | Ok methodName ->
        return encodeErrorResponse requestId -32601 $"Method not found: {methodName}"

    | Error _ ->
        return encodeErrorResponse requestId -32600 "Invalid request: missing method"
}

// ============================================================================
// Input Parsing Helpers
// ============================================================================

/// Get a required string field from tool input
let getString (field: string) (input: JsonValue) : Result<string, string> =
    match Decode.fromValue "" (Decode.field field Decode.string) input with
    | Ok s -> Ok s
    | Error e -> Error $"Missing or invalid field '{field}': {e}"

/// Get an optional string field from tool input
let tryGetString (field: string) (input: JsonValue) : string option =
    Decode.fromValue "" (Decode.optional field Decode.string) input
    |> Result.toOption
    |> Option.flatten

/// Get a required int field from tool input
let getInt (field: string) (input: JsonValue) : Result<int, string> =
    match Decode.fromValue "" (Decode.field field Decode.int) input with
    | Ok n -> Ok n
    | Error e -> Error $"Missing or invalid field '{field}': {e}"

/// Get an optional int field from tool input
let tryGetInt (field: string) (input: JsonValue) : int option =
    Decode.fromValue "" (Decode.optional field Decode.int) input
    |> Result.toOption
    |> Option.flatten

/// Get a required float field from tool input
let getFloat (field: string) (input: JsonValue) : Result<float, string> =
    match Decode.fromValue "" (Decode.field field Decode.float) input with
    | Ok n -> Ok n
    | Error e -> Error $"Missing or invalid field '{field}': {e}"

/// Get an optional float field from tool input
let tryGetFloat (field: string) (input: JsonValue) : float option =
    Decode.fromValue "" (Decode.optional field Decode.float) input
    |> Result.toOption
    |> Option.flatten

/// Get a required bool field from tool input
let getBool (field: string) (input: JsonValue) : Result<bool, string> =
    match Decode.fromValue "" (Decode.field field Decode.bool) input with
    | Ok b -> Ok b
    | Error e -> Error $"Missing or invalid field '{field}': {e}"

/// Get an optional bool field from tool input
let tryGetBool (field: string) (input: JsonValue) : bool option =
    Decode.fromValue "" (Decode.optional field Decode.bool) input
    |> Result.toOption
    |> Option.flatten

/// Get a required string list field from tool input
let getStringList (field: string) (input: JsonValue) : Result<string list, string> =
    match Decode.fromValue "" (Decode.field field (Decode.list Decode.string)) input with
    | Ok lst -> Ok lst
    | Error e -> Error $"Missing or invalid field '{field}': {e}"

/// Get an optional string list field from tool input
let tryGetStringList (field: string) (input: JsonValue) : string list option =
    Decode.fromValue "" (Decode.optional field (Decode.list Decode.string)) input
    |> Result.toOption
    |> Option.flatten

