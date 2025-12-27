/// Common utilities for examples - functional, no mutable state
module Examples.Common

open System
open System.Collections.Generic
open System.Threading.Tasks
open FSharp.Control
open ClaudeAgentSdk

// ============================================================================
// Functional Logger (Reader Pattern)
// ============================================================================

type LogLevel = Silent | Normal | Verbose

type Logger = {
    Level: LogLevel
    Write: string -> ConsoleColor -> string -> unit
}

module Logger =
    let create level = {
        Level = level
        Write = fun prefix color msg ->
            if level <> Silent then
                let ts = DateTime.Now.ToString("HH:mm:ss.fff")
                Console.ForegroundColor <- color
                printf "[%s] %s: " ts prefix
                Console.ResetColor()
                printfn "%s" msg
    }

    let info msg logger = logger.Write "INFO" ConsoleColor.Cyan msg
    let debug msg logger =
        if logger.Level = Verbose then logger.Write "DEBUG" ConsoleColor.DarkGray msg
    let warn msg logger = logger.Write "WARN" ConsoleColor.Yellow msg
    let error msg logger = logger.Write "ERROR" ConsoleColor.Red msg
    let success msg logger = logger.Write "OK" ConsoleColor.Green msg
    let send msg logger = logger.Write "SEND" ConsoleColor.Magenta msg
    let recv msg logger = logger.Write "RECV" ConsoleColor.Blue msg

    let logError e logger =
        match e with
        | CliNotFound msg -> error (sprintf "CLI not found: %s" msg) logger
        | ConnectionFailed msg -> error (sprintf "Connection failed: %s" msg) logger
        | ProcessFailed (code, stderr) ->
            error (sprintf "Process failed: %d" code) logger
            if not (String.IsNullOrWhiteSpace(stderr)) then
                error (sprintf "Stderr: %s" stderr) logger
        | JsonError msg -> error (sprintf "JSON error: %s" msg) logger
        | ParseError msg -> error (sprintf "Parse error: %s" msg) logger
        | ProtocolError msg -> error (sprintf "Protocol error: %s" msg) logger
        | Timeout msg -> error (sprintf "Timeout: %s" msg) logger

    let logMessage msg logger =
        match msg with
        | UserMsg m ->
            let uuid = m.Uuid |> Option.defaultValue "?"
            recv (sprintf "User message (uuid=%s)" uuid) logger
        | AssistantMsg m ->
            recv (sprintf "Assistant message (model=%s)" m.Model) logger
            for block in m.Content do
                match block with
                | Text t ->
                    Console.ForegroundColor <- ConsoleColor.White
                    printf "%s" t
                    Console.ResetColor()
                | ToolUse (id, name, _) ->
                    debug (sprintf "  ToolUse: %s (id=%s)" name id) logger
                | Thinking (t, _) ->
                    let preview = if t.Length > 50 then t.[..49] + "..." else t
                    debug (sprintf "  Thinking: %s" preview) logger
                | _ -> ()
        | SystemMsg m ->
            recv (sprintf "System message: %s" m.Subtype) logger
        | ResultMsg m ->
            let cost = m.TotalCostUsd |> Option.defaultValue 0.0
            recv (sprintf "Result: turns=%d, duration=%dms, cost=$%.4f" m.NumTurns m.DurationMs cost) logger
        | StreamMsg e ->
            recv (sprintf "Stream event: session=%s" e.SessionId) logger

    let silent = create Silent
    let normal = create Normal
    let verbose = create Verbose

// Module-level logging functions (for backward compatibility with old examples)
let private defaultLogger = Logger.normal

let info msg = Logger.info msg defaultLogger
let debug msg = Logger.debug msg defaultLogger
let warn msg = Logger.warn msg defaultLogger
let error msg = Logger.error msg defaultLogger
let success msg = Logger.success msg defaultLogger
let send msg = Logger.send msg defaultLogger
let recv msg = Logger.recv msg defaultLogger
let logError e = Logger.logError e defaultLogger

/// Wrap Client.connect with logging
let verboseConnect (options: Options) = task {
    info "Connecting to Claude CLI..."

    let! result = Client.connect options

    match result with
    | Ok ctx ->
        success (sprintf "Connected! SessionId=%s" ctx.SessionId)
        return Ok ctx
    | Error e ->
        logError e
        return Error e
}

/// Wrap Client.query with logging
let verboseClientQuery (prompt: string) (ctx: ClientContext) = task {
    send (sprintf "Prompt: %s" prompt)

    let! result = Client.query prompt ctx

    match result with
    | Ok (newCtx, messages) ->
        success (sprintf "Query completed with %d messages" (List.length messages))
        return Ok (newCtx, messages)
    | Error e ->
        logError e
        return Error e
}

