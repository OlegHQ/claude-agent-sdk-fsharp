module ClaudeAgentSdk.Tests.HookTests

open Xunit
open FsUnit.Xunit
open Thoth.Json.Net
open ClaudeAgentSdk

module HookMatcherTests =

    [<Fact>]
    let ``matchAll creates matcher without pattern`` () =
        let hooks: HookCallback list = []
        let matcher = Hooks.matchAll hooks
        matcher.Matcher |> should equal None
        matcher.Timeout |> should equal None

    [<Fact>]
    let ``matchTool creates matcher with pattern`` () =
        let hooks: HookCallback list = []
        let matcher = Hooks.matchTool "Bash" hooks
        matcher.Matcher |> should equal (Some "Bash")

    [<Fact>]
    let ``withTimeout adds timeout to matcher`` () =
        let hooks: HookCallback list = []
        let matcher = Hooks.matchAll hooks |> Hooks.withTimeout 5000.0
        matcher.Timeout |> should equal (Some 5000.0)

module HookInputHelpersTests =

    let preToolUseInput = PreToolUseInput("sess_1", "/tmp/transcript", "/home/user", "read", Encode.nil)
    let postToolUseInput = PostToolUseInput("sess_2", "/tmp/transcript2", "/home/user2", "write", Encode.nil, Encode.nil)
    let userPromptInput = UserPromptSubmitInput("sess_3", "/tmp/t", "/home/u", "hello")
    let stopInput = StopInput("sess_4", "/tmp/t", "/cwd", true)

    [<Fact>]
    let ``getSessionId extracts session ID from all input types`` () =
        Hooks.getSessionId preToolUseInput |> should equal "sess_1"
        Hooks.getSessionId postToolUseInput |> should equal "sess_2"
        Hooks.getSessionId userPromptInput |> should equal "sess_3"
        Hooks.getSessionId stopInput |> should equal "sess_4"

    [<Fact>]
    let ``getTranscriptPath extracts transcript path`` () =
        Hooks.getTranscriptPath preToolUseInput |> should equal "/tmp/transcript"
        Hooks.getTranscriptPath postToolUseInput |> should equal "/tmp/transcript2"

    [<Fact>]
    let ``getCwd extracts working directory`` () =
        Hooks.getCwd preToolUseInput |> should equal "/home/user"
        Hooks.getCwd postToolUseInput |> should equal "/home/user2"

module HookOutputBuildersTests =

    [<Fact>]
    let ``continueHook creates continue response`` () =
        let output = Hooks.continueHook
        match output with
        | SyncHook(cont, _, _, _, _, _, _) ->
            cont |> should equal (Some true)
        | _ -> failwith "Expected SyncHook"

    [<Fact>]
    let ``blockHook creates block response with reason`` () =
        let output = Hooks.blockHook "Security violation"
        match output with
        | SyncHook(cont, _, stopReason, _, _, _, _) ->
            cont |> should equal (Some false)
            stopReason |> should equal (Some "Security violation")
        | _ -> failwith "Expected SyncHook"

    [<Fact>]
    let ``asyncHook creates async response`` () =
        let output = Hooks.asyncHook (Some 10000)
        match output with
        | AsyncHook timeout ->
            timeout |> should equal (Some 10000)
        | _ -> failwith "Expected AsyncHook"

    [<Fact>]
    let ``preToolUseResponse creates permission decision`` () =
        let output = Hooks.preToolUseResponse Allow (Some "Approved by user")
        match output with
        | SyncHook(_, _, _, decision, _, reason, specific) ->
            decision |> should equal (Some "allow")
            reason |> should equal (Some "Approved by user")
            match specific with
            | Some (PreToolUseOutput(pd, _, _)) ->
                pd |> should equal (Some Allow)
            | _ -> failwith "Expected PreToolUseOutput"
        | _ -> failwith "Expected SyncHook"

    [<Fact>]
    let ``postToolUseResponse creates context response`` () =
        let output = Hooks.postToolUseResponse (Some "Additional info")
        match output with
        | SyncHook(_, _, _, _, _, _, specific) ->
            match specific with
            | Some (PostToolUseOutput ctx) ->
                ctx |> should equal (Some "Additional info")
            | _ -> failwith "Expected PostToolUseOutput"
        | _ -> failwith "Expected SyncHook"

    [<Fact>]
    let ``withSystemMessage adds system message`` () =
        let output = Hooks.continueHook |> Hooks.withSystemMessage "System update"
        match output with
        | SyncHook(_, _, _, _, sysMsg, _, _) ->
            sysMsg |> should equal (Some "System update")
        | _ -> failwith "Expected SyncHook"

    [<Fact>]
    let ``suppressOutput sets suppress flag`` () =
        let output = Hooks.continueHook |> Hooks.suppressOutput
        match output with
        | SyncHook(_, suppress, _, _, _, _, _) ->
            suppress |> should equal (Some true)
        | _ -> failwith "Expected SyncHook"

module BuildHookCallbacksTests =

    [<Fact>]
    let ``buildHookCallbacks creates callback map`` () = task {
        let mutable called = false
        let callback: HookCallback = fun _ _ -> task {
            called <- true
            return Hooks.continueHook
        }

        let hooks = Map.ofList [
            PreToolUse, [{ Matcher = None; Hooks = [callback]; Timeout = None }]
        ]

        let callbackMap = Hooks.buildHookCallbacks hooks

        // Should have one callback registered
        callbackMap |> Map.count |> should equal 1

        // The callback ID should follow pattern hook_{event}_{matcherIdx}_{hookIdx}
        callbackMap |> Map.containsKey "hook_PreToolUse_0_0" |> should equal true

        // Invoking should work
        let testInput = PreToolUseInput("sess", "/t", "/cwd", "test", Encode.nil)
        let storedCallback = callbackMap.["hook_PreToolUse_0_0"]
        let! _ = storedCallback testInput None
        called |> should equal true
    }

    [<Fact>]
    let ``buildHookCallbacks handles multiple matchers and hooks`` () =
        let callback1: HookCallback = fun _ _ -> task { return Hooks.continueHook }
        let callback2: HookCallback = fun _ _ -> task { return Hooks.blockHook "no" }

        let hooks = Map.ofList [
            PreToolUse, [
                { Matcher = None; Hooks = [callback1; callback2]; Timeout = None }
                { Matcher = Some "Bash"; Hooks = [callback1]; Timeout = None }
            ]
            PostToolUse, [
                { Matcher = None; Hooks = [callback1]; Timeout = None }
            ]
        ]

        let callbackMap = Hooks.buildHookCallbacks hooks

        // Should have 4 callbacks total
        callbackMap |> Map.count |> should equal 4

        callbackMap |> Map.containsKey "hook_PreToolUse_0_0" |> should equal true
        callbackMap |> Map.containsKey "hook_PreToolUse_0_1" |> should equal true
        callbackMap |> Map.containsKey "hook_PreToolUse_1_0" |> should equal true
        callbackMap |> Map.containsKey "hook_PostToolUse_0_0" |> should equal true

