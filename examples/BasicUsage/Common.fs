/// Common utilities for examples - functional, no mutable state
module Examples.Common

open System
open System.Collections.Generic
open System.Threading.Tasks
open FSharp.Control
open ClaudeAgentSdk

// ============================================================================
// TUI - Functional Terminal UI Primitives
// ============================================================================

/// Terminal colors (wraps ConsoleColor without exposing it)
type Color = White | Cyan | Green | Yellow | Red | Blue | Magenta | Gray | DarkGray

module TUI =
    // Internal: Convert to ConsoleColor
    let private toConsoleColor = function
        | White -> ConsoleColor.White
        | Cyan -> ConsoleColor.Cyan
        | Green -> ConsoleColor.Green
        | Yellow -> ConsoleColor.Yellow
        | Red -> ConsoleColor.Red
        | Blue -> ConsoleColor.Blue
        | Magenta -> ConsoleColor.Magenta
        | Gray -> ConsoleColor.Gray
        | DarkGray -> ConsoleColor.DarkGray

    // Internal: Print with color then reset
    let private withColor color action =
        Console.ForegroundColor <- toConsoleColor color
        action ()
        Console.ResetColor()

    // === Basic Output ===
    let text msg = printf "%s" msg
    let textLn msg = printfn "%s" msg
    let blank () = printfn ""

    // === Colored Text ===
    let colored color msg = withColor color (fun () -> printf "%s" msg)
    let coloredLn color msg = withColor color (fun () -> printfn "%s" msg)

    // Color shortcuts (no newline)
    let white msg = colored White msg
    let cyan msg = colored Cyan msg
    let green msg = colored Green msg
    let yellow msg = colored Yellow msg
    let red msg = colored Red msg
    let blue msg = colored Blue msg
    let magenta msg = colored Magenta msg
    let gray msg = colored Gray msg
    let darkGray msg = colored DarkGray msg

    // Color shortcuts (with newline)
    let whiteLn msg = coloredLn White msg
    let cyanLn msg = coloredLn Cyan msg
    let greenLn msg = coloredLn Green msg
    let yellowLn msg = coloredLn Yellow msg
    let redLn msg = coloredLn Red msg
    let blueLn msg = coloredLn Blue msg
    let magentaLn msg = coloredLn Magenta msg
    let grayLn msg = coloredLn Gray msg
    let darkGrayLn msg = coloredLn DarkGray msg

    // === Formatted Output ===
    let indent n msg = printf "%s%s" (String.replicate n " ") msg
    let indentLn n msg = printfn "%s%s" (String.replicate n " ") msg

    let numbered items =
        items |> List.iteri (fun i item ->
            printfn "  [%d] %s" (i + 1) item)

    let bullet items =
        items |> List.iter (fun item ->
            printfn "  - %s" item)

    // === Boxes/Dialogs ===
    let banner title =
        let width = max 50 (String.length title + 4)
        let padding = (width - String.length title - 2) / 2
        withColor Cyan (fun () ->
            printfn "%s%s%s" "╔" (String.replicate width "═") "╗"
            printfn "║%s%s%s║" (String.replicate padding " ") title (String.replicate (width - padding - String.length title) " ")
            printfn "%s%s%s" "╚" (String.replicate width "═") "╝"
        )
        blank ()

    let separator () =
        withColor DarkGray (fun () ->
            printfn "%s" (String.replicate 80 "─"))

    let box title lines =
        let maxLen = lines |> List.map String.length |> List.fold max (String.length title)
        let width = max 45 (maxLen + 4)
        withColor Cyan (fun () ->
            printfn "╭%s╮" (String.replicate width "─")
            printfn "│  %-*s  │" (width - 4) title
            printfn "├%s┤" (String.replicate width "─"))
        for line in lines do
            printfn "│  %-*s  │" (width - 4) line
        withColor Cyan (fun () ->
            printfn "╰%s╯" (String.replicate width "─"))

    let approvalBox title lines actions =
        let maxLen = lines |> List.map String.length |> List.fold max (max (String.length title) (String.length actions))
        let width = max 55 (maxLen + 4)
        withColor Yellow (fun () ->
            printfn "═%s═" (String.replicate width "═")
            printfn "  %s" title
            printfn "═%s═" (String.replicate width "═"))
        blank ()
        for line in lines do
            printfn "  %s" line
        blank ()
        withColor Green (fun () ->
            printf "  %s " actions)

    // === Prompts ===
    let prompt label =
        withColor White (fun () -> printf "%s> " label)

    let promptDefault label defaultVal =
        printf "%s [%s]: " label defaultVal

    let promptOptions options =
        withColor Green (fun () ->
            printf "[%s]: " (String.concat "/" options))

    let promptYN defaultYes =
        if defaultYes then printf "[Y/n]: "
        else printf "[y/N]: "

    // === Input ===
    let readLine () = Console.ReadLine()

    let readKey () = Console.ReadKey(true)

    let readKeyChar () = Console.ReadKey(true).KeyChar

    /// Read hidden input (e.g., password) recursively
    let rec readHidden (acc: string) : string =
        let key = Console.ReadKey(true)
        match key.Key with
        | ConsoleKey.Enter -> acc
        | ConsoleKey.Backspace ->
            let newAcc = if acc.Length > 0 then acc.[..acc.Length-2] else acc
            readHidden newAcc
        | _ -> readHidden (acc + string key.KeyChar)

    /// Read multi-line input until end marker
    let rec readMultiline (endMarker: string) (acc: string list) : string list =
        let line = Console.ReadLine()
        if line = endMarker then
            acc |> List.rev
        else
            readMultiline endMarker (line :: acc)

    /// Confirm yes/no question
    let confirm defaultValue label =
        printf "%s " label
        promptYN defaultValue
        let key = Console.ReadKey(true).KeyChar
        printfn ""
        match Char.ToLower key with
        | 'y' -> true
        | 'n' -> false
        | _ -> defaultValue

    /// Clear screen
    let clear () = Console.Clear()

