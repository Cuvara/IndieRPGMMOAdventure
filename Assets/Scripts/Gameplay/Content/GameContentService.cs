namespace Scripts.Gameplay.Content
{
    using System;
    using System.Threading;
    using Cysharp.Threading.Tasks;
    using UnityEngine;
    using UnityEngine.Networking;

    /// <summary>
    /// Downloads the game server's composed content document (<c>GET {origin}/content</c>,
    /// ADR-19) once per session and holds it as a <see cref="GameContentCatalog"/> for the HUD,
    /// the inventory panel and the cast key.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Content is downloaded, never shipped</b> (<c>docs/CONTENT-PIPELINE.md</c>): a number
    /// baked into the build would be a guess shown to a player as fact. So there is no bundled
    /// fallback; with no content the HUD shows "—" for stats it cannot name and the cast key does
    /// nothing, and the log says why.
    /// </para>
    /// <para>
    /// <b>Not fatal to the session.</b> The join does not wait for this and does not fail on it:
    /// movement and the world work without content, and a dev stack whose metrics port is not
    /// reachable from the client must not lose its whole session to a HUD.
    /// </para>
    /// <para>
    /// Registered in the root scope, so one download serves every scene of the session. Main
    /// thread only (UnityWebRequest).
    /// </para>
    /// </remarks>
    public sealed class GameContentService
    {
        /// <summary>Seconds before a content request is abandoned.</summary>
        public const int TimeoutSeconds = 10;

        private UniTask<GameContentCatalog>? inFlight;
        private string inFlightOrigin;

        /// <summary>The content in use; <see cref="GameContentCatalog.Empty"/> until a fetch succeeds.</summary>
        public GameContentCatalog Catalog { get; private set; } = GameContentCatalog.Empty;

        /// <summary>True once a fetch produced a catalog.</summary>
        public bool IsLoaded => !ReferenceEquals(this.Catalog, GameContentCatalog.Empty);

        /// <summary>Raised on the main thread when <see cref="Catalog"/> changes.</summary>
        public event Action<GameContentCatalog> Loaded;

        /// <summary>
        /// The content origin: <paramref name="explicitUrl"/> when given (a trailing
        /// <c>/content</c> is tolerated), else the scheme, host and port of
        /// <paramref name="statusUrl"/> — <c>/status</c> and <c>/content</c> are served side by
        /// side on the game server's metrics port. Null when neither yields an absolute URL.
        /// </summary>
        public static string ResolveOrigin(string explicitUrl, string statusUrl)
        {
            if (!string.IsNullOrWhiteSpace(explicitUrl))
            {
                var trimmed = explicitUrl.Trim().TrimEnd('/');
                if (trimmed.EndsWith("/content", StringComparison.OrdinalIgnoreCase))
                {
                    trimmed = trimmed.Substring(0, trimmed.Length - "/content".Length);
                }

                return Uri.TryCreate(trimmed, UriKind.Absolute, out _) ? trimmed : null;
            }

            if (!string.IsNullOrWhiteSpace(statusUrl) &&
                Uri.TryCreate(statusUrl.Trim(), UriKind.Absolute, out var status) &&
                (status.Scheme == Uri.UriSchemeHttp || status.Scheme == Uri.UriSchemeHttps))
            {
                return status.GetLeftPart(UriPartial.Authority);
            }

            return null;
        }

        /// <summary>
        /// Fetches content from <paramref name="origin"/> unless it is already loaded. Never
        /// throws for a network or format failure: logs it and returns false.
        /// </summary>
        public async UniTask<bool> EnsureLoadedAsync(string origin, CancellationToken cancellationToken)
        {
            if (this.IsLoaded)
            {
                return true;
            }

            if (string.IsNullOrEmpty(origin))
            {
                Debug.LogWarning(
                    "[Content] no content origin (pass -cuvara-content-url or -cuvara-status-url); stats, statuses, " +
                    "item names and abilities stay unresolved.");
                return false;
            }

            if (this.inFlight == null || this.inFlightOrigin != origin)
            {
                this.inFlightOrigin = origin;
                this.inFlight = FetchAsync(origin, cancellationToken).Preserve();
            }

            GameContentCatalog catalog;
            try
            {
                catalog = await this.inFlight.Value;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                this.inFlight = null;
                throw;
            }
            catch (Exception exception)
            {
                this.inFlight = null;
                Debug.LogWarning($"[Content] {exception.Message}");
                return false;
            }

            if (!this.IsLoaded)
            {
                this.Catalog = catalog;
                Debug.Log(
                    $"[Content] loaded from {origin}/content hash={catalog.Hash}: {catalog.Items.ItemCount} items, " +
                    $"{catalog.StatCount} stats, {catalog.StatusCount} statuses, {catalog.Abilities.Count} abilities");
                this.Loaded?.Invoke(catalog);
            }

            return true;
        }

        /// <summary>Adopts a catalog obtained elsewhere (tests, a local document).</summary>
        public void Adopt(GameContentCatalog catalog)
        {
            this.Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            this.Loaded?.Invoke(catalog);
        }

        private static async UniTask<GameContentCatalog> FetchAsync(string origin, CancellationToken cancellationToken)
        {
            var url = origin + "/content";
            using var request = UnityWebRequest.Get(url);
            request.timeout = TimeoutSeconds;
            try
            {
                await request.SendWebRequest().WithCancellation(cancellationToken);
            }
            catch (UnityWebRequestException ex)
            {
                throw new InvalidOperationException($"could not fetch {url}: {ex.Message}", ex);
            }

            var hash = request.GetResponseHeader("X-Content-Hash");
            if (string.IsNullOrEmpty(hash))
            {
                hash = (request.GetResponseHeader("ETag") ?? string.Empty).Trim('"');
            }

            if (!GameContentCatalog.TryParse(request.downloadHandler.text, hash, out var catalog, out var error))
            {
                throw new InvalidOperationException($"content from {url} is unusable: {error}");
            }

            return catalog;
        }
    }
}
