# Screen Flow (scene)

A scene you can press Play on. It exercises the whole navigation layer — push, pop, replace,
pop-to-root, a modal, suspend/resume and Back — rather than illustrating one call.

## Import and run

1. Package Manager → Cuvara UI Toolkit → Samples → **Screen Flow (scene)** → Import.
2. Open `Assets/Samples/.../ScreenFlow/ScreenFlowSample.unity`.
3. Press Play.

**The scene lives under `Samples~`, so Unity cannot see it until the Package Manager copies
it into `Assets/`.** A directory whose name ends in `~` is skipped by the asset database
entirely — you will not find this scene by searching the project before importing it.

## What to click, and what to watch

The HUD in the top-left is the point. It reports `Depth`, the top screen and its
`ScreenLifecycleState` every frame, so suspend and resume are things you *see* rather than
infer:

| Click | What the HUD should show |
|---|---|
| **Push second screen** | Depth 1 → 2. The root goes to `Suspended` and its view moves to the hidden layer |
| **Pop (back)** | Depth 2 → 1, the root returns to `Active` — and its `OnBindAsync` does NOT run again |
| **Show modal** | Depth +1. The screen below stays **`Active`**, dimmed and non-interactive — try clicking its buttons |
| **Replace** | Depth stays the same, and the screen below never flashes, because it is never resumed |
| **Pop to root** | Depth → 1 whatever it was |
| **Escape / gamepad B / Android Back** | closes the top screen. At the root it does whatever `RootBackPolicy` says |

The Back line counts presses seen versus presses handled. At the root with the default
`RootBackPolicy.NotHandled` you should see the count of seen rise and handled stay put —
that is the navigator *declining*, which is what leaves the platform's own Back working.
Change the policy in `ScreenFlowSampleBootstrap` to `Consume` or `Raise` and watch it change.

## What this sample is showing you about the API

Five steps to add a screen, and step 4 is the only real work:

1. UXML + USS
2. one file with the view and the presenter
3. `builder.RegisterScreen<MyPresenter, MyView>("MyKey")`
4. the body of `OnBindAsync` — the screen itself
5. `await nav.PushAsync<MyPresenter>()`

**Zero lifecycle code.** No `Dispose` override, no `UnregisterCallback`, no
`CancellationTokenSource`, no scope handling. Everything a screen registers goes into the
`ScreenSubscriptions` handed to `OnBindAsync`, and the screen's container scope releases it.

## Two things this sample exists to prove, learned the hard way

**A MonoBehaviour must live in a file named after it.** `ScreenFlowSampleScope` is in
`ScreenFlowSampleScope.cs` and not beside the rest, because Unity maps one MonoScript per file
by filename — a scene referencing a MonoBehaviour declared in a differently-named file
serialises with `m_Script: {fileID: 0}` and loads as *"the referenced script is missing"*. It
compiles cleanly either way, so only opening a scene catches it.

**Do not touch the panel in `Initialize()`.** `UIDocument` builds its `rootVisualElement` in
its own `OnEnable`, and the order of two components across two GameObjects is undefined. The
bootstrap here yields one frame before reading the panel root. The package's `RootUIDocument`
resolves its *layers* lazily so the navigator is immune, but anything reaching for the panel
root directly — as the HUD and the back source do — has to wait for it.

## What CI does and does not do with this

The package's CI copies every declared sample into a throwaway project and compiles it, so
this sample is **compiled** from the moment it lands. Nothing in CI **runs** a scene. A green
tick therefore means "it builds", never "the flow was exercised" — that part is you, pressing
Play.
