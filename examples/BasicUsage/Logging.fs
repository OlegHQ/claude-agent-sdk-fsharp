/// Verbose logging helpers for debugging examples
module Examples.Logging

open System
open ClaudeAgentSdk
open Thoth.Json.Net

/// Log level for controlling output
let mutable Verbose = true

let private log (prefix: string) (color: ConsoleColor) (msg: string) =
    if Verbose then
        let ts = DateTime.Now.ToString("HH:mm:ss.fff")
        Console.ForegroundColor <- color
        Console.Write(sprintf "[%s] %s: " ts prefix)
        Console.ResetColor()
        Console.WriteLine(msg)

let info msg = log "INFO" ConsoleColor.Cyan msg
let debug msg = log "DEBUG" ConsoleColor.DarkGray msg
let warn msg = log "WARN" ConsoleColor.Yellow msg
let error msg = log "ERROR" ConsoleColor.Red msg
let success msg = log "OK" ConsoleColor.Green msg

let send msg = log "SEND" ConsoleColor.Magenta msg
let recv msg = log "RECV" ConsoleColor.Blue msg

/// Log SDK error with full details
let logError (e: SdkError) =
    match e with
    | CliNotFound msg -> error (sprintf "CLI not found: %s" msg)
    | ConnectionFailed msg -> error (sprintf "Connection failed: %s" msg)
    | ProcessFailed (code, stderr) ->
        error (sprintf "Process failed with code %d" code)
        if not (String.IsNullOrWhiteSpace(stderr)) then
            error (sprintf "Stderr: %s" stderr)
    | JsonError msg -> error (sprintf "JSON error: %s" msg)
    | ParseError msg -> error (sprintf "Parse error: %s" msg)
    | ProtocolError msg -> error (sprintf "Protocol error: %s" msg)
    | Timeout msg -> error (sprintf "Timeout: %s" msg)

/// Log a message with details
let logMessage (msg: Message) =
    match msg with
    | UserMsg m ->
        let uuid = m.Uuid |> Option.defaultValue "?"
        recv (sprintf "User message (uuid=%s)" uuid)
    | AssistantMsg m ->
        recv (sprintf "Assistant message (model=%s)" m.Model)
        for block in m.Content do
            match block with
            | Text t ->
                let preview = if t.Length > 100 then t.Substring(0, 100) + "..." else t
                debug (sprintf "  Text: %s" (preview.Replace('\n', ' ')))
            | Thinking (t, _) ->
                let preview = if t.Length > 50 then t.Substring(0, 50) + "..." else t
                debug (sprintf "  Thinking: %s" (preview.Replace('\n', ' ')))
            | ToolUse (id, name, input) ->
                debug (sprintf "  ToolUse: %s (id=%s)" name id)
                debug (sprintf "    Input: %s" (Encode.toString 0 input))
            | ToolResult (id, content, isError) ->
                debug (sprintf "  ToolResult: id=%s, isError=%A" id isError)
    | SystemMsg m ->
        recv (sprintf "System message: %s" m.Subtype)
    | ResultMsg m ->
        let cost = m.TotalCostUsd |> Option.defaultValue 0.0
        recv (sprintf "Result: turns=%d, duration=%dms, cost=$%.4f" m.NumTurns m.DurationMs cost)
        match m.Result with
        | Some r ->
            let len = min 100 r.Length
            debug (sprintf "  Result text: %s..." (r.Substring(0, len)))
        | None -> ()
    | StreamMsg e ->
        recv (sprintf "Stream event: session=%s" e.SessionId)

/// Log options being used
let logOptions (options: Options) =
    info "Options:"
    match options.Model with
    | Some m -> debug (sprintf "  Model: %s" m)
    | None -> debug "  Model: (default)"

    match options.SystemPrompt with
    | Some (SystemPromptText t) ->
        let preview = if t.Length > 50 then t.Substring(0, 50) + "..." else t
        debug (sprintf "  SystemPrompt: %s" preview)
    | Some (SystemPromptPresetConfig _) -> debug "  SystemPrompt: (preset)"
    | None -> debug "  SystemPrompt: (none)"

    match options.PermissionMode with
    | Some m -> debug (sprintf "  PermissionMode: %A" m)
    | None -> debug "  PermissionMode: (default)"

    if not (Map.isEmpty options.McpServers) then
        let servers = options.McpServers |> Map.toList |> List.map fst |> String.concat ", "
        debug (sprintf "  MCP Servers: %s" servers)

    match options.MaxTurns with
    | Some t -> debug (sprintf "  MaxTurns: %d" t)
    | None -> ()

    match options.MaxBudgetUsd with
    | Some b -> debug (sprintf "  MaxBudgetUsd: $%.2f" b)
    | None -> ()

