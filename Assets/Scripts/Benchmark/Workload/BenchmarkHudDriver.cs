namespace Scripts.Benchmark.Workload
{
    using Scripts.Benchmark;
    using Scripts.UI.Hud;
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>
    /// Exercises the HUD binding path with synthetic data: the real <see cref="HudView"/>
    /// over the committed UXML, bound to a <see cref="HudViewModel"/> whose properties are
    /// rewritten once per second. This is the docs/HUD-BRIDGE.md View half without the ECS
    /// half — the netcode-mirror aggregation is meaningless with no server, but the
    /// ViewModel → binding-system → visual-tree cost is exactly what the game will pay, so
    /// the benchmark carries it.
    /// </summary>
    /// <remarks>
    /// Updates deliberately allocate a couple of small caption strings per second — that IS
    /// the realistic cost of driving this HUD, and it is per second, not per frame, so it
    /// cannot masquerade as a frame-loop leak in the results. Frames between updates cost
    /// the binding system nothing (the ViewModel's notify-on-change guard).
    /// </remarks>
    [RequireComponent(typeof(UIDocument))]
    public sealed class BenchmarkHudDriver : MonoBehaviour
    {
        [Tooltip("HudView.uxml — the enrolled HUD layout.")]
        [SerializeField] private VisualTreeAsset hudAsset;

        [Tooltip("Supplies the synthetic player/entity counts shown on the HUD.")]
        [SerializeField] private BenchmarkRecorder recorder;

        private HudView view;
        private HudViewModel viewModel;
        private float nextUpdateTime;
        private int tick;

        private void Start()
        {
            if (this.hudAsset == null)
            {
                Debug.LogWarning("[BenchmarkHudDriver] no HUD VisualTreeAsset assigned — HUD disabled.");
                this.enabled = false;
                return;
            }

            var document = this.GetComponent<UIDocument>();
            this.view = new HudView(this.hudAsset);
            document.rootVisualElement.Add(this.view.Root);
            this.view.Show();

            this.viewModel = new HudViewModel();
            this.view.Bind(this.viewModel);
        }

        private void Update()
        {
            if (Time.realtimeSinceStartup < this.nextUpdateTime)
            {
                return;
            }

            this.nextUpdateTime = Time.realtimeSinceStartup + 1f;
            this.tick++;

            // Synthetic but shaped like the real feed: health ping-pongs so caption AND bar
            // change every push, position walks, counts follow the actual ramp.
            var hp = Mathf.PingPong(this.tick * 7f, 100f);
            this.viewModel.HealthCaption = $"{(int)hp}/100";
            this.viewModel.HealthFraction = hp / 100f;
            this.viewModel.PositionCaption = $"({this.tick % 100}.0, {(this.tick * 3) % 100}.0)";
            this.viewModel.PlayersVisible = this.tick;
            this.viewModel.EntitiesVisible = this.recorder != null ? this.recorder.CurrentPhaseEntityCount : 0;
        }

        private void OnDestroy()
        {
            this.view?.DestroySelf();
            this.view = null;
        }
    }
}
