module ClaudeAgentSdk.Tests.SchemaTests

open Xunit
open FsUnit.Xunit
open Thoth.Json.Net
open ClaudeAgentSdk

// ============================================================================
// Consolidated Primitive Schema Tests
// ============================================================================

[<Theory>]
[<InlineData("string", "string")>]
[<InlineData("number", "number")>]
[<InlineData("bool", "boolean")>]
let ``primitive schemas encode to JSON schema`` (schemaType: string) (expectedType: string) =
    let schema =
        match schemaType with
        | "string" -> Mcp.Schema.string
        | "number" -> Mcp.Schema.number
        | "bool" -> Mcp.Schema.bool
        | _ -> failwith "Invalid schema type"

    let json = Json.Encode.schema schema |> Encode.toString 0
    json |> should equal (sprintf """{"type":"%s"}""" expectedType)

[<Fact>]
let ``schema with description includes description field`` () =
    let schema = Mcp.Schema.string' "A user's name"
    let json = Json.Encode.schema schema |> Encode.toString 0
    json |> should haveSubstring "\"type\":\"string\""
    json |> should haveSubstring "\"description\":\"A user's name\""

[<Fact>]
let ``array schema encodes with items`` () =
    let schema = Mcp.Schema.array Mcp.Schema.string
    let json = Json.Encode.schema schema |> Encode.toString 0
    json |> should haveSubstring "\"type\":\"array\""
    json |> should haveSubstring "\"items\":{\"type\":\"string\"}"

[<Fact>]
let ``object schema with required and optional fields`` () =
    let schema = Mcp.Schema.object' [
        Mcp.Schema.required "name" Mcp.Schema.string
        Mcp.Schema.optional "age" Mcp.Schema.number
    ]
    let json = Json.Encode.schema schema |> Encode.toString 0
    json |> should haveSubstring "\"type\":\"object\""
    json |> should haveSubstring "\"properties\""
    json |> should haveSubstring "\"name\""
    json |> should haveSubstring "\"age\""
    json |> should haveSubstring "\"required\":[\"name\"]"

[<Fact>]
let ``nested object schema encodes correctly`` () =
    let addressSchema = Mcp.Schema.object' [
        Mcp.Schema.required "street" Mcp.Schema.string
        Mcp.Schema.required "city" Mcp.Schema.string
    ]
    let personSchema = Mcp.Schema.object' [
        Mcp.Schema.required "name" Mcp.Schema.string
        Mcp.Schema.required "address" addressSchema
    ]
    let json = Json.Encode.schema personSchema |> Encode.toString 0
    json |> should haveSubstring "\"address\""
    json |> should haveSubstring "\"street\""
    json |> should haveSubstring "\"city\""

[<Fact>]
let ``any schema encodes to empty object`` () =
    let schema = Mcp.Schema.any
    let json = Json.Encode.schema schema |> Encode.toString 0
    json |> should equal "{}"
