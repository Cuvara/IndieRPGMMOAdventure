namespace Scripts.DI
{
    using System;
    using System.Threading;
    using Cuvara.Netcode.Client;
    using Cysharp.Threading.Tasks;
    using Scripts.Gameplay.Content;
    using Scripts.Gameplay.Inventory;
    using Scripts.Gameplay.Presentation;
    using Scripts.Gameplay.Stats;
    using Scripts.UI.Gameplay;
    using UnityEngine;
    using UnityEngine.UIElements;
    using VContainer.Unity;

    /// <summary>
    /// The protocol 3 gameplay surface of MainScene (Core v3): downloads the content, owns the
    /// inventory service on the command channel, and hosts the gameplay panel (stat block,
    /// statuses, inventory) — an entry point of <c>MainSceneScope</c>, like
    /// <see cref="MainSessionDriver"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Hosted at runtime, not in the scene.</b> MainScene carries no <see cref="UIDocument"/>,
    /// and scenes are Editor-owned assets; so this driver creates one GameObject with a
    /// <see cref="UIDocument"/> using <c>Resources/GameplayPanelSettings</c> (authored by
    /// <c>GameplayUiAuthoring</c>) and the enrolled <c>Resources/GameplayPanelView.uxml</c>.
    /// Either missing makes the panel headless: the service still runs and inventory changes
    /// are logged, so a batch player still exercises the commands.
    /// </para>
    /// <para>
    /// <b>Per session.</b> When the netcode client reaches <c>InWorld</c> on a new session (a
    /// join, a reconnect, a transfer) the inventory is requested once (opcode 1); afterwards the
    /// server pushes changes (push 100). Content is fetched once per process from
    /// <c>-cuvara-content-url</c>, else the origin of <c>-cuvara-status-url</c>.
    /// </para>
    /// <para>
    /// Keys (edge-triggered, <see cref="GameplayInputReader"/>): <b>I</b> shows/hides the
    /// inventory, <b>E</b> picks up the nearest item entity within
    /// <see cref="PickupSearchRange"/> (the server judges the real reach).
    /// </para>
    /// </remarks>
    public sealed class GameplayPanelDriver : IStartable, ITickable, IDisposable
    {
        /// <summary>Ground-plane search radius for the pickup key, world units.</summary>
        public const float PickupSearchRange = 6f;

        /// <summary>Seconds between stat-block reads (the stat block replicates at snapshot rate anyway).</summary>
        public const float StatsInterval = 0.1f;

        public const string PanelSettingsResource = "GameplayPanelSettings";
        public const string PanelLayoutResource = "GameplayPanelView";

        private readonly NetworkClient client;
        private readonly GameContentService content;
        private readonly BackendSettings backend;
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();

        private NetworkClientCommandChannel channel;
        private InventoryService inventory;
        private GameplayPanelPresenter presenter;
        private GameplayPanelView view;
        private GameObject host;
        private GameSessionClient refreshedFor;
        private bool contentRequested;
        private float nextStatsAt;
        private PlayerStatsSnapshot lastStats = PlayerStatsSnapshot.Absent;

        public GameplayPanelDriver(NetworkClient client, GameContentService content, BackendSettings backend)
        {
            this.client = client ?? throw new ArgumentNullException(nameof(client));
            this.content = content ?? throw new ArgumentNullException(nameof(content));
            this.backend = backend ?? throw new ArgumentNullException(nameof(backend));
        }

        /// <summary>The inventory service; null before <see cref="Start"/>.</summary>
        public IInventoryService Inventory => this.inventory;

        public void Start()
        {
            this.channel = new NetworkClientCommandChannel(this.client);
            this.inventory = new InventoryService(this.channel);
            this.content.Loaded += this.OnContentLoaded;

            if (this.TryCreatePanel())
            {
                this.presenter = new GameplayPanelPresenter(
                    this.view, this.inventory, () => this.content.Catalog.Items, this.FindNearestItem);
            }
            else
            {
                this.inventory.Changed += this.LogInventory;
            }
        }

        public void Tick()
        {
            if (this.inventory == null) return;

            if (this.client.State == NetworkClientState.InWorld)
            {
                this.OnInWorld();
            }

            this.inventory.Pump();

            if (this.presenter != null)
            {
                if (GameplayInputReader.InventoryTogglePressed()) this.presenter.ToggleInventory();
                if (GameplayInputReader.PickupPressed()) this.presenter.Pickup();
                this.PushStats();
            }
            else if (GameplayInputReader.PickupPressed())
            {
                var target = this.FindNearestItem();
                if (target != null) this.inventory.PickupAsync(target, this.lifetime.Token).Forget();
            }
        }

        public void Dispose()
        {
            this.lifetime.Cancel();
            this.lifetime.Dispose();
            this.content.Loaded -= this.OnContentLoaded;

            this.presenter?.Dispose();
            this.presenter = null;
            if (this.inventory != null) this.inventory.Changed -= this.LogInventory;
            this.inventory?.Dispose();
            this.channel?.Dispose();

            this.view?.DestroySelf();
            this.view = null;
            if (this.host != null) UnityEngine.Object.Destroy(this.host);
            this.host = null;
        }

        private void OnInWorld()
        {
            if (!this.contentRequested)
            {
                this.contentRequested = true;
                var origin = GameContentService.ResolveOrigin(this.backend.Value.ContentUrl, this.backend.Value.StatusUrl);
                this.content.EnsureLoadedAsync(origin, this.lifetime.Token).Forget();
            }

            var session = this.client.Session;
            if (session != null && !ReferenceEquals(session, this.refreshedFor))
            {
                this.refreshedFor = session;
                if (session.SupportsCommands)
                {
                    this.inventory.RefreshAsync(this.lifetime.Token).Forget();
                }
                else
                {
                    Debug.Log($"[Gameplay] server protocol {this.client.ServerProtocolVersion}: no command channel, inventory unavailable.");
                }
            }
        }

        private void PushStats()
        {
            if (Time.unscaledTime < this.nextStatsAt) return;
            this.nextStatsAt = Time.unscaledTime + StatsInterval;

            var stats = this.client.State == NetworkClientState.InWorld &&
                        this.client.World.TryGet(this.client.UserId, out var self)
                ? PlayerStatsSnapshot.From(self, this.content.Catalog, this.client.World.Tick, this.client.TickRate)
                : PlayerStatsSnapshot.Absent;

            if (!stats.Equals(this.lastStats))
            {
                this.lastStats = stats;
                this.presenter.PushStats(stats);
            }
        }

        private string FindNearestItem()
        {
            if (!this.client.World.TryGet(this.client.UserId, out var self)) return null;
            return NearestEntity.Find(this.client.World.EntityMap, NearestEntity.ItemType, self.X, self.Y, PickupSearchRange);
        }

        private void OnContentLoaded(GameContentCatalog catalog)
        {
            this.presenter?.ContentChanged();
            this.lastStats = PlayerStatsSnapshot.Absent; // re-resolve stat keys with the new content
        }

        private void LogInventory()
        {
            var rows = InventoryRows.Build(this.inventory.Current, this.content.Catalog.Items);
            Debug.Log(
                $"[Gameplay] inventory: {rows.Count} stack(s)" +
                (string.IsNullOrEmpty(this.inventory.LastError) ? string.Empty : $", last error {this.inventory.LastError}") +
                (rows.Count > 0 ? ": " + string.Join(", ", rows) : string.Empty));
        }

        private bool TryCreatePanel()
        {
            var layout = Resources.Load<VisualTreeAsset>(PanelLayoutResource);
            var settings = Resources.Load<PanelSettings>(PanelSettingsResource);
            if (layout == null || settings == null)
            {
                Debug.LogWarning(
                    $"[Gameplay] gameplay panel not shown (layout {(layout != null ? "ok" : "missing")}, " +
                    $"panel settings {(settings != null ? "ok" : "missing")} in Resources); running headless, " +
                    "inventory changes are logged.");
                return false;
            }

            // Inactive while wiring, so the document attaches to its panel exactly once.
            this.host = new GameObject("GameplayPanel");
            this.host.SetActive(false);
            var document = this.host.AddComponent<UIDocument>();
            document.panelSettings = settings;
            this.host.SetActive(true);

            this.view = new GameplayPanelView(layout);
            document.rootVisualElement.Add(this.view.Root);
            this.view.Show();
            return true;
        }
    }
}
