module ClaudeAgentSdk.Streaming

open System.Collections.Generic
open System.Threading.Tasks
open FSharp.Control
open ClaudeAgentSdk

// ============================================================================
// Streaming Helper Functions
// ============================================================================

/// Iterate over messages with side effects, collecting any errors
let forEach (handler: Message -> unit) (stream: IAsyncEnumerable<Result<Message, SdkError>>) : Task<SdkError option> = task {
    let mutable lastError = None
    for result in stream do
        match result with
        | Ok msg -> handler msg
        | Error e -> lastError <- Some e
    return lastError
}

/// Iterate over messages until the handler returns false
let forEachWhile (handler: Message -> bool) (stream: IAsyncEnumerable<Result<Message, SdkError>>) : Task<SdkError option> = task {
    let mutable continueLoop = true
    let mutable lastError = None
    for result in stream do
        if continueLoop then
            match result with
            | Ok msg -> continueLoop <- handler msg
            | Error e ->
                lastError <- Some e
                continueLoop <- false
    return lastError
}

/// Collect all messages and errors from a stream
let collect (stream: IAsyncEnumerable<Result<Message, SdkError>>) : Task<Message list * SdkError option> = task {
    let! results = stream |> TaskSeq.toListAsync
    let messages = results |> List.choose (function Ok m -> Some m | _ -> None)
    let errors = results |> List.choose (function Error e -> Some e | _ -> None)
    return (messages, errors |> List.tryHead)
}

/// Filter messages by a predicate
let filterMessages (predicate: Message -> bool) (stream: IAsyncEnumerable<Result<Message, SdkError>>) : IAsyncEnumerable<Result<Message, SdkError>> =
    TaskSeq.choose (function
        | Ok msg when predicate msg -> Some (Ok msg)
        | Error e -> Some (Error e)
        | _ -> None) stream

/// Take messages until a ResultMsg is encountered
let takeUntilResult (stream: IAsyncEnumerable<Result<Message, SdkError>>) : IAsyncEnumerable<Result<Message, SdkError>> = taskSeq {
    let mutable foundResult = false
    for result in stream do
        if not foundResult then
            yield result
            match result with
            | Ok (ResultMsg _) -> foundResult <- true
            | _ -> ()
}
