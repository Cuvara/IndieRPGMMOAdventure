# Session recovery

What happens to the player when the session drops or fails, and how the client decides
whether to try again. Audit items CN-01, CN-02 and CN-10 (A3-CLIENT-NET).

## Reconnect and the Nakama token (CN-01)

Netcode's reconnect loop gets a fresh gateway JWT from `IAuthProvider` on every round, and
after each failed round asks `ReconnectPolicy.IsPermanentFailure`. For a provider failure that
function answers one way: **an `InvalidOperationException` ends the loop at once**; every other
exception type is retried within the budget (30 s entity hold, 60 s budget).

`NakamaAuthProvider` therefore sorts a failed `gateway_token` RPC into two kinds:

| Failure | Thrown | Reconnect loop |
|---|---|---|
| Nakama refused the player: `ApiResponseException` 401 / 403 | `InvalidOperationException` | stops, `ReconnectFailed` |
| Empty or invalid payload, no `token` field, no session | `InvalidOperationException` | stops, `ReconnectFailed` |
| Timeout, `HttpRequestException`, socket / IO error, HTTP 5xx / 429 / any other status, no status (connection failed) | `GatewayTokenUnavailableException` | tries the next round |
| A cancel from the caller's own token | `OperationCanceledException` (unchanged) | exits quietly, the new operation owns the state |

A timeout reaches the provider as `TaskCanceledException`, which *is* an
`OperationCanceledException`. Only a cancel of the caller's token passes through as one;
any other is wrapped, because the loop reads a stray `OperationCanceledException` as "the user
left" and returns without changing state.

Before this, every RPC failure was wrapped in `InvalidOperationException`. A Wi-Fi hand-off
whose first round hit a still-dead network aborted the reconnect after one attempt.

## What the player sees (CN-02)

`MainSessionDriver` owns a `SessionRecovery` (pure, `Scripts.Session`) and feeds it:

| Source | Status |
|---|---|
| `ConnectionStateChanged` to `InWorld`, `Reconnected`, a finished `MainSessionFlow` in `InWorld` | `Active` (overlay hidden) |
| `ConnectionStateChanged` to `Reconnecting` | `Reconnecting` ("Connection lost. Reconnecting...") |
| `ConnectionStateChanged` to `Ended` with a reason other than `user` | `Ended` with the disconnect reason, e.g. `Kicked (duplicate_login)` |
| `ReconnectFailed` | `Ended` with the exception message (replaces the generic "reconnect exhausted") |
| `MainSessionFlow` in `Failed` | `Ended` with the exception message |

`Ended` with reason `user` is the client's own `Disconnect()` (scope teardown) and shows
nothing. A close the client reconnects from passes through `Ended` into `Reconnecting` in one
call chain, so it never renders.

`SessionStatusPresenter` connects `SessionRecovery` to an `ISessionStatusView`.
`SessionStatusOverlay` is the runtime view: a dim layer, the caption and a **Retry** button,
built in code over the gameplay panel's `PanelSettings` (sort order 1000). It is absent in a
headless run, where the session then logs only.

**Retry** runs a fresh `MainSessionFlow` (a flow is single-shot): device auth, character,
party, `NetworkClient.ConnectAsync`. While it runs the status is `Reconnecting` with no button,
so a double click starts one login. A failed retry returns to `Ended` with the new cause.

There is **no Back-to-login button**: this client has no login screen (`MainSessionDriver`
logs in on its own). Add one beside Retry when a screen exists to go back to.

## Tests

| Fixture | Covers |
|---|---|
| `NakamaAuthProviderTests` | the table above; a real `NetworkClient` reconnecting through the provider: first round transient, second lands (`ReconnectFailed` silent, attempts 1 and 2); a 401 gives up after attempt 1 |
| `SessionRecoveryTests` | statuses, Retry reaching `InWorld`, failed retry, no double login, presenter wiring |
| `MainSessionDriverTests` | a scripted kick shows the cause and Retry reaches `InWorld`; an exhausted reconnect; a failed login; dispose cancels the flow, disconnects the client and detaches the handlers first |

The Nakama side is the production SDK client over `FakeNakamaHttp`, a scripted
`IHttpAdapter` (`NakamaSessionService` has a constructor taking one); the netcode side is the
real `NetworkClient` over `ScriptedNetcode`'s scripted transports.

Not covered: the overlay's `VisualElement` tree (no panel in EditMode), and reconnect against a
live server (the `LiveBackend` category, which CI excludes).
