# Harness markers and diagnostic hooks

## The `[DOTSNet]` marker contract

The session log lines printed by `MainSessionFlow` (`Assets/Scripts/Session/MainSessionFlow.cs`) are
read by machines, not only people. The exact text is pinned by
`Tests.Editor.HarnessMarkerContractTests.HarnessMarkers_MatchTheContractByteForByte`.

| Constant | Text | Consumer |
|---|---|---|
| `Tag` | `[DOTSNet]` | prefix of every line below |
| `AuthenticatingPrefix` | `[DOTSNet] Authenticating device=` | logs read by hand and by the 401 diagnosis in CLAUDE.md |
| `AuthOkPrefix` | `[DOTSNet] Auth OK, user_id=` | `Tools/verify-multiclient.sh` counts distinct `user_id=` tokens on `Auth OK` lines |
| `InWorldPrefix` | `[DOTSNet] IN WORLD as ` | `Tools/verify-multiclient.sh` counts logs containing `IN WORLD`; TestLab `clients_in_world` |
| `PartyPrefix` | `[DOTSNet] party ready: ` | party / dungeon runs |
| `CharacterPrefix` | `[DOTSNet] character: ` | roster selection logs |
| `FatalPrefix` | `[DOTSNet] FATAL: ` | `Tools/verify-multiclient.sh` and TestLab `analyze.py` fail on `FATAL` |
| `CancelledLine` | `[DOTSNet] Cancelled` | headless run logs |

To change one: edit the constant, the contract test literal and every consumer above in one
commit. The netcode DOTS sample prints the same `Auth OK` / `IN WORLD` text.

## Unobserved exceptions

`UnobservedExceptionReporter` (root scope, `ClientRootInstaller`) subscribes
`UniTaskScheduler.UnobservedTaskException` and `TaskScheduler.UnobservedTaskException` and logs each
exception once as a single error line starting with `[Unobserved]`. Subscribing to the UniTask event
replaces UniTask's own default log line, so nothing is logged twice. Cancellations are ignored.

## Session driver dispose line

`MainSessionDriver.Dispose` logs `session driver #N disposed (phase P)`. Development builds and the
Editor (`Debug.isDebugBuild`) append ` at:` and the stack trace; release players log no stack trace.