/// Streaming query with real-time logging
let streamingClientQuery (prompt: string) (ctx: ClientContext) = task {
    send (sprintf "Prompt: %s" prompt)

    let! sendResult = Client.send prompt ctx

    match sendResult with
    | Error e ->
        logError e
        return Error e
    | Ok ctx ->
        let! (messages, error) = Streaming.collect (Client.receive ctx)

        match error, messages with
        | Some e, [] -> return Error e
        | _, msgs -> return Ok (ctx, msgs)
}

// ============================================================================
// Console Helper Functions (Functional Recursion)
// ============================================================================

module Console =
    /// Read hidden input (e.g., password) recursively
    let rec readHiddenInput (acc: string) : string =
        let key = System.Console.ReadKey(true)
        match key.Key with
        | ConsoleKey.Enter -> acc
        | ConsoleKey.Backspace ->
            let newAcc = if acc.Length > 0 then acc.[..acc.Length-2] else acc
            readHiddenInput newAcc
        | _ -> readHiddenInput (acc + string key.KeyChar)

    /// Read multi-line input until end marker
    let rec readMultilineUntil (endMarker: string) (acc: string list) : string list =
        let line = System.Console.ReadLine()
        if line = endMarker then
            acc |> List.rev
        else
            readMultilineUntil endMarker (line :: acc)

    /// Confirm yes/no question
    let confirm defaultValue prompt =
        printf "%s [%s]: " prompt (if defaultValue then "Y/n" else "y/N")
        let key = System.Console.ReadKey(true).KeyChar
        printfn ""
        match Char.ToLower key with
        | 'y' -> true
        | 'n' -> false
        | _ -> defaultValue

// ============================================================================
// Streaming Helper Functions (Functional)
// ============================================================================

module Stream =
    /// Collect messages from stream with logging
    let collectMessages (logger: Logger) (source: IAsyncEnumerable<Result<Message, SdkError>>) = task {
        let! results = source |> TaskSeq.toListAsync

        // Log all messages
        results |> List.iter (function
            | Ok msg -> Logger.logMessage msg logger
            | Error e -> Logger.logError e logger
        )

        // Extract messages and errors
        let messages = results |> List.choose (function Ok m -> Some m | _ -> None)
        let errors = results |> List.choose (function Error e -> Some e | _ -> None)

        match errors, messages with
        | e::_, [] -> return Error e
        | _, msgs -> return Ok msgs
    }

    /// Stream messages with real-time display
    let streamWithDisplay (logger: Logger) (source: IAsyncEnumerable<Result<Message, SdkError>>) = task {
        let mutable lastError = None
        let messages = ResizeArray<Message>()

        for result in source do
            match result with
            | Ok msg ->
                messages.Add(msg)
                // Real-time display
                match msg with
                | AssistantMsg m ->
                    for block in m.Content do
                        match block with
                        | Text t ->
                            Console.ForegroundColor <- ConsoleColor.White
                            printf "%s" t
                            Console.ResetColor()
                        | ToolUse (id, name, _) ->
                            printfn ""
                            info (sprintf "Tool call: %s" name)
                        | Thinking (t, _) ->
                            let preview = if t.Length > 50 then t.[..49] else t
                            debug (sprintf "Thinking: %s..." preview)
                        | _ -> ()
                | ResultMsg r ->
                    printfn ""
                    let cost = r.TotalCostUsd |> Option.defaultValue 0.0
                    success (sprintf "Done! turns=%d, duration=%dms, cost=$%.4f" r.NumTurns r.DurationMs cost)
                | SystemMsg m ->
                    debug (sprintf "System: %s" m.Subtype)
                | _ -> ()
            | Error e ->
                lastError <- Some e
                logError e

        match lastError with
        | Some e when messages.Count = 0 -> return Error e
        | _ -> return Ok (List.ofSeq messages)
    }

// ============================================================================
// UI Helper Functions
// ============================================================================

module UI =
    let banner title =
        let width = max 50 (String.length title + 4)
        let padding = (width - String.length title - 2) / 2
        Console.ForegroundColor <- ConsoleColor.Cyan
        printfn "╔%s╗" (String.replicate width "═")
        printfn "║%s%s%s║" (String.replicate padding " ") title (String.replicate (width - padding - String.length title - 2) " ")
        printfn "╚%s╝" (String.replicate width "═")
        Console.ResetColor()
        printfn ""

    let separator () =
        Console.ForegroundColor <- ConsoleColor.DarkGray
        printfn "%s" (String.replicate 80 "─")
        Console.ResetColor()
