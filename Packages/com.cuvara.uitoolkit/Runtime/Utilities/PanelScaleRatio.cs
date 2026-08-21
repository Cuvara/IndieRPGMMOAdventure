namespace Cuvara.UIToolkit.Utilities
{
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>
    /// The UI Toolkit form of <c>Scripts/UIModule/Utilities/ScaleScreenRatio.cs</c>: picks
    /// whether a panel matches screen WIDTH or screen HEIGHT for an aspect ratio.
    /// </summary>
    /// <remarks>
    /// <para><b>The prior analysis is confirmed here.</b> There is no UI Toolkit component
    /// to attach, because there is no <c>CanvasScaler</c> to attach it to: scaling is a
    /// property of the <see cref="PanelSettings"/> ASSET, not of anything in the scene.
    /// <see cref="PanelSettings.scaleMode"/>, <see cref="PanelSettings.referenceResolution"/>,
    /// <see cref="PanelSettings.screenMatchMode"/> and <see cref="PanelSettings.match"/> are
    /// all present in 6000.3.9f1 and map one-for-one onto the <c>CanvasScaler</c> fields the
    /// uGUI component drives.</para>
    ///
    /// <para><b>The one real difference, and it matters.</b> A <c>CanvasScaler</c> is a
    /// component: writing <c>matchWidthOrHeight</c> at runtime touches a scene object and is
    /// forgotten on the next play. A <c>PanelSettings</c> is a shared PROJECT ASSET: writing
    /// <c>match</c> on the one the <see cref="UIDocument"/> points at edits the asset on disk
    /// in the Editor, for every scene that uses it, and the edit survives exiting play mode.
    /// <see cref="UIToolkitPanelScaleRatio"/> therefore clones the asset and assigns the clone
    /// — <see cref="CloneSettings"/> below — rather than mutating the original. That is not
    /// caution, it is the difference between a runtime tweak and a source-control diff nobody
    /// asked for.</para>
    /// </remarks>
    public static class PanelScaleRatio
    {
        /// <summary>Ratio of a wide screen, 16/9, approximately 1.8. Same constant as the uGUI component.</summary>
        public const float LandscapeStandardScreenRatio = 1.8f;

        /// <summary>Ratio of a tall screen, 9/16, approximately 0.56. Same constant as the uGUI component.</summary>
        public const float PortraitStandardScreenRatio = 0.56f;

        /// <summary>Matches width (1) or height (0), by the same rule as <c>ScaleScreenRatio</c>.</summary>
        /// <remarks>
        /// Verbatim from the uGUI original: a screen at or above the standard ratio for its
        /// orientation is "long", so the height is what must be preserved and the width is
        /// what is matched; anything below matches the height. The orientation itself picks
        /// which standard ratio applies.
        ///
        /// <para>A non-positive height returns 0 — match height — rather than dividing by it.
        /// The uGUI version has no such guard and produces an Infinity comparison; there is
        /// no reason to reproduce that.</para>
        /// </remarks>
        public static float CalculateMatch(Vector2 screenSize)
        {
            if (screenSize.y <= 0f || screenSize.x <= 0f) return 0f;

            var standardScreenRatio = screenSize.x > screenSize.y ? LandscapeStandardScreenRatio : PortraitStandardScreenRatio;

            return screenSize.x / screenSize.y >= standardScreenRatio ? 1f : 0f;
        }

        /// <summary>The live screen, for the common case.</summary>
        public static float CalculateMatch()
        {
            return CalculateMatch(new(Screen.width, Screen.height));
        }

        /// <summary>
        /// Applies <see cref="CalculateMatch(Vector2)"/> to <paramref name="settings"/>,
        /// switching it into the mode where <c>match</c> means anything.
        /// </summary>
        /// <remarks>
        /// <c>match</c> is only consulted when <c>scaleMode</c> is
        /// <see cref="PanelScaleMode.ScaleWithScreenSize"/> and <c>screenMatchMode</c> is
        /// <see cref="PanelScreenMatchMode.MatchWidthOrHeight"/>, so both are set here rather
        /// than assumed — assuming them is how you get a caller who sets <c>match</c>,
        /// observes nothing, and concludes the API is broken.
        /// </remarks>
        public static void Apply(PanelSettings settings, Vector2 screenSize)
        {
            if (settings == null) return;

            settings.scaleMode       = PanelScaleMode.ScaleWithScreenSize;
            settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            settings.match           = CalculateMatch(screenSize);
        }

        /// <summary>A runtime copy of <paramref name="settings"/>, safe to mutate.</summary>
        /// <returns>Null when <paramref name="settings"/> is null.</returns>
        public static PanelSettings CloneSettings(PanelSettings settings)
        {
            if (settings == null) return null;

            var clone = Object.Instantiate(settings);
            clone.name = settings.name + " (Runtime)";

            // Not saved, not counted as a leaked asset by the test runner, and gone with the
            // scene rather than with the domain.
            clone.hideFlags = HideFlags.HideAndDontSave;

            return clone;
        }
    }

    /// <summary>
    /// Drives <see cref="PanelScaleRatio"/> from a scene object, so a screen author gets the
    /// aspect-ratio behaviour by adding a component the way they did in uGUI.
    /// </summary>
    /// <remarks>
    /// The counterpart of putting <c>ScaleScreenRatio</c> next to a <c>CanvasScaler</c>. It
    /// clones the <see cref="PanelSettings"/> by default (see
    /// <see cref="PanelScaleRatio.CloneSettings"/> for why) and re-applies on a resize, which
    /// the uGUI component does not do at all — it only runs in <c>Awake</c>, so a device that
    /// rotates after start keeps the wrong match value.
    /// </remarks>
    [RequireComponent(typeof(UIDocument))]
    public class UIToolkitPanelScaleRatio : MonoBehaviour
    {
        [SerializeField] private UIDocument uiDocument;

        [SerializeField]
        [Tooltip("Clone the PanelSettings asset before writing to it. Leave on unless you genuinely want the project asset edited.")]
        private bool cloneSettings = true;

        [SerializeField]
        [Tooltip("Re-apply when the screen size changes, which the uGUI ScaleScreenRatio never did.")]
        private bool reapplyOnResize = true;

        [SerializeField]
        [Tooltip("Reference resolution written to the panel. Leave at zero to keep whatever the asset already has.")]
        private Vector2Int referenceResolution = new(1080, 1920);

        private PanelSettings runtimeSettings;
        private Vector2Int    lastScreenSize;

        /// <summary>The settings actually being written to — the clone, when cloning.</summary>
        public PanelSettings RuntimeSettings => this.runtimeSettings;

        private void Awake()
        {
            if (this.uiDocument == null) this.uiDocument = this.GetComponent<UIDocument>();

            var source = this.uiDocument.panelSettings;

            if (source == null)
            {
                Debug.LogError($"{nameof(UIToolkitPanelScaleRatio)} on {this.gameObject.name} has no PanelSettings on its UIDocument; there is nothing to scale.", this);
                return;
            }

            this.runtimeSettings = this.cloneSettings ? PanelScaleRatio.CloneSettings(source) : source;

            if (this.cloneSettings) this.uiDocument.panelSettings = this.runtimeSettings;

            if (this.referenceResolution.x > 0 && this.referenceResolution.y > 0)
            {
                this.runtimeSettings.referenceResolution = this.referenceResolution;
            }

            this.ApplyNow();
        }

        private void Update()
        {
            if (!this.reapplyOnResize || this.runtimeSettings == null) return;
            if (Screen.width == this.lastScreenSize.x && Screen.height == this.lastScreenSize.y) return;

            this.ApplyNow();
        }

        private void OnDestroy()
        {
            // Only the clone is ours to destroy. Destroying the project asset would be a
            // rather worse bug than the one cloning exists to prevent.
            if (this.cloneSettings && this.runtimeSettings != null) Destroy(this.runtimeSettings);
        }

        /// <summary>Recomputes and writes the match value from the current screen.</summary>
        public void ApplyNow()
        {
            this.lastScreenSize = new(Screen.width, Screen.height);
            PanelScaleRatio.Apply(this.runtimeSettings, this.lastScreenSize);
        }
    }
}