// ============================================================================
// UI Module (Aliases for backward compatibility)
// ============================================================================

module UI =
    let banner = TUI.banner
    let separator = TUI.separator

// ============================================================================
// Console Module (Aliases for backward compatibility)
// ============================================================================

module Console =
    let readHiddenInput = TUI.readHidden
    let readMultilineUntil = TUI.readMultiline
    let confirm = TUI.confirm

// ============================================================================
// Functional Logger (Reader Pattern)
// ============================================================================

type LogLevel = Silent | Normal | Verbose

type Logger = {
    Level: LogLevel
    Write: string -> Color -> string -> unit
}

module Logger =
    let create level = {
        Level = level
        Write = fun prefix color msg ->
            if level <> Silent then
                let ts = DateTime.Now.ToString("HH:mm:ss.fff")
                TUI.colored color (sprintf "[%s] %s: " ts prefix)
                TUI.textLn msg
    }

    let info msg logger = logger.Write "INFO" Cyan msg
    let debug msg logger =
        if logger.Level = Verbose then logger.Write "DEBUG" DarkGray msg
    let warn msg logger = logger.Write "WARN" Yellow msg
    let error msg logger = logger.Write "ERROR" Red msg
    let success msg logger = logger.Write "OK" Green msg
    let send msg logger = logger.Write "SEND" Magenta msg
    let recv msg logger = logger.Write "RECV" Blue msg

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
                    TUI.white t
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

// Module-level logging functions (for backward compatibility)
let private defaultLogger = Logger.normal

let info msg = Logger.info msg defaultLogger
let debug msg = Logger.debug msg defaultLogger
let warn msg = Logger.warn msg defaultLogger
let error msg = Logger.error msg defaultLogger
let success msg = Logger.success msg defaultLogger
let send msg = Logger.send msg defaultLogger
let recv msg = Logger.recv msg defaultLogger
let logError e = Logger.logError e defaultLogger

// ============================================================================
// Client Helper Functions
// ============================================================================

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
                        | Text t -> TUI.white t
                        | ToolUse (id, name, _) ->
                            TUI.blank ()
                            info (sprintf "Tool call: %s" name)
                        | Thinking (t, _) ->
                            let preview = if t.Length > 50 then t.[..49] else t
                            debug (sprintf "Thinking: %s..." preview)
                        | _ -> ()
                | ResultMsg r ->
                    TUI.blank ()
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
