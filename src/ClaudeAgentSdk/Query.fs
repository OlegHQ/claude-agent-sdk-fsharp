module ClaudeAgentSdk.Query

open System.Collections.Generic
open System.Threading.Tasks
open FSharp.Control
open FsToolkit.ErrorHandling
open Thoth.Json.Net
open ClaudeAgentSdk

// ============================================================================
// One-Shot Query
// ============================================================================

/// Execute a one-shot query - sends prompt and yields all messages until result
let query (prompt: string) (options: Options) : IAsyncEnumerable<Result<Message, SdkError>> = taskSeq {
    // Find CLI
    match Transport.findCli options.CliPath with
    | Error e ->
        yield Error e

    | Ok cliPath ->
        // Build args for non-streaming mode
        let args = Transport.buildArgs options false

        // Spawn subprocess
        match Transport.spawn cliPath args options.Env options.Cwd with
        | Error e ->
            yield Error e

        | Ok conn ->
            // Write prompt to stdin
            let promptJson = Encode.toString 0 (Json.Encode.userMessage prompt "default")
            let! writeResult = Transport.write promptJson conn
            match writeResult with
            | Error e ->
                yield Error e
                do! Transport.close conn

            | Ok () ->
                // End stdin to signal we're done sending
                do! Transport.endInput conn

                // Read and parse messages
                let mutable isDone = false
                for line in Transport.readWithBuffer conn options.MaxBufferSize do
                    if not isDone then
                        match Parser.parseLine line with
                        | Error e ->
                            yield Error e

                        | Ok (RegularMessage msg) ->
                            yield Ok msg
                            match msg with
                            | ResultMsg _ -> isDone <- true
                            | _ -> ()

                        | Ok (ControlRequestLine _) ->
                            // In query mode, we can't respond to control requests
                            // since stdin is already closed. Just ignore them.
                            ()

                        | Ok (ControlResponseLine _) ->
                            // Ignore responses in query mode
                            ()

                // Clean up
                do! Transport.close conn
}

/// Execute a query with multiple input messages (streaming input mode)
let queryStreaming
    (messages: IAsyncEnumerable<string * string>) // (role, content) pairs
    (options: Options)
    : IAsyncEnumerable<Result<Message, SdkError>> = taskSeq {

    match Transport.findCli options.CliPath with
    | Error e ->
        yield Error e

    | Ok cliPath ->
        let args = Transport.buildArgs options true

        match Transport.spawn cliPath args options.Env options.Cwd with
        | Error e ->
            yield Error e

        | Ok conn ->
            let mutable writeError: SdkError option = None

            // Write all input messages
            for (role, content) in messages do
                if writeError.IsNone then
                    let msgJson =
                        Encode.object [
                            "type", Encode.string role
                            "message", Encode.object [
                                "role", Encode.string role
                                "content", Encode.string content
                            ]
                            "session_id", Encode.string "default"
                        ]
                        |> Encode.toString 0
                    let! result = Transport.write msgJson conn
                    match result with
                    | Error e -> writeError <- Some e
                    | Ok () -> ()

            match writeError with
            | Some e ->
                yield Error e
                do! Transport.close conn
            | None ->
                do! Transport.endInput conn

                // Read messages
                let mutable isDone = false
                for line in Transport.readWithBuffer conn options.MaxBufferSize do
                    if not isDone then
                        match Parser.parseMessage line with
                        | Error e -> yield Error e
                        | Ok msg ->
                            yield Ok msg
                            match msg with
                            | ResultMsg _ -> isDone <- true
                            | _ -> ()

                do! Transport.close conn
}

// ============================================================================
// Convenience Helpers
// ============================================================================

/// Execute a query and collect all messages into a list
let queryCollect (prompt: string) (options: Options) : Task<Result<Message list, SdkError>> = task {
    let messages = ResizeArray<Message>()
    let mutable lastError: SdkError option = None

    for result in query prompt options do
        match result with
        | Ok msg -> messages.Add(msg)
        | Error e -> lastError <- Some e

    match lastError with
    | Some e when messages.Count = 0 -> return Error e
    | _ -> return Ok (messages |> List.ofSeq)
}

/// Execute a query and return just the final result
let queryResult (prompt: string) (options: Options) : Task<Result<ResultMessage option, SdkError>> = task {
    let! result = queryCollect prompt options
    match result with
    | Error e -> return Error e
    | Ok messages ->
        return Ok (messages |> List.tryPick (function ResultMsg r -> Some r | _ -> None))
}

/// Execute a query and return the text result
let queryText (prompt: string) (options: Options) : Task<Result<string option, SdkError>> = task {
    let! result = queryResult prompt options
    match result with
    | Error e -> return Error e
    | Ok r -> return Ok (r |> Option.bind (fun r -> r.Result))
}

/// Execute a query and return structured output
let queryStructured (prompt: string) (options: Options) : Task<Result<JsonValue option, SdkError>> = task {
    let! result = queryResult prompt options
    match result with
    | Error e -> return Error e
    | Ok r -> return Ok (r |> Option.bind (fun r -> r.StructuredOutput))
}

