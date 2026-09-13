namespace Scripts.Nakama.Tls
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using global::Nakama;
    using global::Nakama.TinyJson;
    using UnityEngine.Networking;

    /// <summary>
    /// The Nakama SDK's HTTP transport, with a <see cref="PinnedCertificateHandler"/> attached
    /// to every request.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why this type has to exist at all.</b> A <c>CertificateHandler</c> is per-request:
    /// it does nothing until something assigns it to a <c>UnityWebRequest</c>. The SDK's stock
    /// <c>UnityWebRequestAdapter</c> builds every request itself and never sets
    /// <c>certificateHandler</c>, and it exposes no hook to change that — so pinning this hop
    /// means supplying the adapter, not just the handler. That is the whole difference from
    /// the gateway hop, where <c>TlsOptions.PinnedCertificate</c> is a property on a transport
    /// the netcode package owns.
    /// </para>
    /// <para>
    /// Behaviour is otherwise the stock adapter's, deliberately: same verb handling, same
    /// <c>application/json</c> content type, same mapping of Unity's result codes onto
    /// <see cref="ApiResponseException"/>. The one deliberate difference besides the handler
    /// is that this one completes off an <c>AsyncOperation</c> callback instead of a
    /// coroutine, so it needs no <c>MonoBehaviour</c> and no hidden <c>[Nakama]</c>
    /// <c>GameObject</c> in the scene.
    /// </para>
    /// <para>
    /// <b>Install it only when there is a pin.</b> With no certificate to pin, pass the stock
    /// <c>UnityWebRequestAdapter.Instance</c> and let Unity's own validation decide — that is
    /// the stronger default for a CA-issued certificate, and it correctly refuses a
    /// self-signed one.
    /// </para>
    /// </remarks>
    public sealed class PinnedHttpAdapter : IHttpAdapter, IDisposable
    {
        private readonly PinnedCertificateHandler certificate;
        private bool disposed;

        /// <param name="pinnedDer">DER bytes of the certificate Nakama must present.</param>
        /// <exception cref="ArgumentException">The pin is empty; see the handler's remarks.</exception>
        public PinnedHttpAdapter(byte[] pinnedDer)
        {
            this.certificate = new PinnedCertificateHandler(pinnedDer);
        }

        /// <inheritdoc cref="IHttpAdapter.Logger"/>
        public ILogger Logger { get; set; }

        /// <inheritdoc/>
        public TransientExceptionDelegate TransientExceptionDelegate => IsTransientException;

        /// <inheritdoc/>
        public Task<string> SendAsync(string method, Uri uri, IDictionary<string, string> headers, byte[] body,
            int timeout, CancellationToken? cancellationToken)
        {
            var completion = new TaskCompletionSource<string>();
            UnityWebRequest www;

            try
            {
                www = this.BuildRequest(method, uri, headers, body, timeout);
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
                return completion.Task;
            }

            cancellationToken?.Register(() => completion.TrySetCanceled());

            var operation = www.SendWebRequest();
            operation.completed += _ =>
            {
                try
                {
                    Complete(www, completion);
                }
                finally
                {
                    // Disposing the request must NOT dispose the shared certificate handler;
                    // see BuildRequest.
                    www.Dispose();
                }
            };

            return completion.Task;
        }

        private UnityWebRequest BuildRequest(string method, Uri uri, IDictionary<string, string> headers,
            byte[] body, int timeout)
        {
            UnityWebRequest www;
            if (string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(method, "PUT", StringComparison.OrdinalIgnoreCase))
            {
                www = new UnityWebRequest(uri, method)
                {
                    uploadHandler = new UploadHandlerRaw(body),
                    downloadHandler = new DownloadHandlerBuffer(),
                };
            }
            else if (string.Equals(method, "DELETE", StringComparison.OrdinalIgnoreCase))
            {
                www = UnityWebRequest.Delete(uri);
            }
            else
            {
                www = UnityWebRequest.Get(uri);
            }

            www.SetRequestHeader("Content-Type", "application/json");
            if (headers != null)
            {
                foreach (var kv in headers)
                {
                    www.SetRequestHeader(kv.Key, kv.Value);
                }
            }

            www.timeout = timeout;
            www.certificateHandler = this.certificate;

            // WITHOUT THIS THE PIN WORKS EXACTLY ONCE. UnityWebRequest.Dispose() disposes the
            // attached certificate handler by default, and this adapter shares one handler
            // across every request — so the second request would carry a disposed handler.
            // The adapter owns the handler's lifetime instead; see Dispose.
            www.disposeCertificateHandlerOnDispose = false;

            return www;
        }

        private static void Complete(UnityWebRequest www, TaskCompletionSource<string> completion)
        {
            if (www.result == UnityWebRequest.Result.ConnectionError)
            {
                // A failed pin arrives here, not as an HTTP status: the handshake never
                // completed. The message is Unity's own ("Cert verify failed" and friends),
                // which is the honest thing to surface — this adapter cannot tell a rejected
                // certificate from an unreachable host any better than Unity can.
                completion.TrySetException(new ApiResponseException(www.error));
                return;
            }

            if (www.result == UnityWebRequest.Result.ProtocolError)
            {
                string text = www.downloadHandler?.text ?? string.Empty;

                if (www.responseCode >= 500)
                {
                    completion.TrySetException(new ApiResponseException(www.responseCode, text, -1));
                    return;
                }

                completion.TrySetException(Decode(www.responseCode, text));
                return;
            }

            completion.TrySetResult(www.downloadHandler?.text);
        }

        /// <summary>
        /// Turns Nakama's JSON error body into a typed exception, falling back to the raw text
        /// when it is not the shape we expect.
        /// </summary>
        private static ApiResponseException Decode(long statusCode, string text)
        {
            try
            {
                var decoded = text.FromJson<Dictionary<string, object>>();
                if (decoded == null)
                {
                    return new ApiResponseException(text);
                }

                string message = decoded.TryGetValue("message", out var m) && m != null
                    ? m.ToString()
                    : string.Empty;
                int grpcCode = decoded.TryGetValue("code", out var c) && c is int code ? code : -1;

                return new ApiResponseException(statusCode, message, grpcCode);
            }
            catch (Exception)
            {
                // A body that does not parse is still a real error; losing the status code to
                // a parser problem would be worse than losing the structure.
                return new ApiResponseException(statusCode, text, -1);
            }
        }

        private static bool IsTransientException(Exception e) =>
            e is ApiResponseException api && (api.StatusCode >= 500 || api.StatusCode == -1);

        public void Dispose()
        {
            if (this.disposed) return;
            this.disposed = true;
            this.certificate.Dispose();
        }
    }
}
