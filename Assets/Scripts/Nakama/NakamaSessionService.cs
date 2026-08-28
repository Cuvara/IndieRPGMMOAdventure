using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Nakama;
using UnityEngine;

namespace Scripts.Nakama
{
    /// <summary>
    /// Manages the Nakama SDK client and authenticated session.
    /// Provides device auth (primary) and email auth (secondary),
    /// persists session tokens in PlayerPrefs, and refreshes expired tokens.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Account recovery.</b> Device authentication alone binds a character to the
    /// installing device and nothing else: reinstall, wipe or replace the phone and the
    /// character is gone, with no credential left pointing at it and no support path to
    /// restore one. The three calls that fix that are
    /// <see cref="LinkEmailAsync"/> (attach a recovery credential on the original device),
    /// <see cref="RecoverWithEmailAsync"/> (sign in to that account elsewhere), and
    /// <see cref="GetRecoveryEmailAsync"/> (does this account have one yet?).
    /// </para>
    /// <para>
    /// Linking is what makes recovery possible, and it can only happen <i>before</i> the
    /// device is lost. An unlinked account is not recoverable by any means — so a client
    /// that never surfaces <see cref="LinkEmailAsync"/> to the player has, in practice, no
    /// account recovery regardless of what this class offers.
    /// </para>
    /// <para>
    /// Registered as a singleton in VContainer via
    /// <see cref="DI.NakamaRegistration.RegisterNakama"/>.
    /// </para>
    /// <para>
    /// <b>Two different tokens, do not confuse them.</b>
    /// <see cref="Session"/>'s <c>AuthToken</c> is the <b>Nakama session</b> token: it
    /// authenticates calls to Nakama itself (RPCs, storage, social) and nothing else.
    /// The <b>gateway</b> token is a separate credential minted by the
    /// <c>gateway_token</c> RPC; it carries the user id in the <c>sub</c> claim the
    /// gateway reads, and it is the only one the gateway can resolve an identity from.
    /// <see cref="Auth.NakamaAuthProvider"/> performs that exchange.
    /// </para>
    /// <para>
    /// Presenting the session token to the gateway is not a clean failure. The deploy
    /// may share one HS256 secret, in which case the signature verifies and the
    /// gateway accepts the connection — but the user claim it looks for is absent, so
    /// the session is established with an <b>empty user_id</b>. Never substitute one
    /// for the other.
    /// </para>
    /// </remarks>
    public sealed class NakamaSessionService : IDisposable
    {
        const string PrefKeyAuthToken = "nakama.auth_token";
        const string PrefKeyRefreshToken = "nakama.refresh_token";

        readonly IClient _client;

        /// <summary>The underlying Nakama SDK client.</summary>
        public IClient Client => _client;

        /// <summary>
        /// The current authenticated session, or null if not authenticated.
        /// Check <see cref="IsSessionValid"/> before using.
        /// </summary>
        public ISession Session { get; private set; }

        /// <summary>
        /// True when <see cref="Session"/> exists and has not expired.
        /// Does not account for clock skew — the server is authoritative.
        /// </summary>
        public bool IsSessionValid => Session != null && !Session.IsExpired;

        public NakamaSessionService(NakamaSettings settings)
        {
            _client = new Client(settings.Scheme, settings.Host, settings.Port, settings.ServerKey);
        }

        /// <summary>
        /// Authenticate with a device ID. Creates the account on first use.
        /// Falls back to <see cref="SystemInfo.deviceUniqueIdentifier"/>.
        /// </summary>
        public async UniTask<ISession> AuthenticateDeviceAsync(string deviceId = null, CancellationToken ct = default)
        {
            deviceId ??= SystemInfo.deviceUniqueIdentifier;
            ct.ThrowIfCancellationRequested();
            var session = await _client.AuthenticateDeviceAsync(deviceId, create: true);
            ApplySession(session);
            return session;
        }

