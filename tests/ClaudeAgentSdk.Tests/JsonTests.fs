module ClaudeAgentSdk.Tests.JsonTests

open Xunit
open FsUnit.Xunit
open Thoth.Json.Net
open ClaudeAgentSdk

module ContentBlockTests =

    [<Fact>]
    let ``decode text content block`` () =
        let json = """{"type":"text","text":"Hello world"}"""
        let result = Decode.fromString Json.Decode.contentBlock json
        match result with
        | Ok (Text t) -> t |> should equal "Hello world"
        | _ -> failwith "Expected Text content block"

    [<Fact>]
    let ``decode thinking content block`` () =
        let json = """{"type":"thinking","thinking":"Let me think...","signature":"abc123"}"""
        let result = Decode.fromString Json.Decode.contentBlock json
        match result with
        | Ok (Thinking(t, s)) ->
            t |> should equal "Let me think..."
            s |> should equal "abc123"
        | _ -> failwith "Expected Thinking content block"

    [<Fact>]
    let ``decode tool_use content block`` () =
        let json = """{"type":"tool_use","id":"tu_123","name":"read_file","input":{"path":"/tmp/test.txt"}}"""
        let result = Decode.fromString Json.Decode.contentBlock json
        match result with
        | Ok (ToolUse(id, name, _)) ->
            id |> should equal "tu_123"
            name |> should equal "read_file"
        | _ -> failwith "Expected ToolUse content block"

    [<Fact>]
    let ``decode tool_result content block`` () =
        let json = """{"type":"tool_result","tool_use_id":"tu_123","content":"file contents","is_error":false}"""
        let result = Decode.fromString Json.Decode.contentBlock json
        match result with
        | Ok (ToolResult(id, content, isError)) ->
            id |> should equal "tu_123"
            content |> should equal (Some "file contents")
            isError |> should equal (Some false)
        | _ -> failwith "Expected ToolResult content block"

module MessageTests =

    [<Fact>]
    let ``decode user message with string content`` () =
        let json = """{"type":"user","message":{"role":"user","content":"Hello"},"uuid":"msg_123"}"""
        let result = Decode.fromString Json.Decode.message json
        match result with
        | Ok (UserMsg m) ->
            m.Content |> should haveLength 1
            match m.Content.[0] with
            | Text t -> t |> should equal "Hello"
            | _ -> failwith "Expected Text block"
            m.Uuid |> should equal (Some "msg_123")
        | _ -> failwith "Expected UserMsg"

    [<Fact>]
    let ``decode assistant message`` () =
        let json = """{"type":"assistant","message":{"role":"assistant","content":[{"type":"text","text":"Hi there!"}],"model":"claude-3-opus"}}"""
        let result = Decode.fromString Json.Decode.message json
        match result with
        | Ok (AssistantMsg m) ->
            m.Model |> should equal "claude-3-opus"
            m.Content |> should haveLength 1
        | _ -> failwith "Expected AssistantMsg"

    [<Fact>]
    let ``decode system message`` () =
        let json = """{"type":"system","subtype":"init"}"""
        let result = Decode.fromString Json.Decode.message json
        match result with
        | Ok (SystemMsg m) ->
            m.Subtype |> should equal "init"
        | _ -> failwith "Expected SystemMsg"

    [<Fact>]
    let ``decode result message`` () =
        let json = """{
            "type":"result",
            "subtype":"success",
            "duration_ms":1000,
            "duration_api_ms":800,
            "is_error":false,
            "num_turns":3,
            "session_id":"sess_123",
            "total_cost_usd":0.05,
            "result":"Done!"
        }"""
        let result = Decode.fromString Json.Decode.message json
        match result with
        | Ok (ResultMsg m) ->
            m.Subtype |> should equal "success"
            m.DurationMs |> should equal 1000
            m.IsError |> should equal false
            m.NumTurns |> should equal 3
            m.SessionId |> should equal "sess_123"
            m.TotalCostUsd |> should equal (Some 0.05)
            m.Result |> should equal (Some "Done!")
        | _ -> failwith "Expected ResultMsg"

