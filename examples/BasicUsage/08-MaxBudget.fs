/// Example 08: Budget Control - Limit costs with MaxBudgetUsd
module Examples.MaxBudget

open ClaudeAgentSdk
open Examples.Common

let basicBudget () = task {
    TUI.banner "Budget Control - MaxBudgetUsd"

    let options = {
        Options.defaults with
            MaxBudgetUsd = Some 0.05  // 5 cents max
            MaxTurns = Some 3
    }

    info "Budget limit: $0.05"
    info "Max turns: 3"
    TUI.blank ()

    let! result = queryCollect "Write a detailed essay about artificial intelligence" options

    match result with
    | Ok messages ->
        // Extract result message
        let resultMsg = messages |> List.tryPick (function ResultMsg r -> Some r | _ -> None)

        match resultMsg with
        | Some r ->
            let cost = r.TotalCostUsd |> Option.defaultValue 0.0
            success (sprintf "Cost: $%.4f / $0.05" cost)
            info (sprintf "Turns: %d / 3" r.NumTurns)
            info (sprintf "Duration: %dms" r.DurationMs)

            if r.IsError then
                warn "Query stopped (budget or turns limit reached)"
        | None ->
            warn "No result message received"

    | Error e ->
        logError e

    TUI.blank ()
}

let compareModels () = task {
    TUI.banner "Budget Control - Compare Models"

    let prompt = "What is recursion?"

    // Haiku (cheaper)
    info "Running with Haiku..."
    let haikuOptions = {
        Options.defaults with
            Model = Some "claude-haiku-4-20250514"
            MaxBudgetUsd = Some 0.01
    }

    let! haikuResult = queryCollect prompt haikuOptions

    match haikuResult with
    | Ok messages ->
        let resultMsg = messages |> List.tryPick (function ResultMsg r -> Some r | _ -> None)
        match resultMsg with
        | Some r ->
            let cost = r.TotalCostUsd |> Option.defaultValue 0.0
            success (sprintf "Haiku cost: $%.6f" cost)
        | None -> ()
    | Error e ->
        logError e

    TUI.blank ()

    // Sonnet (more expensive)
    info "Running with Sonnet..."
    let sonnetOptions = {
        Options.defaults with
            Model = Some "claude-sonnet-4-20250514"
            MaxBudgetUsd = Some 0.05
    }

    let! sonnetResult = queryCollect prompt sonnetOptions

    match sonnetResult with
    | Ok messages ->
        let resultMsg = messages |> List.tryPick (function ResultMsg r -> Some r | _ -> None)
        match resultMsg with
        | Some r ->
            let cost = r.TotalCostUsd |> Option.defaultValue 0.0
            success (sprintf "Sonnet cost: $%.6f" cost)
        | None -> ()
    | Error e ->
        logError e

    TUI.blank ()
}

let runAll () = task {
    do! basicBudget ()
    TUI.separator ()
    do! compareModels ()
}
