module ClaudeAgentSdk.Tests.ProtocolTests

open Xunit
open FsUnit.Xunit
open Thoth.Json.Net
open ClaudeAgentSdk

module ControlRequestHandlingTests =

    [<Fact>]
    let ``handle CanUseToolRequest with allow callback`` () = task {
        let canUseTool: string -> JsonValue -> PermissionContext -> System.Threading.Tasks.Task<PermissionResult> =
            fun toolName _ _ -> task {
                if toolName = "safe_tool" then
                    return PermitAllow(None, None)
                else
                    return PermitDeny("Not allowed", false)
            }

        let request = CanUseToolRequest("req_1", "safe_tool", Encode.nil, [])
        let! response = Protocol.handleControlRequest (Some canUseTool) Map.empty Map.empty request

        match response with
        | SuccessResponse(rid, Some resp) ->
            rid |> should equal "req_1"
            let respStr = Encode.toString 0 resp
            respStr |> should haveSubstring "allow"
        | _ -> failwith "Expected SuccessResponse"
    }

    [<Fact>]
    let ``handle CanUseToolRequest with deny callback`` () = task {
        let canUseTool: string -> JsonValue -> PermissionContext -> System.Threading.Tasks.Task<PermissionResult> =
            fun _ _ _ -> task {
                return PermitDeny("Dangerous operation", true)
            }

        let request = CanUseToolRequest("req_2", "dangerous_tool", Encode.nil, [])
        let! response = Protocol.handleControlRequest (Some canUseTool) Map.empty Map.empty request

        match response with
        | SuccessResponse(rid, Some resp) ->
            rid |> should equal "req_2"
            let respStr = Encode.toString 0 resp
            respStr |> should haveSubstring "deny"
            respStr |> should haveSubstring "Dangerous operation"
        | _ -> failwith "Expected SuccessResponse"
    }

    [<Fact>]
    let ``handle CanUseToolRequest without callback`` () = task {
        let request = CanUseToolRequest("req_3", "some_tool", Encode.nil, [])
        let! response = Protocol.handleControlRequest None Map.empty Map.empty request

        match response with
        | ErrorResponse(rid, error) ->
            rid |> should equal "req_3"
            error |> should haveSubstring "not provided"
        | _ -> failwith "Expected ErrorResponse"
    }

    [<Fact>]
    let ``handle HookCallbackRequest with registered callback`` () = task {
        let mutable callbackInvoked = false
        let hookCallback: HookCallback = fun input toolUseId -> task {
            callbackInvoked <- true
            return Hooks.continueHook
        }

        let hookCallbacks = Map.ofList ["hook_1", hookCallback]
        let input = PreToolUseInput("sess", "/t", "/cwd", "tool", Encode.nil)
        let request = HookCallbackRequest("req_4", "hook_1", input, None)

        let! response = Protocol.handleControlRequest None hookCallbacks Map.empty request

        callbackInvoked |> should equal true
        match response with
        | SuccessResponse(rid, _) -> rid |> should equal "req_4"
        | _ -> failwith "Expected SuccessResponse"
    }

    [<Fact>]
    let ``handle HookCallbackRequest with unregistered callback`` () = task {
        let request = HookCallbackRequest("req_5", "nonexistent", StopInput("", "", "", false), None)
        let! response = Protocol.handleControlRequest None Map.empty Map.empty request

        match response with
        | ErrorResponse(rid, error) ->
            rid |> should equal "req_5"
            error |> should haveSubstring "not found"
        | _ -> failwith "Expected ErrorResponse"
    }

    [<Fact>]
    let ``handle McpMessageRequest with registered server`` () = task {
        let tools = [
            Mcp.tool "test" "Test tool" Mcp.Schema.any (fun _ -> task { return Mcp.textResult "ok" })
        ]

        let mcpServers = Map.ofList ["my-server", tools]
        let mcpMessage = Encode.object [
            "jsonrpc", Encode.string "2.0"
            "method", Encode.string "tools/list"
            "id", Encode.int 1
        ]
        let request = McpMessageRequest("req_6", "my-server", mcpMessage)

        let! response = Protocol.handleControlRequest None Map.empty mcpServers request

        match response with
        | SuccessResponse(rid, Some resp) ->
            rid |> should equal "req_6"
            let respStr = Encode.toString 0 resp
            respStr |> should haveSubstring "mcp_response"
        | _ -> failwith "Expected SuccessResponse"
    }

    [<Fact>]
    let ``handle McpMessageRequest with unregistered server`` () = task {
        let request = McpMessageRequest("req_7", "nonexistent", Encode.nil)
        let! response = Protocol.handleControlRequest None Map.empty Map.empty request

        match response with
        | ErrorResponse(rid, error) ->
            rid |> should equal "req_7"
            error |> should haveSubstring "not found"
        | _ -> failwith "Expected ErrorResponse"
    }

    [<Fact>]
    let ``handle InitializeRequest`` () = task {
        let request = InitializeRequest "req_8"
        let! response = Protocol.handleControlRequest None Map.empty Map.empty request

        match response with
        | SuccessResponse(rid, _) -> rid |> should equal "req_8"
        | _ -> failwith "Expected SuccessResponse"
    }

    [<Fact>]
    let ``handle SetPermissionModeRequest`` () = task {
        let request = SetPermissionModeRequest("req_9", AcceptEdits)
        let! response = Protocol.handleControlRequest None Map.empty Map.empty request

        match response with
        | SuccessResponse(rid, _) -> rid |> should equal "req_9"
        | _ -> failwith "Expected SuccessResponse"
    }

    [<Fact>]
    let ``handle SetModelRequest`` () = task {
        let request = SetModelRequest("req_10", Some "claude-3-opus")
        let! response = Protocol.handleControlRequest None Map.empty Map.empty request

        match response with
        | SuccessResponse(rid, _) -> rid |> should equal "req_10"
        | _ -> failwith "Expected SuccessResponse"
    }

    [<Fact>]
    let ``handle RewindFilesRequest`` () = task {
        let request = RewindFilesRequest("req_11", "msg_123")
        let! response = Protocol.handleControlRequest None Map.empty Map.empty request

        match response with
        | SuccessResponse(rid, _) -> rid |> should equal "req_11"
        | _ -> failwith "Expected SuccessResponse"
    }

    [<Fact>]
    let ``handle InterruptRequest`` () = task {
        let request = InterruptRequest "req_12"
        let! response = Protocol.handleControlRequest None Map.empty Map.empty request

        match response with
        | SuccessResponse(rid, _) -> rid |> should equal "req_12"
        | _ -> failwith "Expected SuccessResponse"
    }

module EncodeControlResponseTests =

    [<Fact>]
    let ``encodeControlResponse for success`` () =
        let response = SuccessResponse("req_1", Some (Encode.string "done"))
        let json = Protocol.encodeControlResponse response
        json |> should haveSubstring "control_response"
        json |> should haveSubstring "success"
        json |> should haveSubstring "req_1"

    [<Fact>]
    let ``encodeControlResponse for error`` () =
        let response = ErrorResponse("req_2", "Something failed")
        let json = Protocol.encodeControlResponse response
        json |> should haveSubstring "control_response"
        json |> should haveSubstring "error"
        json |> should haveSubstring "Something failed"