module PermissionTests =

    [<Fact>]
    let ``decode permission behavior`` () =
        let testCases = [
            "\"allow\"", Allow
            "\"deny\"", Deny
            "\"ask\"", Ask
        ]
        for (json, expected) in testCases do
            match Decode.fromString Json.Decode.permissionBehavior json with
            | Ok actual -> actual |> should equal expected
            | Error e -> failwith $"Decode failed: {e}"

    [<Fact>]
    let ``decode permission mode`` () =
        let testCases = [
            "\"default\"", Default
            "\"acceptEdits\"", AcceptEdits
            "\"plan\"", Plan
            "\"bypassPermissions\"", BypassPermissions
        ]
        for (json, expected) in testCases do
            match Decode.fromString Json.Decode.permissionMode json with
            | Ok actual -> actual |> should equal expected
            | Error e -> failwith $"Decode failed: {e}"

    [<Fact>]
    let ``encode permission result allow`` () =
        let result = PermitAllow(None, None)
        let json = Json.Encode.permissionResult result |> Encode.toString 0
        json |> should haveSubstring "\"behavior\":\"allow\""

    [<Fact>]
    let ``encode permission result deny`` () =
        let result = PermitDeny("Not allowed", true)
        let json = Json.Encode.permissionResult result |> Encode.toString 0
        json |> should haveSubstring "\"behavior\":\"deny\""
        json |> should haveSubstring "\"message\":\"Not allowed\""
        json |> should haveSubstring "\"interrupt\":true"

module HookTests =

    [<Fact>]
    let ``decode PreToolUse hook input`` () =
        let json = """{
            "hook_event_name":"PreToolUse",
            "session_id":"sess_1",
            "transcript_path":"/tmp/transcript",
            "cwd":"/home/user",
            "tool_name":"read_file",
            "tool_input":{"path":"/test.txt"}
        }"""
        let result = Decode.fromString Json.Decode.hookInput json
        match result with
        | Ok (PreToolUseInput(sid, tp, cwd, tn, _)) ->
            sid |> should equal "sess_1"
            tp |> should equal "/tmp/transcript"
            cwd |> should equal "/home/user"
            tn |> should equal "read_file"
        | _ -> failwith "Expected PreToolUseInput"

    [<Fact>]
    let ``decode PostToolUse hook input`` () =
        let json = """{
            "hook_event_name":"PostToolUse",
            "session_id":"sess_1",
            "transcript_path":"/tmp/transcript",
            "cwd":"/home/user",
            "tool_name":"write_file",
            "tool_input":{"path":"/test.txt"},
            "tool_response":{"success":true}
        }"""
        let result = Decode.fromString Json.Decode.hookInput json
        match result with
        | Ok (PostToolUseInput(sid, _, _, tn, _, _)) ->
            sid |> should equal "sess_1"
            tn |> should equal "write_file"
        | _ -> failwith "Expected PostToolUseInput"

    [<Fact>]
    let ``encode sync hook output`` () =
        let output = SyncHook(Some true, None, None, Some "allow", None, Some "approved", None)
        let json = Json.Encode.hookOutput output |> Encode.toString 0
        json |> should haveSubstring "\"continue\":true"
        json |> should haveSubstring "\"decision\":\"allow\""
        json |> should haveSubstring "\"reason\":\"approved\""

    [<Fact>]
    let ``encode async hook output`` () =
        let output = AsyncHook(Some 5000)
        let json = Json.Encode.hookOutput output |> Encode.toString 0
        json |> should haveSubstring "\"async\":true"
        json |> should haveSubstring "\"asyncTimeout\":5000"

module ControlProtocolTests =

    [<Fact>]
    let ``decode can_use_tool control request`` () =
        let json = """{
            "type":"control_request",
            "request_id":"req_123",
            "request":{
                "subtype":"can_use_tool",
                "tool_name":"bash",
                "input":{"command":"ls"}
            }
        }"""
        let result = Decode.fromString Json.Decode.controlRequest json
        match result with
        | Ok (CanUseToolRequest(rid, tn, _, _)) ->
            rid |> should equal "req_123"
            tn |> should equal "bash"
        | _ -> failwith "Expected CanUseToolRequest"

    [<Fact>]
    let ``encode control response success`` () =
        let json = Json.Encode.controlResponseSuccess "req_123" None |> Encode.toString 0
        json |> should haveSubstring "\"type\":\"control_response\""
        json |> should haveSubstring "\"subtype\":\"success\""
        json |> should haveSubstring "\"request_id\":\"req_123\""

    [<Fact>]
    let ``encode control response error`` () =
        let json = Json.Encode.controlResponseError "req_123" "Something went wrong" |> Encode.toString 0
        json |> should haveSubstring "\"subtype\":\"error\""
        json |> should haveSubstring "\"error\":\"Something went wrong\""

module UserMessageEncoderTests =

    [<Fact>]
    let ``encode user message`` () =
        let json = Json.Encode.userMessage "Hello Claude" "sess_abc" |> Encode.toString 0
        json |> should haveSubstring "\"type\":\"user\""
        json |> should haveSubstring "\"role\":\"user\""
        json |> should haveSubstring "\"content\":\"Hello Claude\""
        json |> should haveSubstring "\"session_id\":\"sess_abc\""