        /// <summary>
        /// Authenticate with email and password.
        /// </summary>
        /// <param name="create">
        /// Whether to create an account when the email is unknown. <b>Deliberately has no
        /// default</b> — see the remarks; picking wrong is silent, not loud.
        /// </param>
        /// <remarks>
        /// <para>
        /// <b>Do not call this to recover an account.</b> Use
        /// <see cref="RecoverWithEmailAsync"/>, which pins <c>create</c> to <c>false</c>.
        /// </para>
        /// <para>
        /// With <c>create: true</c>, an email Nakama does not recognise produces a brand-new
        /// empty account and a perfectly successful-looking login. A player who mistypes their
        /// address during recovery lands in a fresh character with no items and no progress,
        /// and nothing anywhere reports an error — the client authenticated, the gateway
        /// resolved an identity, the session is valid. It is simply the wrong account. That is
        /// why <c>create</c> lost its default: the safe value depends entirely on whether the
        /// caller means "sign up" or "sign in", and there is no answer that is right for both.
        /// </para>
        /// </remarks>
        public async UniTask<ISession> AuthenticateEmailAsync(string email, string password, bool create,
            CancellationToken ct = default)
        {
            RequireEmailAndPassword(email, password);
            ct.ThrowIfCancellationRequested();
            var session = await _client.AuthenticateEmailAsync(email, password, create: create);
            ApplySession(session);
            return session;
        }

        /// <summary>
        /// Sign in to an existing account with its recovery email. Never creates an account:
        /// an unknown email fails loudly instead of silently producing an empty one.
        /// </summary>
        /// <remarks>
        /// This is the second half of account recovery. The first half is
        /// <see cref="LinkEmailAsync"/>, which must have been called on the original device —
        /// an account that was never linked has no recovery credential and cannot be reached
        /// from a new device at all.
        /// </remarks>
        /// <exception cref="ApiResponseException">
        /// The email is unknown or the password is wrong. Both are genuine failures here,
        /// which is the entire point of pinning <c>create</c> to <c>false</c>.
        /// </exception>
        public UniTask<ISession> RecoverWithEmailAsync(string email, string password, CancellationToken ct = default)
            => AuthenticateEmailAsync(email, password, create: false, ct);

        /// <summary>
        /// Attach an email and password to the <b>current</b> account so it can be recovered
        /// on another device. Does not change the current session or user id.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Until this is called, an account exists only as a device id. Reinstalling the app,
        /// wiping the device, or moving to a new phone loses the character permanently —
        /// there is no other credential pointing at it and no support path to restore one.
        /// </para>
        /// <para>
        /// Linking is additive: the device id keeps working afterwards, so a player who links
        /// an email is not signed out and does not have to log in again on this device.
        /// </para>
        /// </remarks>
        /// <exception cref="InvalidOperationException">No valid session to link to.</exception>
        /// <exception cref="ApiResponseException">
        /// The email already belongs to a different account. Nakama refuses rather than
        /// merging, which is correct — merging two characters is not something a client can
        /// decide.
        /// </exception>
        public async UniTask LinkEmailAsync(string email, string password, CancellationToken ct = default)
        {
            RequireEmailAndPassword(email, password);
            RequireSession(nameof(LinkEmailAsync));
            ct.ThrowIfCancellationRequested();
            await _client.LinkEmailAsync(Session, email, password);
            Debug.Log($"[Nakama] Recovery email linked to {Session.UserId}. Account is now recoverable.");
        }

        /// <summary>
        /// Detach a previously linked recovery email from the current account.
        /// </summary>
        /// <remarks>
        /// After this the account is device-only again, and therefore unrecoverable. Callers
        /// should treat it as destructive and confirm with the player first.
        /// </remarks>
        public async UniTask UnlinkEmailAsync(string email, string password, CancellationToken ct = default)
        {
            RequireEmailAndPassword(email, password);
            RequireSession(nameof(UnlinkEmailAsync));
            ct.ThrowIfCancellationRequested();
            await _client.UnlinkEmailAsync(Session, email, password);
            Debug.LogWarning($"[Nakama] Recovery email unlinked from {Session.UserId}. " +
                             "This account can no longer be recovered on another device.");
        }

