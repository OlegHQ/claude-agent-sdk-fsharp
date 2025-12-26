module ClaudeAgentSdk.Tests.McpTests

open Xunit
open FsUnit.Xunit
open Thoth.Json.Net
open ClaudeAgentSdk

module ToolCreationTests =

    [<Fact>]
    let ``create simple tool`` () =
        let tool = Mcp.tool "greet" "Says hello" Mcp.Schema.any (fun _ -> task {
            return Mcp.textResult "Hello!"
        })
        tool.Name |> should equal "greet"
        tool.Description |> should equal "Says hello"

    [<Fact>]
    let ``create tool with schema`` () =
        let schema = Mcp.Schema.object' [
            Mcp.Schema.required "name" Mcp.Schema.string
        ]
        let tool = Mcp.tool "greet" "Greet by name" schema (fun input -> task {
            match Mcp.getString "name" input with
            | Ok name -> return Mcp.textResult $"Hello, {name}!"
            | Error e -> return Mcp.errorResult e
        })
        tool.Name |> should equal "greet"

module ToolOutputTests =

    [<Fact>]
    let ``textResult creates success with text content`` () =
        let output = Mcp.textResult "Hello world"
        match output with
        | ToolSuccess [TextContent t] -> t |> should equal "Hello world"
        | _ -> failwith "Expected ToolSuccess with TextContent"

    [<Fact>]
    let ``errorResult creates failure`` () =
        let output = Mcp.errorResult "Something went wrong"
        match output with
        | ToolFailure msg -> msg |> should equal "Something went wrong"
        | _ -> failwith "Expected ToolFailure"

    [<Fact>]
    let ``imageResult creates success with image content`` () =
        let data = [| 0x89uy; 0x50uy; 0x4Euy; 0x47uy |] // PNG header
        let output = Mcp.imageResult data "image/png"
        match output with
        | ToolSuccess [ImageContent(d, mime)] ->
            d |> should equal data
            mime |> should equal "image/png"
        | _ -> failwith "Expected ToolSuccess with ImageContent"

    [<Fact>]
    let ``multiResult creates success with multiple contents`` () =
        let output = Mcp.multiResult [
            TextContent "Line 1"
            TextContent "Line 2"
        ]
        match output with
        | ToolSuccess contents -> contents |> should haveLength 2
        | _ -> failwith "Expected ToolSuccess"

module InputParsingTests =

    [<Fact>]
    let ``getString extracts string field`` () =
        let input = Encode.object ["name", Encode.string "Alice"]
        match Mcp.getString "name" input with
        | Ok s -> s |> should equal "Alice"
        | Error e -> failwith e

    [<Fact>]
    let ``getString returns error for missing field`` () =
        let input = Encode.object []
        let result = Mcp.getString "name" input
        match result with
        | Error e -> e |> should haveSubstring "name"
        | Ok _ -> failwith "Expected error"

    [<Fact>]
    let ``tryGetString returns Some for existing field`` () =
        let input = Encode.object ["name", Encode.string "Bob"]
        let result = Mcp.tryGetString "name" input
        result |> should equal (Some "Bob")

    [<Fact>]
    let ``tryGetString returns None for missing field`` () =
        let input = Encode.object []
        let result = Mcp.tryGetString "name" input
        result |> should equal None

    [<Fact>]
    let ``getInt extracts int field`` () =
        let input = Encode.object ["count", Encode.int 42]
        match Mcp.getInt "count" input with
        | Ok n -> n |> should equal 42
        | Error e -> failwith e

    [<Fact>]
    let ``getBool extracts bool field`` () =
        let input = Encode.object ["enabled", Encode.bool true]
        match Mcp.getBool "enabled" input with
        | Ok b -> b |> should equal true
        | Error e -> failwith e

    [<Fact>]
    let ``getFloat extracts float field`` () =
        let input = Encode.object ["price", Encode.float 19.99]
        match Mcp.getFloat "price" input with
        | Ok f -> f |> should equal 19.99
        | Error e -> failwith e

    [<Fact>]
    let ``getStringList extracts string list field`` () =
        let input = Encode.object ["tags", Encode.list [Encode.string "a"; Encode.string "b"]]
        match Mcp.getStringList "tags" input with
        | Ok lst -> lst |> should equal ["a"; "b"]
        | Error e -> failwith e

