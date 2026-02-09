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
├── Events.fs      # Event bus for monitoring
├── Json.fs        # JSON helpers
├── Transport.fs   # Subprocess spawn/read/write
├── Parser.fs      # JSON → Message
├── Hooks.fs       # Hook execution
├── Mcp.fs         # MCP tools
├── Streaming.fs   # TaskSeq helpers
├── Protocol.fs    # Control protocol
├── Query.fs       # One-shot query
├── Client.fs      # Streaming client
└── Sdk.fs         # Public API
```

## CI

- Workflow: `.github/workflows/ci.yml` (Continuous Integration).
- Triggers: `workflow_dispatch`, `pull_request`, and `push` to the default branch (currently `dev`).
- Runs on `ubuntu-latest`, restores/builds SDK + examples, runs tests, and publishes TRX results via `EnricoMi/publish-unit-test-result-action` pinned to a commit SHA (reporting skipped for fork PRs).

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
    Tools: ToolsConfig option
    ToolAllowMode: ToolAllowMode              // NEW: Auto-allow MCP tools
    AllowedTools: string list
    DisallowedTools: string list
    SystemPrompt: SystemPromptConfig option
    Model: string option
    FallbackModel: string option
    PermissionMode: PermissionMode option
    MaxTurns: int option
    MaxBudgetUsd: float option
    MaxThinkingTokens: int option
    Cwd: string option
    CliPath: string option
    Env: Map<string, string>
    ExtraArgs: Map<string, string option>
    McpServers: Map<string, McpServer>
    Hooks: Map<HookEvent, HookMatcher list>
    CanUseTool: (string -> JsonValue -> PermissionContext -> Task<PermissionResult>) option
    Events: EventBus option                   // NEW: Event subscription
    OutputFormat: Schema option
    EnableFileCheckpointing: bool
    // ... and more (30+ fields total)
}

let defaults = {
    Tools = None
    ToolAllowMode = AutoAllowMcp              // NEW: Smart default
    AllowedTools = []
    DisallowedTools = []
    SystemPrompt = None
    Model = None
    FallbackModel = None
    PermissionMode = None
    MaxTurns = None
    MaxBudgetUsd = None
    MaxThinkingTokens = None
    Cwd = None
    CliPath = None
    Env = Map.empty
    ExtraArgs = Map.empty
    McpServers = Map.empty
    Hooks = Map.empty
    CanUseTool = None
    Events = None                             // NEW: Opt-in events
    OutputFormat = None
    EnableFileCheckpointing = false
    // ... (see Types.fs for all fields)
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

// Schema DSL with consistent descriptions
type Schema =
    | SString of description: string option
    | SNumber of description: string option
    | SBool of description: string option
    | SObject of description: string option * properties: (string * Schema * bool) list
    | SArray of description: string option * itemSchema: Schema
    | SAny

module Schema =
    let string = SString None
    let string' desc = SString (Some desc)
    let object' props = SObject (None, props)
    let object'' desc props = SObject (Some desc, props)
    let required name s = (name, s, true)
    let optional name s = (name, s, false)

type McpTool = { Name: string; Description: string; InputSchema: Schema; Handler: JsonValue -> Task<ToolOutput> }

// Usage - clean tool creation
let greet = Mcp.tool "greet" "Says hello"
    (Mcp.Schema.object' [
        Mcp.Schema.required "name" (Mcp.Schema.string' "The name to greet")
    ])
    (fun input -> task {
        match Mcp.getString "name" input with
        | Ok name -> return Mcp.textResult (sprintf "Hello, %s!" name)
        | Error e -> return Mcp.errorResult e
    })

// Create server and auto-allow
let server = Mcp.createSdkServer "greetings" [greet]
let options = {
    Options.defaults with
        McpServers = Map.ofList ["greetings", server]
        // Tools automatically allowed!
}
```

## Examples

All examples follow functional principles (zero mutable state):

```
examples/BasicUsage/
├── Common.fs              # Functional Logger + helpers
├── 01-QuickStart.fs       # Simplest query
├── 02-StreamingBasic.fs   # TaskSeq patterns
├── 03-McpTools.fs         # MCP tools with auto-allow
├── 04-HooksAndPermissions.fs  # Security hooks
├── 05-InteractiveSession.fs   # Recursive REPL
├── 06-HumanInTheLoop.fs   # Permission callbacks
├── 07-EventsExample.fs    # Event system
├── 08-MaxBudget.fs        # Budget control
├── 09-StructuredOutput.fs # JSON schema output
└── 11-AskUserTool.fs      # Bidirectional interaction
```

