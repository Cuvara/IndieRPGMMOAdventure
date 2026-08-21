# UI architecture contract

Authoritative for all UI work in this client. Set by the project owner on 2026-08-21.
Where this file and a habit disagree, this file wins.

## The rule in one line

**Use the correct UI technology for each responsibility** — not "UXML everywhere".

| Kind of UI | Technology |
|---|---|
| Screen / application UI | UI Toolkit — UXML + USS + MVP + VContainer |
| World-space and combat UI | Prefab / uGUI |

UI Toolkit is the **default** for screens. Do not create a Canvas + GameObject + prefab
screen without a technical reason.

## uGUI is not legacy

This is the half that gets misread, so it is stated first. The prefab/uGUI path is
**permanent and preferred** for:

world-space UI · combat UI attached to entities · HP bars above characters and enemies ·
damage numbers · heal numbers · floating text · target indicators · NPC interaction
indicators · boss world-space UI · anything needing a `Transform` or world position ·
anything driven by `Animator` · anything driven by DOTween · anything needing
`ParticleSystem`/VFX · anything pooled and spawned/despawned frequently in the world.

Do not convert these to UXML to satisfy a default. Architecture serves technical
requirements, not ideology. Nothing in any document should describe uGUI here as legacy.

## Layers, and what each may know

```
UI Toolkit  ->  View  ->  Presenter  ->  Service  ->  Repository / ECS / Network
```

Dependencies run one way, down that list. Never reverse one without written justification.

**UXML** — structure only: element hierarchy, labels, buttons, images, ListView,
ScrollView, containers, and the names/classes the View queries. No business logic, no
network calls, no ECS queries, no state mutation, no domain rules.

**USS** — presentation only: layout, spacing, sizing, typography, colour, borders,
backgrounds, states, shared component and theme styles. Prefer reusable classes over
duplicated blocks. No logic of any kind.

**View** — the adapter between UI Toolkit and MVP. It knows `VisualElement`, `Button`,
`Label`, `ListView`, `UIDocument`, the UXML structure and the USS classes. It queries
elements, registers UI events, and renders a ViewModel. It holds **no** business rules,
network logic or gameplay logic.

**Presenter** — presentation and application logic. Reacts to View events, asks services
for data, turns that data into ViewModels, tells the View what to display. It must **not**
depend on `UIDocument`, `VisualElement`, `Button`, `Label`, UXML or USS — it talks to
`IThingView` and `IThingService`. Never inject a `UIDocument` into a Presenter; that type
belongs to the View boundary.

**Service** — application and gameplay logic. Knows nothing about UI Toolkit. May talk to
ECS, repositories, the network layer and persistence.

**ECS/DOTS** — simulation and data. **ECS must never manipulate UI Toolkit.** Not
`VisualElement`, not `Button`, not `Label`. The path is:

```
ECS -> application/presentation adapter -> Presenter -> View -> UI Toolkit
```

There is also a hard technical reason reinforcing that boundary, verified against the
installed 6000.3.9f1 assembly rather than assumed: `VisualElement` is plain managed C#, not
a `UnityEngine.Object`, so it cannot be touched from `ISystem`, `IJobEntity`, Burst or any
worker thread at all. The adapter therefore runs on the main thread and hands over a plain
ViewModel — no `VisualElement`, no `VisualTreeAsset`, no `UIDocument` in it.

## Dependency injection

VContainer is the composition root. Constructor injection for plain C# classes:

```csharp
public sealed class InventoryPresenter
{
    private readonly IInventoryView    view;
    private readonly IInventoryService service;

    public InventoryPresenter(IInventoryView view, IInventoryService service)
    {
        this.view    = view;
        this.service = service;
    }
}
```

No service locators. No static globals beyond what the existing architecture already
requires.

## Screen layout on disk

```
Assets/UI/Screens/Inventory/
    Inventory.uxml
    Inventory.uss
    IInventoryView.cs
    InventoryView.cs
    InventoryPresenter.cs
    InventoryLifetimeScope.cs
    InventoryViewModel.cs      (if needed)
    IInventoryService.cs       (if screen-specific)
    InventoryService.cs
```

Unrelated gameplay logic does not live in the UI folder.

## Reusable components

```
UI/Core/         UIManager, UIScreen, UIContext, UIEvents
UI/Components/   ItemSlot, TabButton, StatRow, CurrencyDisplay, Tooltip,
                 CharacterCard, ProgressBar, Notification
```

A component is `Component.uxml` + `Component.uss` + `ComponentView.cs` + `IComponentView.cs`.
Screens compose components; they do not re-implement them.

## Lists and data-heavy UI

Inventory, quests, guild members, friends, mail, leaderboard, shop — use `ListView`
virtualization. Never instantiate hundreds of GameObjects to represent data, and never do
this per frame:

```
Clear() -> create 100 elements -> destroy 100 -> recreate 100
```

Update only what changed, or recycle. `RefreshItems()` for a partial update; `Rebuild()`
only when the source object identity changes.

## Performance

UXML is a definition format; it does not make UI fast by itself. Cost comes from tree size,
layout complexity, style resolution, rebuild frequency, rendering, textures, fonts, element
count and update frequency.

Keep trees small. Reuse components. Virtualize long lists. Update on data change, not per
frame. Cache element references instead of re-querying the tree. Profile before optimising,
and do not optimise prematurely.

## Navigation

One navigation abstraction, centralised — open, close, replace, push, pop, screen stack,
modals. **Do not scatter `SetActive(true/false)` through gameplay code, and do not introduce
a second navigation system** beside the one that already exists. Follow the existing API.

## Testability

A Presenter must be testable as a plain C# class, with no scene, no `UIDocument`, no
`VisualElement`, no UXML and no USS — mock the view and service interfaces. Unity and UI
Toolkit integration is the View's job, and only the View's.

## Before implementing anything

1. Screen UI or world/combat UI?
2. UXML or prefab?
3. Which layer owns the behaviour?
4. What interface does the View expose?
5. What does the Presenter coordinate?
6. Which Service owns the business logic?
7. How does VContainer wire it?
8. Where does the data come from?
9. Does it need virtualization?
10. Does a reusable component already solve it?

If an abstraction exists, reuse it rather than building a parallel one.

## Avoid

God views · god presenters · UI singletons · static service locators · direct ECS access
from UI · direct network calls from UI · business logic in callbacks · excessive
MonoBehaviours · unnecessary GameObjects · duplicated styling across screens · a second
UI architecture per screen.

## Design workflow

Figma is the design source: Figma → UXML + USS → View → MVP. Generated UXML/USS is a
starting point only — normalise names, drop dead elements, extract reusable components,
clean the USS, and keep the MVP boundaries intact. Do not trust a generated tree as shipped.
