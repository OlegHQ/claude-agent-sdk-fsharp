# Claude Agent SDK for F#

Idiomatic F# port of the Claude Agent SDK with 100% feature parity. Embraces algebraic types, immutability, and functional pipelines.

## Requirements

- .NET 8.0 or later
- Claude CLI installed (for running examples)

## Installation (Paket)

Add to your `paket.dependencies`:

```
git https://github.com/snowbear/claude-agent-sdk-fsharp.git master build: "dotnet build src/ClaudeAgentSdk -c Release"
```

Add to your `paket.references`:

```
ClaudeAgentSdk
```

Then run:

```bash
paket install
```

> NuGet package coming soon.

## Quick Start

```fsharp
open ClaudeAgentSdk

// One-shot query
let result = Sdk.query "Hello Claude!" Options.defaults

// Streaming with TaskSeq
for msg in result do
    match msg with
    | Ok (AssistantMsg blocks) -> printfn "%A" blocks
    | Ok (ResultMsg r) -> printfn "Done: %s" r.SessionId
    | Error e -> printfn "Error: %A" e
```

## Build & Test

```bash
# Build SDK
dotnet build src/ClaudeAgentSdk/ClaudeAgentSdk.fsproj

# Run tests
dotnet test tests/ClaudeAgentSdk.Tests/ClaudeAgentSdk.Tests.fsproj

# Build examples
dotnet build examples/BasicUsage/BasicUsage.fsproj
```

## Examples

Run individual examples or all at once:

```bash
# Run all examples
dotnet run --project examples/BasicUsage

# Run specific example by number
dotnet run --project examples/BasicUsage -- 01    # Quick Start
dotnet run --project examples/BasicUsage -- 02    # Streaming Basic
dotnet run --project examples/BasicUsage -- 03    # MCP Tools
dotnet run --project examples/BasicUsage -- 04    # Hooks & Permissions
dotnet run --project examples/BasicUsage -- 05    # Interactive Session
dotnet run --project examples/BasicUsage -- 06    # Human in the Loop
dotnet run --project examples/BasicUsage -- 07    # Events Example
dotnet run --project examples/BasicUsage -- 08    # Max Budget
dotnet run --project examples/BasicUsage -- 09    # Structured Output
dotnet run --project examples/BasicUsage -- 11    # Ask User Tool
```

Aliases also work:

```bash
dotnet run --project examples/BasicUsage -- quick       # 01
dotnet run --project examples/BasicUsage -- streaming   # 02
dotnet run --project examples/BasicUsage -- mcp         # 03
dotnet run --project examples/BasicUsage -- hooks       # 04
dotnet run --project examples/BasicUsage -- interactive # 05
dotnet run --project examples/BasicUsage -- human       # 06
dotnet run --project examples/BasicUsage -- events      # 07
dotnet run --project examples/BasicUsage -- budget      # 08
dotnet run --project examples/BasicUsage -- structured  # 09
dotnet run --project examples/BasicUsage -- ask         # 11
```

### Example Descriptions

| # | Name | Description |
|---|------|-------------|
| 01 | QuickStart | Simplest query, one-shot response |
| 02 | StreamingBasic | TaskSeq patterns: streaming, collecting, folding |
| 03 | McpTools | MCP tools with auto-allow feature |
| 04 | HooksAndPermissions | Pre/post tool hooks, blocking, permissions |
| 05 | InteractiveSession | Recursive REPL loop (functional, no mutable state) |
| 06 | HumanInTheLoop | Permission callbacks, user approval |
| 07 | EventsExample | Event bus: subscribe to SDK events |
| 08 | MaxBudget | Budget control with MaxBudgetUsd |
| 09 | StructuredOutput | JSON Schema output format |
| 11 | AskUserTool | Bidirectional interaction |

## Key Features

### MCP Tools with Auto-Allow

```fsharp
let greet = Mcp.tool "greet" "Says hello"
    (Mcp.Schema.object' [
        Mcp.Schema.required "name" (Mcp.Schema.string' "The name to greet")
    ])
    (fun input -> task {
        match Mcp.getString "name" input with
        | Ok name -> return Mcp.textResult (sprintf "Hello, %s!" name)
        | Error e -> return Mcp.errorResult e
    })

let server = Mcp.createSdkServer "greetings" [greet]
let options = {
    Options.defaults with
        McpServers = Map.ofList ["greetings", server]
        // No AllowedTools needed - auto-allowed by default!
}
```

### Event-Driven Monitoring

```fsharp
let bus = Events.createBus ()

Events.subscribe (fun event ->
    match event with
    | MessageReceived msg -> printfn "Got: %A" msg
    | ToolUseStarted (name, _, _) -> printfn "Tool: %s" name
    | ErrorOccurred err -> printfn "Error: %A" err
    | _ -> ()
) bus |> ignore

let options = { Options.defaults with Events = Some bus }
```

### Streaming Helpers

```fsharp
// Pattern 1: forEach with side effects
let! error = Streaming.forEach (fun msg ->
    printfn "%A" msg
) (Client.receive ctx)

// Pattern 2: Collect into list
let! (messages, error) = Streaming.collect (Client.receive ctx)

// Pattern 3: Filter messages
let assistantOnly =
    Client.receive ctx
    |> Streaming.filterMessages (function AssistantMsg _ -> true | _ -> false)
```

### Type-Safe Schemas

```fsharp
Mcp.Schema.string                           // Simple
Mcp.Schema.string' "A user's name"          // With description
Mcp.Schema.object'' "Person data" [         // Object with description
    Mcp.Schema.required "name" Mcp.Schema.string
    Mcp.Schema.optional "age" Mcp.Schema.number
]
```

## Architecture

Pure functional F# with:
- Discriminated unions for states (no OOP classes)
- Immutable records for configuration
- Result/Option for error handling (no exceptions)
- TaskSeq for streaming
- Pattern matching for parsing

See [CLAUDE.md](CLAUDE.md) for detailed architecture documentation.

## Dependencies

```xml
<PackageReference Include="FSharp.Control.TaskSeq" Version="0.4.*" />
<PackageReference Include="FsToolkit.ErrorHandling.TaskResult" Version="4.*" />
<PackageReference Include="Thoth.Json.Net" Version="11.*" />
```

## License

MIT
