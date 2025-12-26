# Claude Agent SDK F# Port

Port `claude-agent-sdk-python` to idiomatic F#. 100% feature parity.

## Principles

1. **Algebraic types** - DUs for states, records for data, no classes
2. **Immutable** - state flows through functions, never mutated
3. **Result/Option** - no nulls, no exceptions
4. **Pipelines** - `|>` and `>>` everywhere
5. **Type inference** - annotate only at module boundaries

## File Structure
```
src/
├── Types.fs       # All DUs and records
├── Json.fs        # JSON helpers
├── Transport.fs   # Subprocess spawn/read/write
├── Protocol.fs    # Control protocol
├── Parser.fs      # JSON → Message
├── Hooks.fs       # Hook execution
├── Mcp.fs         # MCP tools
├── Query.fs       # One-shot query
├── Client.fs      # Streaming client
└── Sdk.fs         # Public API
```

## Types - Algebraic, Not Classes

```fsharp
// States as DUs
type Connection =
    | Disconnected
    | Connected of proc: Process * stdin: StreamWriter * stdout: StreamReader

type ClientState =
    | Idle of Connection
    | Querying of Connection * sessionId: string
    | Receiving of Connection * sessionId: string

// Content as DUs
type Content =
    | Text of string
    | Thinking of string * signature: string
    | ToolUse of id: string * name: string * input: JsonValue
    | ToolResult of toolUseId: string * content: string option * isError: bool option

type Message =
    | User of content: string * uuid: string option
    | Assistant of blocks: Content list * model: string
    | System of subtype: string * data: JsonValue
    | Result of ResultData
    | Stream of uuid: string * sessionId: string * event: JsonValue

type PermissionResult =
    | Allow of updatedInput: JsonValue option
    | Deny of message: string * interrupt: bool

type HookResult =
    | Continue
    | Block of reason: string
    | RunAsync of timeout: int

type McpServer =
    | Stdio of cmd: string * args: string list
    | Sse of url: string
    | Http of url: string
    | Sdk of name: string * tools: McpTool list

type SdkError =
    | CliNotFound of string
    | ProcessFailed of code: int * stderr: string
    | JsonError of string
    | ParseError of string
    | ProtocolError of string

// Config as record (immutable data, not object)
type Options = {
    Tools: string list option
    AllowedTools: string list
    DisallowedTools: string list
    SystemPrompt: string option
    Model: string option
    PermissionMode: string option
    MaxTurns: int option
    MaxBudgetUsd: float option
    Cwd: string option
    CliPath: string option
    Env: Map<string, string>
    McpServers: Map<string, McpServer>
    Hooks: Map<string, HookMatcher list>
    CanUseTool: (string -> JsonValue -> Task<PermissionResult>) option
    OutputFormat: Schema option
    EnableCheckpointing: bool
}

let defaults = {
    Tools = None
    AllowedTools = []
    DisallowedTools = []
    SystemPrompt = None
    Model = None
    PermissionMode = None
    MaxTurns = None
    MaxBudgetUsd = None
    Cwd = None
    CliPath = None
    Env = Map.empty
    McpServers = Map.empty
    Hooks = Map.empty
    CanUseTool = None
    OutputFormat = None
    EnableCheckpointing = false
}
```

## Functions Over Methods

```fsharp
// Transport - pure functions returning TaskResult
module Transport =
    let spawn cliPath args env : Result<Connection, SdkError>
    let write line conn = taskResult { ... }
    let read conn : TaskSeq<string> = taskSeq { ... }
    let close conn = task { ... }

// Query - function returning stream
let query prompt options : TaskSeq<Result<Message, SdkError>> = taskSeq {
    match Transport.spawn options.CliPath (buildArgs options) options.Env with
    | Error e -> yield Error e
    | Ok conn ->
        do! Transport.write (encodePrompt prompt) conn
        for line in Transport.read conn do
            yield Parser.parse line
        do! Transport.close conn
}

// Client - state machine via functions
module Client =
    let connect options = taskResult {
        let! conn = Transport.spawn options.CliPath (buildArgs options) options.Env
        return Idle conn
    }

    let send prompt = function
        | Idle conn -> taskResult {
            do! Transport.write (encodePrompt prompt) conn
            return Querying (conn, "default")
          }
        | _ -> TaskResult.error (ProtocolError "invalid state")

    let receive = function
        | Querying (conn, _) -> Transport.read conn |> TaskSeq.map Parser.parse
        | _ -> TaskSeq.empty
```

