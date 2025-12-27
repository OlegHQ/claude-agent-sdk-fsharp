module ClaudeAgentSdk.Events

open System
open ClaudeAgentSdk

// ============================================================================
// Event Bus Module
// ============================================================================

module EventBus =
    /// Create a new event bus
    let create () : EventBus =
        { Subscriptions = ref [] }

    /// Subscribe to events with a handler function
    let subscribe (handler: EventHandler) (bus: EventBus) : Guid =
        let id = Guid.NewGuid()
        let sub = { Id = id; Handler = handler }
        bus.Subscriptions.Value <- sub :: bus.Subscriptions.Value
        id

    /// Unsubscribe from events using the subscription ID
    let unsubscribe (id: Guid) (bus: EventBus) : unit =
        bus.Subscriptions.Value <-
            bus.Subscriptions.Value |> List.filter (fun s -> s.Id <> id)

    /// Publish an event to all subscribers
    let publish (event: SdkEvent) (bus: EventBus) : unit =
        for sub in bus.Subscriptions.Value do
            try
                sub.Handler event
            with _ ->
                ()  // Isolate handler exceptions to prevent breaking the event loop
