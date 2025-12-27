/// Example 01: Quick Start - Simplest possible query
module Examples.QuickStart

open System
open ClaudeAgentSdk
open Examples.Common

let run () = task {
    UI.banner "Quick Start - Basic Query"

    info "Sending a simple query to Claude..."

    let! result = queryCollect "What is 2 + 2? Reply with just the number." Options.defaults

    match result with
    | Ok messages ->
        success "Query completed!"
        printfn ""

        // Extract assistant text
        let texts = Client.getAssistantText messages
        for text in texts do
            Console.ForegroundColor <- ConsoleColor.Green
            printfn "Answer: %s" text
            Console.ResetColor()

    | Error e ->
        logError e

    printfn ""
}
