/// Example 07: Event System - Subscribe to SDK events
module Examples.EventsExample

open System
open ClaudeAgentSdk
open Examples.Common

let basicEvents () = task {
    UI.banner "Events - Basic Event Subscription"

    let logger = Logger.normal

    // Create event bus
    let bus = Events.createBus ()

    // Create event handler
    let eventHandler event =
        match event with
        | MessageReceived msg ->
            debug (sprintf "[EVENT] Message received: %s" (msg.GetType().Name))
        | MessageSent (prompt, _) ->
            let preview = if prompt.Length > 30 then prompt.[..29] + "..." else prompt
            debug (sprintf "[EVENT] Message sent: %s" preview)
        | ToolUseStarted (name, id, _) ->
            info (sprintf "[EVENT] Tool started: %s (%s)" name id)
        | ThinkingStarted content ->
            let preview = if content.Length > 40 then content.[..39] + "..." else content
            debug (sprintf "[EVENT] Thinking: %s" preview)
        | ConnectionEstablished cliPath ->
            success (sprintf "[EVENT] Connected: %s" cliPath)
        | _ -> ()

    let _subId = Events.subscribe eventHandler bus

    let options = {
        Options.defaults with
            Events = Some bus
    }

    Logger.info "Querying with event monitoring..." logger

    let! result = queryCollect "What is functional programming?" options

    match result with
    | Ok messages ->
        success (sprintf "Completed with %d messages" (List.length messages))
    | Error e ->
        logError e

    printfn ""
}

let multipleSubscribers () = task {
    UI.banner "Events - Multiple Subscribers"

    let logger = Logger.normal

    let bus = Events.createBus ()

    // Subscriber 1: Count messages
    let messageCount = ref 0
    let handler1 event =
        match event with
        | MessageReceived _ -> messageCount.Value <- messageCount.Value + 1
        | _ -> ()

    // Subscriber 2: Count tool uses
    let toolCount = ref 0
    let handler2 event =
        match event with
        | ToolUseStarted _ -> toolCount.Value <- toolCount.Value + 1
        | _ -> ()

    // Subscriber 3: Log thinking
    let handler3 event =
        match event with
        | ThinkingStarted content ->
            let preview = if content.Length > 50 then content.[..49] else content
            printfn "[THINKING] %s" preview
        | _ -> ()

    let _sub1 = Events.subscribe handler1 bus
    let _sub2 = Events.subscribe handler2 bus
    let _sub3 = Events.subscribe handler3 bus

    let options = {
        Options.defaults with
            Events = Some bus
            SystemPrompt = Some (SystemPromptText "Think step by step.")
    }

    let! result = queryCollect "Solve: If x + 5 = 12, what is x?" options

    match result with
    | Ok _ ->
        info (sprintf "Messages received: %d" messageCount.Value)
        info (sprintf "Tools used: %d" toolCount.Value)
    | Error e ->
        logError e

    printfn ""
}

let unsubscribeExample () = task {
    UI.banner "Events - Unsubscribe"

    let logger = Logger.normal

    let bus = Events.createBus ()

    // Subscribe temporarily
    let handler event =
        match event with
        | MessageReceived _ -> Logger.debug "[EVENT] Message" logger
        | _ -> ()

    let subId = Events.subscribe handler bus

    Logger.info "Subscribed to events..." logger

    // Query with subscription
    let! result1 = queryCollect "Hello!" { Options.defaults with Events = Some bus }

    match result1 with
    | Ok messages ->
        success (sprintf "Query 1: %d messages (events active)" (List.length messages))
    | Error e ->
        logError e

    // Unsubscribe
    Events.unsubscribe subId bus
    Logger.info "Unsubscribed from events" logger

    // Query without subscription
    let! result2 = queryCollect "Hello again!" { Options.defaults with Events = Some bus }

    match result2 with
    | Ok messages ->
        success (sprintf "Query 2: %d messages (events inactive)" (List.length messages))
    | Error e ->
        logError e

    printfn ""
}

let runAll () = task {
    do! basicEvents ()
    UI.separator ()
    do! multipleSubscribers ()
    UI.separator ()
    do! unsubscribeExample ()
}
