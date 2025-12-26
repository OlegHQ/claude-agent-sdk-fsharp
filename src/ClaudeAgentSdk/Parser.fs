module ClaudeAgentSdk.Parser

open Thoth.Json.Net
open ClaudeAgentSdk

/// Parse a JSON line into a ParsedLine (Message, ControlRequest, or ControlResponse)
let parseLine (json: string) : Result<ParsedLine, SdkError> =
    match Decode.fromString Json.Decode.parsedLine json with
    | Ok parsed -> Ok parsed
    | Error err -> Error (ParseError err)

/// Parse a JSON line into a Message (ignores control protocol messages)
let parseMessage (json: string) : Result<Message, SdkError> =
    match Decode.fromString Json.Decode.message json with
    | Ok msg -> Ok msg
    | Error err -> Error (ParseError err)

/// Try to parse a JSON line, returning None if it fails
let tryParseLine (json: string) : ParsedLine option =
    match parseLine json with
    | Ok parsed -> Some parsed
    | Error _ -> None

/// Try to parse a JSON line as a Message, returning None if it fails
let tryParseMessage (json: string) : Message option =
    match parseMessage json with
    | Ok msg -> Some msg
    | Error _ -> None