        /// <summary>
        /// The recovery email attached to the current account, or <c>null</c> if it has none
        /// and is therefore device-only.
        /// </summary>
        /// <remarks>
        /// A round trip to Nakama; the answer is not cached in the session token. Intended for
        /// a settings screen deciding whether to show "Add recovery email" or the linked
        /// address, not for a per-frame check.
        /// </remarks>
        public async UniTask<string> GetRecoveryEmailAsync(CancellationToken ct = default)
        {
            RequireSession(nameof(GetRecoveryEmailAsync));
            ct.ThrowIfCancellationRequested();
            var account = await _client.GetAccountAsync(Session);
            var email = account?.Email;
            return string.IsNullOrEmpty(email) ? null : email;
        }

        /// <summary>True when the current account has a recovery credential attached.</summary>
        public async UniTask<bool> IsRecoverableAsync(CancellationToken ct = default)
            => await GetRecoveryEmailAsync(ct) != null;

        /// <summary>
        /// Restore a session from locally persisted tokens, refreshing if
        /// the auth token has expired but the refresh token is still valid.
        /// Returns true if a valid session was restored.
        /// </summary>
        public async UniTask<bool> RestoreSessionAsync(CancellationToken ct = default)
        {
            var authToken = PlayerPrefs.GetString(PrefKeyAuthToken, null);
            var refreshToken = PlayerPrefs.GetString(PrefKeyRefreshToken, null);

            if (string.IsNullOrEmpty(authToken) || string.IsNullOrEmpty(refreshToken))
                return false;

            var session = global::Nakama.Session.Restore(authToken, refreshToken);

            if (session.IsExpired)
            {
                // Auth token expired — try refreshing with the refresh token.
                if (session.IsRefreshExpired)
                    return false;

                try
                {
                    ct.ThrowIfCancellationRequested();
                    session = await _client.SessionRefreshAsync(session);
                }
                catch (ApiResponseException)
                {
                    ClearPersistedSession();
                    return false;
                }
            }

            ApplySession(session);
            return true;
        }

        /// <summary>Sign out: clear the in-memory session and persisted tokens.</summary>
        public void SignOut()
        {
            Session = null;
            ClearPersistedSession();
        }

        public void Dispose()
        {
            // IClient is not IDisposable in the Nakama SDK, nothing to tear down.
        }

        /// <summary>
        /// Guards the credential arguments before they reach the SDK.
        /// </summary>
        /// <remarks>
        /// Nakama answers an empty email with a 400 carrying a generic message, which surfaces
        /// to a player as "something went wrong" and to a developer as a server problem. The
        /// argument was wrong locally and can say so locally.
        /// </remarks>
        static void RequireEmailAndPassword(string email, string password)
        {
            if (string.IsNullOrWhiteSpace(email))
                throw new ArgumentException("Email is required.", nameof(email));
            if (string.IsNullOrEmpty(password))
                throw new ArgumentException("Password is required.", nameof(password));
        }

        void RequireSession(string operation)
        {
            if (!IsSessionValid)
            {
                throw new InvalidOperationException(
                    $"{operation} needs a valid Nakama session, and there is none. Authenticate " +
                    "first — linking or reading a recovery credential operates on the account " +
                    "the current session identifies, so there is no account to act on yet.");
            }
        }

        void ApplySession(ISession session)
        {
            Session = session;
            PlayerPrefs.SetString(PrefKeyAuthToken, session.AuthToken);
            PlayerPrefs.SetString(PrefKeyRefreshToken, session.RefreshToken);
            PlayerPrefs.Save();
            Debug.Log($"[Nakama] Authenticated as {session.UserId} (username={session.Username})");
        }

        void ClearPersistedSession()
        {
            PlayerPrefs.DeleteKey(PrefKeyAuthToken);
            PlayerPrefs.DeleteKey(PrefKeyRefreshToken);
            PlayerPrefs.Save();
        }
    }
}