**Run examples:**
```bash
dotnet run --project examples/BasicUsage -- 01  # Quick start
dotnet run --project examples/BasicUsage -- all # All examples
```

## What This Is NOT

- No `type Foo() = member ...` classes
- No `mutable` fields (except internal SDK/examples helpers where necessary)
- No interfaces
- No inheritance
- No dependency injection
- No builder patterns
- No OOP design patterns

## Dependencies

```xml
<PackageReference Include="FSharp.Control.TaskSeq" Version="0.4.*" />
<PackageReference Include="FsToolkit.ErrorHandling.TaskResult" Version="4.*" />
<PackageReference Include="Thoth.Json.Net" Version="11.*" />
```

## Features

- [x] `query` - one-shot, returns `TaskSeq<Result<Message, SdkError>>`
- [x] `Client.connect/send/receive` - state machine
- [x] Control protocol commands
- [x] Message/Content types as DUs
- [x] Options record with defaults
- [x] Hooks
- [x] MCP servers (typed tools) with **auto-allow**
- [x] Permission callbacks
- [x] **Event system** - subscribe to SDK events
- [x] **Streaming helpers** - clean TaskSeq utilities

## MCP Tools - Auto-Allow by Default

MCP tools are automatically allowed when registered. No manual allowlisting needed!

```fsharp
let server = Mcp.createSdkServer "tools" [greetTool; calcTool]

let options = {
    Options.defaults with
        McpServers = Map.ofList ["tools", server]
        // AllowedTools NOT needed! Auto-allowed by default
        ToolAllowMode = AutoAllowMcp  // This is the default
}
```

**Opt-out specific tools:**
```fsharp
{ options with DisallowedTools = ["mcp__tools__greet"] }
```

**Manual control** (old behavior):
```fsharp
{ options with
    ToolAllowMode = ManualControl
    AllowedTools = ["mcp__tools__calc"] }
```

**Benefits:**
- ✅ No more brittle string literals like `"mcp__server__tool"`
- ✅ Type-safe through `ToolAllowMode` DU
- ✅ Cleaner API for the common case
- ✅ Backward compatible

## Event-Driven Architecture

Subscribe to SDK events for logging, monitoring, and debugging:

```fsharp
// Create event bus
let bus = Events.createBus ()

// Subscribe to events
let subId = Events.subscribe (fun event ->
    match event with
    | MessageReceived msg ->
        printfn "Got message: %A" msg
    | ToolUseStarted (name, id, _) ->
        printfn "Tool started: %s" name
    | ThinkingStarted content ->
        printfn "Thinking: %s" (content.[..min 50 content.Length])
    | ConnectionEstablished cliPath ->
        printfn "Connected: %s" cliPath
    | ErrorOccurred err ->
        printfn "Error: %A" err
    | _ -> ()
) bus

// Use in options
let options = { Options.defaults with Events = Some bus }
```

**Event Types:**
```fsharp
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
```

**Multiple subscribers:**
```fsharp
let sub1 = Events.subscribe logger1 bus
let sub2 = Events.subscribe logger2 bus

// Unsubscribe when done
Events.unsubscribe sub1 bus
```

## Streaming Helpers

Clean TaskSeq utilities to avoid manual enumerators:

```fsharp
// Pattern 1: forEach with side effects
let! error = Streaming.forEach (fun msg ->
    printfn "%A" msg
) (Client.receive ctx)

// Pattern 2: Collect into list
let! (messages, error) = Streaming.collect (Client.receive ctx)

// Pattern 3: Conditional iteration
let! _ = Streaming.forEachWhile (fun msg ->
    match msg with
    | ResultMsg _ -> false  // Stop
    | _ -> true             // Continue
) (Client.receive ctx)

// Pattern 4: Filter messages
let assistantOnly =
    Client.receive ctx
    |> Streaming.filterMessages (function AssistantMsg _ -> true | _ -> false)

// Pattern 5: Take until result
let upToResult =
    Client.receive ctx
    |> Streaming.takeUntilResult
```

**No more manual `GetAsyncEnumerator()`!**

## Schema Consistency

All schema variants now support optional descriptions:

```fsharp
// Without descriptions
Mcp.Schema.string
Mcp.Schema.number
Mcp.Schema.bool
Mcp.Schema.object' [...]
Mcp.Schema.array itemSchema

// With descriptions
Mcp.Schema.string' "A user's name"
Mcp.Schema.number' "Age in years"
Mcp.Schema.bool' "Is active"
Mcp.Schema.object'' "Person data" [...]
Mcp.Schema.array' "List of items" itemSchema

// Add description to any schema
Mcp.Schema.string |> Mcp.Schema.describe "User name"
```
