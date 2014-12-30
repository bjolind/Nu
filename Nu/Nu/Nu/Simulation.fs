﻿// Nu Game Engine.
// Copyright (C) Bryan Edds, 2013-2014.

namespace Nu
open System
open System.Collections.Generic
open System.ComponentModel
open System.Reflection
open System.Xml
open SDL2
open OpenTK
open Prime
open Nu
open Nu.Constants

[<AutoOpen>]
module InterativityModule =

    /// Describes the game engine's current level of 'interactivity'.
    type Interactivity =
        | GuiOnly
        | GuiAndPhysics
        | GuiAndPhysicsAndGamePlay

[<RequireQualifiedAccess>]
module Interactivity =

    /// Query that the engine is in game-playing mode.
    let isGamePlaying interactivity =
        match interactivity with
        | GuiOnly -> false
        | GuiAndPhysics -> false
        | GuiAndPhysicsAndGamePlay -> true

    /// Query that the physics system is running.
    let isPhysicsRunning interactivity =
        match interactivity with
        | GuiOnly -> false
        | GuiAndPhysics -> true
        | GuiAndPhysicsAndGamePlay -> true

[<AutoOpen>]
module TransitionTypeModule =

    /// The type of a screen transition. Incoming means a new screen is being shown, and Outgoing
    /// means an existing screen being hidden.
    type TransitionType =
        | Incoming
        | Outgoing

[<AutoOpen>]
module ScreenStateModule =

    /// The state of a screen in regards to its transition.
    type ScreenState =
        | IncomingState
        | OutgoingState
        | IdlingState