/// Wrap query with verbose logging
let verboseQuery (prompt: string) (options: Options) = task {
    let preview = if prompt.Length > 50 then prompt.Substring(0, 50) + "..." else prompt
    info (sprintf "Starting query: %s" preview)
    logOptions options
    send (sprintf "Prompt: %s" prompt)

    let! result = queryCollect prompt options

    match result with
    | Ok messages ->
        success (sprintf "Query completed with %d messages" (List.length messages))
        for msg in messages do
            logMessage msg
        return Ok messages
    | Error e ->
        logError e
        return Error e
}

/// Wrap Client.connect with verbose logging
let verboseConnect (options: Options) = task {
    info "Connecting to Claude CLI..."
    logOptions options

    let! result = Client.connect options

    match result with
    | Ok ctx ->
        success (sprintf "Connected! SessionId=%s" ctx.SessionId)
        return Ok ctx
    | Error e ->
        logError e
        return Error e
}

/// Wrap Client.query with verbose logging
let verboseClientQuery (prompt: string) (ctx: ClientContext) = task {
    send (sprintf "Prompt: %s" prompt)

    let! result = Client.query prompt ctx

    match result with
    | Ok (newCtx, messages) ->
        success (sprintf "Query completed with %d messages" (List.length messages))
        for msg in messages do
            logMessage msg
        return Ok (newCtx, messages)
    | Error e ->
        logError e
        return Error e
}

/// Streaming query with real-time output - shows messages as they arrive
let streamingClientQuery (prompt: string) (ctx: ClientContext) = task {
    send (sprintf "Prompt: %s" prompt)
    info "Waiting for response (streaming)..."

    let! sendResult = Client.send prompt ctx

    match sendResult with
    | Error e ->
        logError e
        return Error e
    | Ok ctx ->
        let messages = ResizeArray<Message>()
        let mutable lastError: SdkError option = None

        // Iterate using GetAsyncEnumerator
        let enumerator = (Client.receive ctx).GetAsyncEnumerator()
        let mutable hasMore = true
        while hasMore do
            let! moveNext = enumerator.MoveNextAsync()
            if moveNext then
                let result = enumerator.Current
                match result with
                | Ok msg ->
                    messages.Add(msg)
                    // Show progress in real-time
                    match msg with
                    | AssistantMsg m ->
                        for block in m.Content do
                            match block with
                            | Text t ->
                                Console.ForegroundColor <- ConsoleColor.White
                                Console.Write(t)
                                Console.ResetColor()
                            | ToolUse (id, name, input) ->
                                Console.WriteLine()
                                info (sprintf "Tool call: %s" name)
                                debug (sprintf "  Input: %s" (Encode.toString 0 input))
                            | Thinking (t, _) ->
                                debug (sprintf "Thinking: %s..." (if t.Length > 50 then t.Substring(0, 50) else t))
                            | ToolResult _ -> ()
                    | ResultMsg r ->
                        Console.WriteLine()
                        let cost = r.TotalCostUsd |> Option.defaultValue 0.0
                        success (sprintf "Done! turns=%d, duration=%dms, cost=$%.4f" r.NumTurns r.DurationMs cost)
                    | SystemMsg m ->
                        debug (sprintf "System: %s" m.Subtype)
                    | UserMsg _ -> ()
                    | StreamMsg _ -> ()
                | Error e ->
                    lastError <- Some e
                    logError e
            else
                hasMore <- false

        match lastError with
        | Some e when messages.Count = 0 -> return Error e
        | _ -> return Ok (ctx, List.ofSeq messages)
}