## Pattern Matching for Parsing

```fsharp
// Active patterns for JSON
let (|Prop|_|) name (j: JsonValue) = j.TryGetProperty name
let (|Str|_|) = function JsonValue.String s -> Some s | _ -> None
let (|Num|_|) = function JsonValue.Number n -> Some n | _ -> None
let (|Arr|_|) = function JsonValue.Array a -> Some a | _ -> None

// Parse via pattern matching
let parseContent = function
    | Prop "type" (Str "text") & Prop "text" (Str t) -> Ok (Text t)
    | Prop "type" (Str "thinking") & Prop "thinking" (Str t) & Prop "signature" (Str s) -> Ok (Thinking (t, s))
    | Prop "type" (Str "tool_use") & Prop "id" (Str id) & Prop "name" (Str n) & Prop "input" i -> Ok (ToolUse (id, n, i))
    | Prop "type" (Str "tool_result") & Prop "tool_use_id" (Str id) -> Ok (ToolResult (id, None, None))
    | j -> Error (ParseError (j.ToString()))

let parseMessage = function
    | Prop "type" (Str "user") & Prop "content" c ->
        Ok (User (c.AsString(), None))
    | Prop "type" (Str "assistant") & Prop "content" (Arr blocks) & Prop "model" (Str m) ->
        blocks |> Array.map parseContent |> Result.sequence |> Result.map (fun b -> Assistant (List.ofArray b, m))
    | Prop "type" (Str "system") & Prop "subtype" (Str s) ->
        Ok (System (s, JsonValue.Null))
    | Prop "type" (Str "result") ->
        Ok (Result { (* parse fields *) })
    | j -> Error (ParseError (j.ToString()))
```

## MCP - Typed Tools

```fsharp
// Typed output instead of JsonValue
type ToolContent = Text of string | Image of bytes: byte[] * mime: string
type ToolOutput = Success of ToolContent list | Failure of string

// Schema DSL instead of JsonValue
type Schema =
    | SString | SNumber | SBool
    | SObject of (string * Schema * bool) list  // name, type, required
    | SArray of Schema

module Schema =
    let string = SString
    let object' props = SObject props
    let required name s = (name, s, true)

// Decoder combinators
module Decode =
    let field name f json = json |> tryProp name |> Option.toResult $"missing: {name}" |> Result.bind f
    let string = function JsonValue.String s -> Ok s | _ -> Error "expected string"

type McpTool = { Name: string; Description: string; Schema: Schema; Handler: JsonValue -> Task<ToolOutput> }

// Usage - typed input via decoder
let greet = {
    Name = "greet"; Description = "Says hello"
    Schema = Schema.object' [ Schema.required "name" Schema.string ]
    Handler = fun json -> task {
        match json |> Decode.field "name" Decode.string with
        | Ok name -> return Success [ Text $"Hello, {name}!" ]
        | Error e -> return Failure e
    }
}
```

## What This Is NOT

- No `type Foo() = member ...` classes
- No `mutable` fields
- No interfaces
- No inheritance
- No dependency injection
- No builder patterns
- No OOP design patterns

## Dependencies

```xml
<PackageReference Include="FSharp.Control.TaskSeq" Version="0.4.*" />
<PackageReference Include="FsToolkit.ErrorHandling.TaskResult" Version="4.*" />
```

## Features

- [ ] `query` - one-shot, returns `TaskSeq<Result<Message, SdkError>>`
- [ ] `Client.connect/send/receive` - state machine
- [ ] Control protocol commands
- [ ] Message/Content types as DUs
- [ ] Options record with defaults
- [ ] Hooks
- [ ] MCP servers (typed tools)
- [ ] Permission callbacks
