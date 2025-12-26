/// Claude Agent SDK F# Examples - Entry Point
module Examples.Program

open System

[<EntryPoint>]
let main args =
    printfn "Claude Agent SDK for F# - Examples"
    printfn "=================================="
    printfn ""

    let example =
        if args.Length > 0 then args.[0]
        else "all"

    let runExample name runner =
        task {
            try
                do! runner ()
            with ex ->
                printfn "Example '%s' failed: %s" name ex.Message
                printfn "(This is expected if Claude CLI is not installed)"
        }

    let run = task {
        match example.ToLower() with
        | "query" | "simple" ->
            printfn "Running: Simple Query Examples\n"
            do! runExample "SimpleQuery" Examples.SimpleQuery.runAll

        | "client" | "interactive" ->
            printfn "Running: Interactive Client Examples\n"
            do! runExample "InteractiveClient" Examples.InteractiveClient.runAll

        | "mcp" | "tools" ->
            printfn "Running: MCP Tools Examples\n"
            do! runExample "McpTools" Examples.McpTools.runAll

        | "hooks" | "permissions" ->
            printfn "Running: Hooks and Permissions Examples\n"
            do! runExample "HooksAndPermissions" Examples.HooksAndPermissions.runAll

        | "human" | "hitl" | "approval" ->
            printfn "Running: Human-in-the-Loop Examples\n"
            do! runExample "HumanInTheLoop" Examples.HumanInTheLoop.runAll

        | "ask" | "askuser" | "clarify" ->
            printfn "Running: Ask User Tool Examples\n"
            do! runExample "AskUserTool" Examples.AskUserTool.runAll

        | "all" | _ ->
            printfn "Running all examples...\n"
            printfn "Note: Examples that require Claude CLI will fail if not installed.\n"

            do! runExample "SimpleQuery" Examples.SimpleQuery.runAll
            printfn "\n----------------------------------------\n"

            do! runExample "InteractiveClient" Examples.InteractiveClient.runAll
            printfn "\n----------------------------------------\n"

            do! runExample "McpTools" Examples.McpTools.runAll
            printfn "\n----------------------------------------\n"

            do! runExample "HooksAndPermissions" Examples.HooksAndPermissions.runAll
            printfn "\n----------------------------------------\n"

            do! runExample "HumanInTheLoop" Examples.HumanInTheLoop.runAll
            printfn "\n----------------------------------------\n"

            do! runExample "AskUserTool" Examples.AskUserTool.runAll

        printfn "\n=================================="
        printfn "Examples completed."
    }

    run.GetAwaiter().GetResult()
    0

