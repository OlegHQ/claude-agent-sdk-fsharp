module ClaudeAgentSdk.Tests.TransportTests

open Xunit
open FsUnit.Xunit
open ClaudeAgentSdk

module FindCliTests =

    [<Fact>]
    let ``findCli with explicit path that exists returns Ok`` () =
        let result = Transport.findCli (Some "/bin/sh")
        match result with
        | Ok path -> path |> should equal "/bin/sh"
        | Error (CliNotFound _) -> () // Skip on Windows
        | Error _ -> ()

    [<Fact>]
    let ``findCli with explicit path that doesn't exist returns Error`` () =
        let result = Transport.findCli (Some "/nonexistent/path/to/cli")
        match result with
        | Error (CliNotFound msg) -> msg |> should haveSubstring "not found"
        | Ok _ -> failwith "Expected CliNotFound error"

module BuildArgsTests =

    let defaultOptions = Options.defaults

    [<Fact>]
    let ``buildArgs includes required flags`` () =
        let args = Transport.buildArgs defaultOptions false
        args |> should contain "--output-format"
        args |> should contain "stream-json"
        args |> should contain "--verbose"

    // Consolidated: String options with flag and value
    [<Theory>]
    [<InlineData("system-prompt", "Be helpful")>]
    [<InlineData("model", "claude-3-opus")>]
    [<InlineData("fallback-model", "claude-3-haiku")>]
    [<InlineData("resume", "sess_123")>]
    let ``buildArgs with string options`` (flag: string) (value: string) =
        let options =
            match flag with
            | "system-prompt" -> { defaultOptions with SystemPrompt = Some (SystemPromptText value) }
            | "model" -> { defaultOptions with Model = Some value }
            | "fallback-model" -> { defaultOptions with FallbackModel = Some value }
            | "resume" -> { defaultOptions with Resume = Some value }
            | _ -> defaultOptions

        let args = Transport.buildArgs options false
        args |> should contain (sprintf "--%s" flag)
        args |> should contain value

    // Consolidated: Numeric options
    [<Theory>]
    [<InlineData("max-turns", 10)>]
    [<InlineData("max-thinking-tokens", 1000)>]
    let ``buildArgs with numeric options`` (flag: string) (value: int) =
        let options =
            match flag with
            | "max-turns" -> { defaultOptions with MaxTurns = Some value }
            | "max-thinking-tokens" -> { defaultOptions with MaxThinkingTokens = Some value }
            | _ -> defaultOptions

        let args = Transport.buildArgs options false
        args |> should contain (sprintf "--%s" flag)
        args |> should contain (string value)

    // Consolidated: Boolean flags
    [<Theory>]
    [<InlineData("continue")>]
    [<InlineData("fork-session")>]
    [<InlineData("include-partial-messages")>]
    let ``buildArgs with boolean flags`` (flag: string) =
        let options =
            match flag with
            | "continue" -> { defaultOptions with ContinueConversation = true }
            | "fork-session" -> { defaultOptions with ForkSession = true }
            | "include-partial-messages" -> { defaultOptions with IncludePartialMessages = true }
            | _ -> defaultOptions

        let args = Transport.buildArgs options false
        args |> should contain (sprintf "--%s" flag)

    [<Fact>]
    let ``buildArgs with empty system prompt`` () =
        let options = { defaultOptions with SystemPrompt = None }
        let args = Transport.buildArgs options false
        args |> should contain "--system-prompt"
        args |> should contain ""

    [<Fact>]
    let ``buildArgs with max budget`` () =
        let options = { defaultOptions with MaxBudgetUsd = Some 5.0 }
        let args = Transport.buildArgs options false
        args |> should contain "--max-budget-usd"
        args |> should contain "5"

    [<Fact>]
    let ``buildArgs with Claude Code preset append`` () =
        let options = { defaultOptions with SystemPrompt = Some (SystemPromptPresetConfig (ClaudeCodePreset (Some "Extra"))) }
        let args = Transport.buildArgs options false
        args |> should contain "--append-system-prompt"
        args |> should contain "Extra"

    [<Fact>]
    let ``buildArgs with tools list`` () =
        let options = { defaultOptions with Tools = Some (ToolsList ["Read"; "Write"; "Bash"]) }
        let args = Transport.buildArgs options false
        args |> should contain "--tools"
        let toolsArg = args |> List.find (fun a -> a.Contains("Read"))
        toolsArg |> should haveSubstring "Read"
        toolsArg |> should haveSubstring "Write"

    [<Fact>]
    let ``buildArgs with empty tools list`` () =
        let options = { defaultOptions with Tools = Some (ToolsList []) }
        let args = Transport.buildArgs options false
        args |> should contain "--tools"
        args |> should contain ""

    [<Fact>]
    let ``buildArgs with allowed tools`` () =
        let options = { defaultOptions with AllowedTools = ["Bash"; "Read"] }
        let args = Transport.buildArgs options false
        args |> should contain "--allowedTools"

    [<Fact>]
    let ``buildArgs with disallowed tools`` () =
        let options = { defaultOptions with DisallowedTools = ["Write"] }
        let args = Transport.buildArgs options false
        args |> should contain "--disallowedTools"

    [<Fact>]
    let ``buildArgs with permission modes`` () =
        let testCases = [
            Default, "default"
            AcceptEdits, "acceptEdits"
            Plan, "plan"
            BypassPermissions, "bypassPermissions"
        ]
        for (mode, expected) in testCases do
            let options = { defaultOptions with PermissionMode = Some mode }
            let args = Transport.buildArgs options false
            args |> should contain "--permission-mode"
            args |> should contain expected

    [<Fact>]
    let ``buildArgs with streaming mode adds input-format`` () =
        let args = Transport.buildArgs defaultOptions true
        args |> should contain "--input-format"
        args |> should contain "stream-json"

    [<Fact>]
    let ``buildArgs without streaming mode doesn't add input-format`` () =
        let args = Transport.buildArgs defaultOptions false
        args |> should not' (contain "--input-format")

    [<Fact>]
    let ``buildArgs with add dirs`` () =
        let options = { defaultOptions with AddDirs = ["/path/to/dir1"; "/path/to/dir2"] }
        let args = Transport.buildArgs options false
        let addDirCount = args |> List.filter ((=) "--add-dir") |> List.length
        addDirCount |> should equal 2

    [<Fact>]
    let ``buildArgs with output format schema`` () =
        let schema = Mcp.Schema.object' [
            Mcp.Schema.required "answer" Mcp.Schema.string
        ]
        let options = { defaultOptions with OutputFormat = Some schema }
        let args = Transport.buildArgs options false
        args |> should contain "--json-schema"

    [<Fact>]
    let ``buildArgs with extra args`` () =
        let options = { defaultOptions with ExtraArgs = Map.ofList [("custom-flag", Some "value"); ("no-value-flag", None)] }
        let args = Transport.buildArgs options false
        args |> should contain "--custom-flag"
        args |> should contain "value"
        args |> should contain "--no-value-flag"

    [<Fact>]
    let ``buildArgs with agents`` () =
        let agent = {
            Description = "Test agent"
            Prompt = "Do testing"
            Tools = Some ["Read"; "Write"]
            Model = Some Haiku
        }
        let options = { defaultOptions with Agents = Map.ofList ["test-agent", agent] }
        let args = Transport.buildArgs options false
        args |> should contain "--agents"
        let agentsArg = args |> List.find (fun a -> a.Contains("test-agent"))
        agentsArg |> should haveSubstring "Test agent"
