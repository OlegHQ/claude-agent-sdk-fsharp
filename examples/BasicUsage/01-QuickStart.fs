/// Example 01: Quick Start - Simplest possible query
module Examples.QuickStart

open ClaudeAgentSdk
open Examples.Common

let run () = task {
    TUI.banner "Quick Start - Basic Query"

    info "Sending a simple query to Claude..."

    let! result = queryCollect "What is 2 + 2? Reply with just the number." Options.defaults

    match result with
    | Ok messages ->
        success "Query completed!"
        TUI.blank ()

        // Extract assistant text
        let texts = Client.getAssistantText messages
        for text in texts do
            TUI.greenLn (sprintf "Answer: %s" text)

    | Error e ->
        logError e

    TUI.blank ()
}