module McpRequestHandlingTests =

    let testTools = [
        Mcp.tool "echo" "Echoes input" Mcp.Schema.any (fun input -> task {
            let text = Mcp.tryGetString "text" input |> Option.defaultValue "no input"
            return Mcp.textResult text
        })
        Mcp.tool "fail" "Always fails" Mcp.Schema.any (fun _ -> task {
            return Mcp.errorResult "Intentional failure"
        })
    ]

    [<Fact>]
    let ``handle initialize request`` () = task {
        let request = Encode.object [
            "jsonrpc", Encode.string "2.0"
            "method", Encode.string "initialize"
            "id", Encode.int 1
        ]
        let! response = Mcp.handleMcpRequest testTools request
        let responseStr = Encode.toString 0 response
        responseStr |> should haveSubstring "\"protocolVersion\""
        responseStr |> should haveSubstring "\"capabilities\""
    }

    [<Fact>]
    let ``handle tools/list request`` () = task {
        let request = Encode.object [
            "jsonrpc", Encode.string "2.0"
            "method", Encode.string "tools/list"
            "id", Encode.int 1
        ]
        let! response = Mcp.handleMcpRequest testTools request
        let responseStr = Encode.toString 0 response
        responseStr |> should haveSubstring "\"tools\""
        responseStr |> should haveSubstring "\"echo\""
        responseStr |> should haveSubstring "\"fail\""
    }

    [<Fact>]
    let ``handle tools/call success`` () = task {
        let request = Encode.object [
            "jsonrpc", Encode.string "2.0"
            "method", Encode.string "tools/call"
            "id", Encode.int 1
            "params", Encode.object [
                "name", Encode.string "echo"
                "arguments", Encode.object ["text", Encode.string "Hello!"]
            ]
        ]
        let! response = Mcp.handleMcpRequest testTools request
        let responseStr = Encode.toString 0 response
        responseStr |> should haveSubstring "\"Hello!\""
        responseStr |> should haveSubstring "\"isError\":false"
    }

    [<Fact>]
    let ``handle tools/call failure`` () = task {
        let request = Encode.object [
            "jsonrpc", Encode.string "2.0"
            "method", Encode.string "tools/call"
            "id", Encode.int 1
            "params", Encode.object [
                "name", Encode.string "fail"
                "arguments", Encode.object []
            ]
        ]
        let! response = Mcp.handleMcpRequest testTools request
        let responseStr = Encode.toString 0 response
        responseStr |> should haveSubstring "\"Intentional failure\""
        responseStr |> should haveSubstring "\"isError\":true"
    }

    [<Fact>]
    let ``handle unknown tool`` () = task {
        let request = Encode.object [
            "jsonrpc", Encode.string "2.0"
            "method", Encode.string "tools/call"
            "id", Encode.int 1
            "params", Encode.object [
                "name", Encode.string "nonexistent"
                "arguments", Encode.object []
            ]
        ]
        let! response = Mcp.handleMcpRequest testTools request
        let responseStr = Encode.toString 0 response
        responseStr |> should haveSubstring "Tool not found"
        responseStr |> should haveSubstring "\"isError\":true"
    }

    [<Fact>]
    let ``handle unknown method`` () = task {
        let request = Encode.object [
            "jsonrpc", Encode.string "2.0"
            "method", Encode.string "unknown/method"
            "id", Encode.int 1
        ]
        let! response = Mcp.handleMcpRequest testTools request
        let responseStr = Encode.toString 0 response
        responseStr |> should haveSubstring "\"error\""
        responseStr |> should haveSubstring "Method not found"
    }

module SdkServerTests =

    [<Fact>]
    let ``createSdkServer creates SdkServer`` () =
        let tools = [
            Mcp.tool "test" "Test tool" Mcp.Schema.any (fun _ -> task { return Mcp.textResult "ok" })
        ]
        let server = Mcp.createSdkServer "my-server" tools
        match server with
        | SdkServer(name, ts) ->
            name |> should equal "my-server"
            ts |> should haveLength 1
        | _ -> failwith "Expected SdkServer"

