using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Cuvara.Netcode.Auth;

namespace Scripts.Nakama.Auth
{
    /// <summary>
    /// Provides the gateway JWT from a Nakama session. Authenticates via device
    /// auth on first call, then returns the cached token. Refreshes automatically
    /// if the session has expired.
    /// </summary>
    public sealed class NakamaAuthProvider : IAuthProvider
    {
        readonly NakamaSessionService _nakama;

        public NakamaAuthProvider(NakamaSessionService nakama)
        {
            _nakama = nakama;
        }

        public async UniTask<string> GetJwtAsync(CancellationToken ct)
        {
            // Try restoring a persisted session first.
            if (!_nakama.IsSessionValid)
            {
                await _nakama.RestoreSessionAsync(ct);
            }

            // Still invalid — authenticate fresh with device ID.
            if (!_nakama.IsSessionValid)
            {
                await _nakama.AuthenticateDeviceAsync(ct: ct);
            }

            if (_nakama.Session == null)
            {
                throw new InvalidOperationException(
                    "Nakama authentication completed but no session was produced. " +
                    "Check the Nakama server is reachable at the configured address.");
            }

            return _nakama.Session.AuthToken;
        }
    }
}
