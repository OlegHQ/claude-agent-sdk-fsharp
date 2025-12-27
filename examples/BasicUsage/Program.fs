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
        | "01" | "quick" | "quickstart" ->
            printfn "Running: 01-QuickStart\n"
            do! runExample "QuickStart" Examples.QuickStart.run

        | "02" | "streaming" | "stream" ->
            printfn "Running: 02-StreamingBasic\n"
            do! runExample "StreamingBasic" Examples.StreamingBasic.runAll

        | "03" | "mcp" | "tools" ->
            printfn "Running: 03-McpTools\n"
            do! runExample "McpTools" Examples.McpTools.runAll

        | "04" | "hooks" | "permissions" ->
            printfn "Running: 04-HooksAndPermissions\n"
            do! runExample "HooksAndPermissions" Examples.HooksAndPermissions.runAll

        | "05" | "interactive" | "repl" ->
            printfn "Running: 05-InteractiveSession\n"
            do! runExample "InteractiveSession" Examples.InteractiveSession.runAll

        | "06" | "human" | "hitl" ->
            printfn "Running: 06-HumanInTheLoop\n"
            do! runExample "HumanInTheLoop" Examples.HumanInTheLoop.runAll

        | "07" | "events" ->
            printfn "Running: 07-EventsExample\n"
            do! runExample "EventsExample" Examples.EventsExample.runAll

        | "08" | "budget" ->
            printfn "Running: 08-MaxBudget\n"
            do! runExample "MaxBudget" Examples.MaxBudget.runAll

        | "09" | "structured" | "json" ->
            printfn "Running: 09-StructuredOutput\n"
            do! runExample "StructuredOutput" Examples.StructuredOutput.runAll

        | "11" | "ask" | "askuser" ->
            printfn "Running: 11-AskUserTool\n"
            do! runExample "AskUserTool" Examples.AskUserTool.run

        | "all" | _ ->
            printfn "Running all examples...\n"
            printfn "Note: Examples that require Claude CLI will fail if not installed.\n"

            do! runExample "01-QuickStart" Examples.QuickStart.run
            printfn "\n----------------------------------------\n"

            do! runExample "02-StreamingBasic" Examples.StreamingBasic.runAll
            printfn "\n----------------------------------------\n"

            do! runExample "03-McpTools" Examples.McpTools.runAll
            printfn "\n----------------------------------------\n"

            do! runExample "04-HooksAndPermissions" Examples.HooksAndPermissions.runAll
            printfn "\n----------------------------------------\n"

            do! runExample "05-InteractiveSession" Examples.InteractiveSession.runAll
            printfn "\n----------------------------------------\n"

            do! runExample "06-HumanInTheLoop" Examples.HumanInTheLoop.runAll
            printfn "\n----------------------------------------\n"

            do! runExample "07-EventsExample" Examples.EventsExample.runAll
            printfn "\n----------------------------------------\n"

            do! runExample "08-MaxBudget" Examples.MaxBudget.runAll
            printfn "\n----------------------------------------\n"

            do! runExample "09-StructuredOutput" Examples.StructuredOutput.runAll
            printfn "\n----------------------------------------\n"

            do! runExample "11-AskUserTool" Examples.AskUserTool.run

        printfn "\n=================================="
        printfn "Examples completed."
    }

    run.GetAwaiter().GetResult()
    0
