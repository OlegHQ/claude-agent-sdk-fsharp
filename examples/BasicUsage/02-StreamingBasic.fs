/// Example 02: Streaming Basics - TaskSeq patterns without mutable state
module Examples.StreamingBasic

open FSharp.Control
open ClaudeAgentSdk
open Examples.Common

let streamingQuery () = task {
    TUI.banner "Streaming Query - Real-time Output"

    info "Streaming: 'Write a haiku about F#'..."
    TUI.blank ()

    // Pattern 1: Direct iteration with for loop (no mutable state!)
    for result in query "Write a haiku about F#" Options.defaults do
        match result with
        | Ok msg ->
            match msg with
            | AssistantMsg m ->
                for block in m.Content do
                    match block with
                    | Text t -> TUI.white t
                    | _ -> ()
            | ResultMsg r ->
                TUI.blank ()
                success (sprintf "Done! %d turns" r.NumTurns)
            | _ -> ()
        | Error e -> logError e

    TUI.blank ()
}

let collectStream () = task {
    TUI.banner "Collect Stream - Gather All Messages"

    info "Collecting stream into list..."

    // Pattern 2: Collect into list using TaskSeq
    let! messages =
        query "Count to 5" Options.defaults
        |> TaskSeq.choose (function Ok m -> Some m | Error _ -> None)
        |> TaskSeq.toListAsync

    success (sprintf "Collected %d messages" (List.length messages))

    TUI.blank ()
}

let foldStream () = task {
    TUI.banner "Fold Stream - Count Messages"

    // Pattern 3: Fold over stream (pure functional)
    let! messageCount =
        query "Hello Claude, how are you?" Options.defaults
        |> TaskSeq.fold (fun count result ->
            match result with
            | Ok (AssistantMsg _) -> count + 1
            | _ -> count
        ) 0

    info (sprintf "Assistant sent %d messages" messageCount)

    TUI.blank ()
}

let customOptions () = task {
    TUI.banner "Custom Options - Model, Prompt, Budget"

    let options = {
        Options.defaults with
            Model = Some "claude-sonnet-4-20250514"
            SystemPrompt = Some (SystemPromptText "Be concise and helpful.")
            MaxTurns = Some 3
            MaxBudgetUsd = Some 0.10
    }

    info "Query with custom options..."
    debug "  Model: claude-sonnet-4-20250514"
    debug "  MaxTurns: 3"
    debug "  MaxBudget: $0.10"

    let! result = queryCollect "Explain monads in one sentence" options

    match result with
    | Ok messages ->
        for text in Client.getAssistantText messages do
            TUI.blank ()
            TUI.cyanLn text
            TUI.blank ()
    | Error e ->
        logError e
}

let runAll () = task {
    do! streamingQuery ()
    TUI.separator ()
    do! collectStream ()
    TUI.separator ()
    do! foldStream ()
    TUI.separator ()
    do! customOptions ()
}
