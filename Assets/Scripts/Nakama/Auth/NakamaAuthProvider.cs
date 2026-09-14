using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Cuvara.Netcode.Auth;
using Cuvara.Netcode.Json;
using Nakama;

namespace Scripts.Nakama.Auth
{
    /// <summary>
    /// Provides the gateway JWT for a Nakama-authenticated player. Establishes a
    /// Nakama session on first call (restoring a persisted one where possible), then
    /// exchanges it for a gateway token through the <c>gateway_token</c> RPC.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The Nakama <b>session</b> token and the <b>gateway</b> token are two different
    /// credentials and are not interchangeable — see
    /// <see cref="NakamaSessionService"/>. Returning the session token here is a
    /// mistake that does not look like one: the gateway may well accept it, because
    /// the deploy can share one HS256 secret, but it carries the user in a claim the
    /// gateway does not read, so the session authenticates with an <b>empty
    /// user_id</b> and the player is nobody. The RPC is the only path that yields a
    /// token the gateway can resolve an identity from.
    /// </para>
    /// <para>
    /// No signing secret is held client-side on this path. The gateway's
    /// <c>JWT_SECRET</c> stays server-side; Nakama mints the token.
    /// </para>
    /// </remarks>
    public sealed class NakamaAuthProvider : IAuthProvider
    {
        /// <summary>Server-side RPC that mints a gateway-signed JWT for the caller.</summary>
        const string GatewayTokenRpc = "gateway_token";

        readonly NakamaSessionService _nakama;

        public NakamaAuthProvider(NakamaSessionService nakama)
        {
            _nakama = nakama;
        }

        public async UniTask<string> GetJwtAsync(CancellationToken ct)
        {
            // Try restoring a persisted session first — unless this process was given its own
            // device id: PlayerPrefs is shared by every instance on the machine, and restoring
            // would hand this client the account of whichever instance logged in last.
            if (!_nakama.IsSessionValid && !_nakama.HasExplicitDeviceId)
            {
                await _nakama.RestoreSessionAsync(ct);
            }

            // Still invalid — authenticate fresh with device ID.
            if (!_nakama.IsSessionValid)
            {
                await _nakama.AuthenticateDeviceAsync(ct: ct);
            }

            var session = _nakama.Session;
            if (session == null)
            {
                throw new InvalidOperationException(
                    "Nakama authentication completed but no session was produced. " +
                    "Check the Nakama server is reachable at the configured address.");
            }

            // The token is minted FOR this session. If the session changes underneath
            // the RPC — sign-out, a newer login — the token names the wrong account
            // and must not be handed to the gateway.
            var generation = _nakama.LoginGeneration;
            var token = await FetchGatewayTokenAsync(session, ct);
            ct.ThrowIfCancellationRequested();
            if (generation != _nakama.LoginGeneration || !ReferenceEquals(session, _nakama.Session))
            {
                throw new OperationCanceledException(
                    "the Nakama session changed while the gateway token was being minted; the token is discarded");
            }

            return token;
        }

        /// <summary>
        /// Exchanges the Nakama session for a gateway-signed JWT.
        /// </summary>
        /// <remarks>
        /// The payload is parsed <b>once</b>. Nakama's HTTP API returns the RPC result
        /// as a JSON-encoded string nested in an envelope — over raw HTTP that needs
        /// unwrapping twice — but the Unity SDK has already unwrapped the envelope, so
        /// <c>IApiRpc.Payload</c> is the inner JSON object. Parsing twice here fails.
        /// </remarks>
        async UniTask<string> FetchGatewayTokenAsync(ISession session, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            string payload;
            try
            {
                var rpc = await _nakama.Client.RpcAsync(session, GatewayTokenRpc, "{}", canceller: ct);
                payload = rpc?.Payload;
            }
            catch (OperationCanceledException)
            {
                // A cancel is a cancel, not a "Nakama RPC failed".
                throw;
            }
            catch (Exception ex)
            {
                // Loud on purpose: the failure mode this replaces was silent.
                throw new InvalidOperationException(
                    $"Nakama RPC '{GatewayTokenRpc}' failed, so no gateway token could be " +
                    "obtained. The gateway cannot authenticate this player without it. " +
                    $"Verify the RPC is registered on the Nakama server. Cause: {ex.Message}", ex);
            }

            if (string.IsNullOrEmpty(payload))
            {
                throw new InvalidOperationException(
                    $"Nakama RPC '{GatewayTokenRpc}' returned an empty payload. Expected a JSON " +
                    "object carrying a 'token' field.");
            }

            string token;
            try
            {
                token = JsonParser.Parse(payload).GetString("token");
            }
            catch (JsonParseException ex)
            {
                throw new InvalidOperationException(
                    $"Nakama RPC '{GatewayTokenRpc}' returned a payload that is not valid JSON: " +
                    $"{payload}", ex);
            }

            if (string.IsNullOrEmpty(token))
            {
                throw new InvalidOperationException(
                    $"Nakama RPC '{GatewayTokenRpc}' returned no 'token' field, so there is no " +
                    $"gateway credential to present. Payload: {payload}");
            }

            return token;
        }
    }
}