[<AutoOpen>]
module SimulationModule =

    /// Describes one of a screen's transition processes.
    type [<CLIMutable; StructuralEquality; NoComparison>] Transition =
        { TransitionLifetime : int64
          TransitionType : TransitionType
          OptDissolveImage : AssetTag option }

        static member make transitionType =
            { TransitionLifetime = 0L
              TransitionType = transitionType
              OptDissolveImage = None }

    /// The data for a mouse move event.
    type [<StructuralEquality; NoComparison>] MouseMoveData =
        { Position : Vector2 }

    /// The data for a mouse button event.
    type [<StructuralEquality; NoComparison>] MouseButtonData =
        { Position : Vector2
          Button : MouseButton
          Down : bool }

    /// The data for a keyboard key event.
    type [<StructuralEquality; NoComparison>] KeyboardKeyData =
        { ScanCode : int
          Repeated : bool
          Down : bool }

    /// The data for a collision event.
    type [<StructuralEquality; NoComparison>] CollisionData =
        { Normal : Vector2
          Speed : single
          Collidee : Entity Address }

    /// The data for a world state change event.
    and [<StructuralEquality; NoComparison>] WorldStateChangeData =
        { OldWorldState : WorldState }

    /// The data for a simulant change event.
    and [<StructuralEquality; NoComparison>] SimulantChangeData<'a when 'a :> Simulant> =
        { OldSimulant : 'a }

    /// An event used by Nu's purely functional event system.
    and [<ReferenceEquality>] Event<'a, 's when 's :> Simulant> =
        { SubscriberAddress : 's Address
          PublisherAddress : Simulant Address // TODO: consider making this a list so that Observer can provide all useful addresses
          EventAddress : 'a Address
          Subscriber : 's
          Data : 'a }

    /// Describes whether an event has been resolved or should cascade.
    and EventHandling =
        | Resolve
        | Cascade

    /// Describes a game event subscription.
    and Subscription<'a, 's when 's :> Simulant> =
        Event<'a, 's> -> World -> EventHandling * World

    /// Describes a game event subscription that can be boxed / unboxed.
    and BoxableSubscription = obj -> World -> EventHandling * World

    /// An entry into the world's subscription map.
    and SubscriptionEntry = Guid * obj Address * obj

    /// A map of event subscriptions.
    and SubscriptionEntries = Map<obj Address, SubscriptionEntry rQueue>

    /// Abstracts over a subscription sorting procedure.
    and SubscriptionSorter = SubscriptionEntry rQueue -> World -> SubscriptionEntry rQueue

    /// A map of subscription keys to unsubscription data.
    and UnsubscriptionEntries = Map<Guid, obj Address * obj Address>

    /// A task to be completed at the given time, with time being represented by the world's tick
    /// field.
    and [<ReferenceEquality>] Task =
        { ScheduledTime : int64
          Operation : World -> World }

    /// The default dispatcher for games.
    and GameDispatcher () =

        abstract member Register : Game * World -> Game * World
        default dispatcher.Register (game, world) = (game, world)

    /// The default dispatcher for screens.
    and ScreenDispatcher () =

        static member FieldDefinitions =
            [define? Persistent true]

        abstract member Register : Screen * Screen Address * World -> Screen * World
        default dispatcher.Register (screen, _, world) = (screen, world)

        abstract member Unregister : Screen * Screen Address * World -> Screen * World
        default dispatcher.Unregister (screen, _, world) = (screen, world)

    /// The default dispatcher for groups.
    and GroupDispatcher () =

        static member FieldDefinitions =
            [define? Persistent true]

        abstract member Register : Group * Group Address * World -> Group * World
        default dispatcher.Register (group, _, world) = (group, world)

        abstract member Unregister : Group * Group Address * World -> Group * World
        default dispatcher.Unregister (group, _, world) = (group, world)

    /// The default dispatcher for entities.
    and EntityDispatcher () =

        static member FieldDefinitions =
            [define? Position Vector2.Zero
             define? Depth 0.0f
             define? Size DefaultEntitySize
             define? Rotation 0.0f
             define? Visible true
             define? ViewType Relative
             define? PublishChanges false
             define? Persistent true]

        abstract member Register : Entity * Entity Address * World -> Entity * World
        default dispatcher.Register (entity, _, world) = (entity, world)

        abstract member Unregister : Entity * Entity Address * World -> Entity * World
        default dispatcher.Unregister (entity, _, world) = (entity, world)

        abstract member PropagatePhysics : Entity * Entity Address * World -> World
        default dispatcher.PropagatePhysics (_, _, world) = world

        abstract member GetRenderDescriptors : Entity * World -> RenderDescriptor list
        default dispatcher.GetRenderDescriptors (_, _) = []

        abstract member GetQuickSize : Entity * World -> Vector2
        default dispatcher.GetQuickSize (_, _) = Vector2.One

        abstract member GetPickingPriority : Entity * World -> single
        default dispatcher.GetPickingPriority (entity, _) = entity.Depth

    /// Dynamically augments an entity's behavior in a composable way.
    and Facet () =

        abstract member Register : Entity * Entity Address * World -> Entity * World
        default facet.Register (entity, address, world) =
            let world = facet.RegisterPhysics (entity, address, world)
            (entity, world)

        abstract member Unregister : Entity * Entity Address * World -> Entity * World
        default facet.Unregister (entity, address, world) =
            let world = facet.UnregisterPhysics (entity, address, world)
            (entity, world)

        abstract member RegisterPhysics : Entity * Entity Address * World -> World
        default facet.RegisterPhysics (_, _, world) = world

        abstract member UnregisterPhysics : Entity * Entity Address * World -> World
        default facet.UnregisterPhysics (_, _, world) = world

        abstract member PropagatePhysics : Entity * Entity Address * World -> World
        default facet.PropagatePhysics (_, _, world) = world

        abstract member GetRenderDescriptors : Entity * World -> RenderDescriptor list
        default facet.GetRenderDescriptors (_, _) = []

        abstract member GetQuickSize : Entity * World -> Vector2
        default facet.GetQuickSize (_, _) = DefaultEntitySize

    /// A marker interface for simulation types (Game, Screen, Group, Entity).
    /// The only methods that have a place in here are those used internally by Nu's event system.
    and Simulant =
        interface
            abstract member GetPublishingPriority : (Entity -> World -> single) -> World -> single
            end

    /// The game type that hosts the various screens used to navigate through a game.
    and [<CLIMutable; StructuralEquality; NoComparison>] Game =
        { Id : Guid
          OptSelectedScreenAddress : Screen Address option
          CreationTimeNp : DateTime
          DispatcherNp : GameDispatcher
          Xtension : Xtension }

        interface Simulant with
            member this.GetPublishingPriority _ _ = GamePublishingPriority

        static member (?) (this : Game, memberName) =
            Xtension.(?) (this.Xtension, memberName)

        static member (?<-) (this : Game, memberName, value) =
            let xtension = Xtension.(?<-) (this.Xtension, memberName, value)
            { this with Xtension = xtension }

    /// The screen type that allows transitioning to and fro other screens, and also hosts the
    /// currently interactive groups of entities.
    and [<CLIMutable; StructuralEquality; NoComparison>] Screen =
        { Id : Guid
          Name : string
          ScreenStateNp : ScreenState
          TransitionTicksNp : int64
          Incoming : Transition
          Outgoing : Transition
          Persistent : bool
          CreationTimeNp : DateTime
          DispatcherNp : ScreenDispatcher
          Xtension : Xtension }

        interface Simulant with
            member this.GetPublishingPriority _ _ = ScreenPublishingPriority

        static member (?) (this : Screen, memberName) =
            Xtension.(?) (this.Xtension, memberName)

        static member (?<-) (this : Screen, memberName, value) =
            let xtension = Xtension.(?<-) (this.Xtension, memberName, value)
            { this with Xtension = xtension }

    /// Forms logical groups of entities.
    and [<CLIMutable; StructuralEquality; NoComparison>] Group =
        { Id : Guid
          Name : string
          Persistent : bool
          CreationTimeNp : DateTime
          DispatcherNp : GroupDispatcher
          Xtension : Xtension }

        interface Simulant with
            member this.GetPublishingPriority _ _ = GroupPublishingPriority

        static member (?) (this : Group, memberName) =
            Xtension.(?) (this.Xtension, memberName)

        static member (?<-) (this : Group, memberName, value) =
            let xtension = Xtension.(?<-) (this.Xtension, memberName, value)
            { this with Xtension = xtension }

    /// The type around which the whole game engine is based! Used in combination with dispatchers
    /// to implement things like buttons, characters, blocks, and things of that sort.
    /// TODO: now that there are field descriptors, consider making their persistence configurable
    /// with data instead of name-suffixing.
    /// TODO: also now that there is sufficient type metadata in field descriptors, consider
    /// removing type names from serialized xfields as well as removing the ability to dynamically
    /// add and remove xfields in the editor. ALSO, this would allow xfields to be removed from the
    /// Xtension tag as well - so that could also happen!
    and [<CLIMutable; StructuralEquality; NoComparison>] Entity =
        { Id : Guid
          Name : string
          Position : Vector2 // NOTE: will become a Vector3 if Nu gets 3d capabilities
          Depth : single // NOTE: will become part of position if Nu gets 3d capabilities
          Size : Vector2 // NOTE: will become a Vector3 if Nu gets 3d capabilities
          Rotation : single // NOTE: will become a Vector3 if Nu gets 3d capabilities
          Visible : bool
          ViewType : ViewType
          PublishChanges : bool
          Persistent : bool
          CreationTimeNp : DateTime // just needed for ordering writes to reduce diff volumes
          DispatcherNp : EntityDispatcher
          FacetNames : string list
          FacetsNp : Facet list
          OptOverlayName : string option
          Xtension : Xtension }

        interface Simulant with
            member this.GetPublishingPriority getEntityPublishingPriority world =
                getEntityPublishingPriority this world

        static member (?) (this : Entity, memberName) =
            Xtension.(?) (this.Xtension, memberName)

        static member (?<-) (this : Entity, memberName, value) =
            let xtension = Xtension.(?<-) (this.Xtension, memberName, value)
            { this with Xtension = xtension }

    /// The world's components.
    and [<ReferenceEquality>] Components =
        { EntityDispatchers : Map<string, EntityDispatcher>
          GroupDispatchers : Map<string, GroupDispatcher>
          ScreenDispatchers : Map<string, ScreenDispatcher>
          GameDispatchers : Map<string, GameDispatcher>
          Facets : Map<string, Facet> }

    /// The world's subsystems.
    and [<ReferenceEquality>] Subsystems =
        { AudioPlayer : IAudioPlayer
          Renderer : IRenderer
          Integrator : IIntegrator
          Overlayer : Overlayer }

    /// The world's message queues.
    and [<ReferenceEquality>] MessageQueues =
        { AudioMessages : AudioMessage rQueue
          RenderMessages : RenderMessage rQueue
          PhysicsMessages : PhysicsMessage rQueue }

    /// The world's higher order facilities.
    and [<ReferenceEquality>] Callbacks =
        { Tasks : Task list
          Subscriptions : SubscriptionEntries
          Unsubscriptions : UnsubscriptionEntries
          CallbackStates : Map<Guid, obj> }

    /// The world's state.
    and [<ReferenceEquality>] WorldState =
        { TickTime : int64
          Liveness : Liveness
          Interactivity : Interactivity
          OptScreenTransitionDestinationAddress : Screen Address option
          Camera : Camera
          AssetMetadataMap : AssetMetadataMap
          AssetGraphFilePath : string
          OverlayRouter : OverlayRouter
          OverlayFilePath : string
          UserState : obj }

    /// The world, in a functional programming sense. Hosts the game object, the dependencies
    /// needed to implement a game, messages to by consumed by the various engine sub-systems,
    /// and general configuration data.
    ///
    /// TODO: attempt to implement with Fsharpx.PersistentHashMap with hash cached in Address type.
    and [<ReferenceEquality>] World =
        { Simulants : Game * Map<string, Screen * Map<string, Group * Map<string, Entity>>>
          Components : Components
          Subsystems : Subsystems
          MessageQueues : MessageQueues
          Callbacks : Callbacks
          State : WorldState }

    /// Provides a way to make user-defined dispatchers, facets, and various other sorts of game-
    /// specific values.
    and NuPlugin () =
        abstract MakeFacets : unit -> Facet list
        default this.MakeFacets () = []
        abstract MakeEntityDispatchers : unit -> EntityDispatcher list
        default this.MakeEntityDispatchers () = []
        abstract MakeGroupDispatchers : unit -> GroupDispatcher list
        default this.MakeGroupDispatchers () = []
        abstract MakeScreenDispatchers : unit -> ScreenDispatcher list
        default this.MakeScreenDispatchers () = []
        abstract MakeOptGameDispatcher : unit -> GameDispatcher option
        default this.MakeOptGameDispatcher () = None
        abstract MakeOverlayRoutes : unit -> (string * string option) list
        default this.MakeOverlayRoutes () = []