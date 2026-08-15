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
        /// Authenticate with email and password. Creates the account on first use.
        /// </summary>
        public async UniTask<ISession> AuthenticateEmailAsync(string email, string password, bool create = true,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var session = await _client.AuthenticateEmailAsync(email, password, create: create);
            ApplySession(session);
            return session;
        }

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
