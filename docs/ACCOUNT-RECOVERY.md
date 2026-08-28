# Account recovery

## The problem this exists to solve

A player who has only ever been authenticated by device id owns a character that exists
nowhere else. `SystemInfo.deviceUniqueIdentifier` is the sole credential pointing at that
Nakama account. Reinstall the app, wipe the phone, or buy a new one, and the character is
unreachable — not deleted, but with no way for the player or for support to prove it is
theirs.

That was the client's state until this document existed: `NakamaAuthProvider` restores a
session or falls back to `AuthenticateDeviceAsync`, and nothing ever attached a second
credential. Every account was one lost phone away from gone.

## The shape of the fix

Recovery is two operations separated in time, and the order is not negotiable:

1. **Link, on the device the player already has.** `LinkEmailAsync` attaches an email and
   password to the *current* account. This is additive — the device id keeps working, the
   session does not change, and the player is not signed out.
2. **Recover, on the new device.** `RecoverWithEmailAsync` signs in to that same account.

An account that was never linked cannot be recovered by any means. Linking has to happen
*before* the loss, which makes it a product problem as much as a technical one: a player
who is never asked to add a recovery email does not have account recovery, however complete
this API is.

## API

All on `NakamaSessionService` (`Assets/Scripts/Nakama/NakamaSessionService.cs`).

| Call | Purpose |
|------|---------|
| `LinkEmailAsync(email, password, ct)` | Attach a recovery credential to the current account |
| `RecoverWithEmailAsync(email, password, ct)` | Sign in to a linked account from anywhere |
| `GetRecoveryEmailAsync(ct)` | The linked address, or `null` if the account is device-only |
| `IsRecoverableAsync(ct)` | Convenience boolean over the above |
| `UnlinkEmailAsync(email, password, ct)` | Detach it. Destructive — see below |
| `AuthenticateEmailAsync(email, password, create, ct)` | Low level. Prefer the two above |

`LinkEmailAsync`, `UnlinkEmailAsync` and `GetRecoveryEmailAsync` all act on the account the
current session identifies, so they throw `InvalidOperationException` when there is no valid
session. Authenticate first.

## The trap in `create: true`

`AuthenticateEmailAsync` takes `create` with **no default value**, deliberately. It used to
default to `true`, which is the dangerous value for the recovery case:

> With `create: true`, an email Nakama does not recognise produces a brand-new empty account
> and a completely successful-looking login.

A player who mistypes their address while recovering lands in a fresh character with no
items and no progress, and *nothing reports an error*. The client authenticated. The gateway
resolved an identity. The session is valid. It is simply the wrong account — and by the time
anyone notices, the player believes their character was deleted.

`RecoverWithEmailAsync` exists so that the recovery path cannot get this wrong: it pins
`create` to `false`, turning an unknown email into a loud `ApiResponseException` instead of a
silent new account. Use it. `AuthenticateEmailAsync` is for a deliberate sign-up flow, where
`create: true` is what you actually mean.

## Failure modes worth handling in UI

| Situation | What happens | What the player should see |
|---|---|---|
| Unknown email during recovery | `ApiResponseException` | "No account found for that email" — never silently create one |
| Wrong password | `ApiResponseException` | Standard wrong-credentials message |
| Email already on another account, during link | `ApiResponseException` | "That email is already in use." Nakama refuses to merge, which is correct — merging two characters is not a decision a client can make |
| Link attempted with no session | `InvalidOperationException` | A bug, not a player error. Authenticate first |
| Empty email or password | `ArgumentException` | Validate in the form before calling |

## What is still missing

**There is no UI for any of this**, and that is the remaining gap. The client has no login
screen, no settings screen, and no way for a player to type an email address —
`Assets/Scripts/` holds DI wiring, Nakama auth and an Addressables loader, nothing else.

So the honest status is: the mechanism exists and is reachable from code; account recovery is
not yet a feature a player can use. Two screens close that, both buildable on
`com.cuvara.uitoolkit`'s view lifecycle:

- **Add recovery email** — in a settings or profile screen, shown when `IsRecoverableAsync`
  returns false. This is the one that matters; everything else is worthless without it.
- **Recover account** — reachable before authentication, calling `RecoverWithEmailAsync`.

Prompting for the link at a natural milestone rather than at first launch is the usual
pattern — a player who has invested nothing will decline, and a player who has invested
something has a reason to say yes.
