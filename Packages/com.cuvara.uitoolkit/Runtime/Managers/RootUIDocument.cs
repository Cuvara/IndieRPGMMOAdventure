namespace Cuvara.UIToolkit.Managers
{
    using Cuvara.UIToolkit.Core;
    using Cuvara.UIToolkit.View;
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>The three layers a screen can live in, resolved from one document.</summary>
    /// <remarks>
    /// A struct of three layers rather than three loose properties, so a caller can hold
    /// "the layers" as one thing — which is what a screen flow actually wants — instead of
    /// threading three references through every call.
    /// </remarks>
    public readonly struct ViewLayers
    {
        /// <summary>Where an open screen lives.</summary>
        public IViewLayer Screen { get; }

        /// <summary>Where a screen goes when it is closed but kept alive for reopening.</summary>
        public IViewLayer Hidden { get; }

        /// <summary>Where a popup lives, above every screen.</summary>
        public IViewLayer Overlay { get; }

        public ViewLayers(IViewLayer screen, IViewLayer hidden, IViewLayer overlay)
        {
            this.Screen  = screen;
            this.Hidden  = hidden;
            this.Overlay = overlay;
        }
    }

    /// <summary>
    /// One <see cref="UIDocument"/> whose <c>rootVisualElement</c> carries the three layers
    /// a screen can be parented into.
    /// </summary>
    /// <remarks>
    /// <para>Still a MonoBehaviour, because <see cref="UIDocument"/> still is one — that is
    /// the only piece of this package that has to live on a GameObject.</para>
    ///
    /// <para>Layers are resolved BY NAME from the UXML rather than by serialized reference,
    /// since there is no such thing as an inspector reference to a
    /// <see cref="VisualElement"/>. A name that does not resolve is reported once at
    /// <c>Awake</c> and falls back to the document root, so a mis-named layer shows up in
    /// the log instead of as a screen that renders nowhere.</para>
    ///
    /// <para>The names are serialized fields, not constants, because the UXML belongs to
    /// the consuming project. A host with an existing document and different element names
    /// points this at them rather than renaming its own UI to suit the package.</para>
    /// </remarks>
    [RequireComponent(typeof(UIDocument))]
    public class RootUIDocument : MonoBehaviour
    {
        [SerializeField] private UIDocument uiDocument;

        [SerializeField] private string rootUIShowElementName    = "root-ui-show";
        [SerializeField] private string rootUIClosedElementName  = "root-ui-closed";
        [SerializeField] private string rootUIOverlayElementName = "root-ui-overlay";

        public UIDocument UIDocument => this.uiDocument;

        public VisualElement RootUIShowElement    { get { this.EnsureResolved(); return this.showElement; } }

        public VisualElement RootUIClosedElement  { get { this.EnsureResolved(); return this.closedElement; } }

        public VisualElement RootUIOverlayElement { get { this.EnsureResolved(); return this.overlayElement; } }

        /// <summary>The three roots as layers, cached — never allocated per reparent.</summary>
        public VisualElementViewLayer ShowLayer    { get { this.EnsureResolved(); return this.showLayer; } }

        public VisualElementViewLayer ClosedLayer  { get { this.EnsureResolved(); return this.closedLayer; } }

        public VisualElementViewLayer OverlayLayer { get { this.EnsureResolved(); return this.overlayLayer; } }

        /// <summary>The same three, as one value.</summary>
        public ViewLayers Layers => new(this.ShowLayer, this.ClosedLayer, this.OverlayLayer);

        /// <summary>The panel root, or null if the UIDocument has no source asset.</summary>
        public VisualElement RootVisualElement
        {
            get
            {
                if (this.uiDocument == null) this.uiDocument = this.GetComponent<UIDocument>();

                return this.uiDocument == null ? null : this.uiDocument.rootVisualElement;
            }
        }

        private VisualElement showElement, closedElement, overlayElement;

        private VisualElementViewLayer showLayer, closedLayer, overlayLayer;

        private bool resolved;

        private void Awake() { this.EnsureResolved(); }

        /// <summary>
        /// Resolves the three layers on first use, and only once.
        /// </summary>
        /// <remarks>
        /// <para><b>Lazy rather than only in <c>Awake</c>, and this is not a micro-optimisation
        /// — it is a correctness fix found by putting the package in a scene.</b>
        /// <see cref="UIDocument"/> builds its <c>rootVisualElement</c> in its own
        /// <c>OnEnable</c>, and the relative order of two components' <c>Awake</c>/<c>OnEnable</c>
        /// across two GameObjects is undefined. A host whose composition root initialises first
        /// therefore saw null layers, and the symptom was an ArgumentNullException from deep
        /// inside the flow with nothing pointing at ordering as the cause.</para>
        ///
        /// <para>Resolving on first access removes the ordering question entirely: whoever asks
        /// first pays for it, and by then the document is necessarily built, because asking
        /// means the panel is in use. <c>Awake</c> still tries, so the common case is resolved
        /// early and the error below is reported at the earliest honest moment.</para>
        /// </remarks>
        private void EnsureResolved()
        {
            if (this.resolved) return;

            if (this.uiDocument == null) this.uiDocument = this.GetComponent<UIDocument>();

            var root = this.uiDocument == null ? null : this.uiDocument.rootVisualElement;

            // Not an error yet: Awake may simply have run before the document was built, and the
            // next caller will resolve successfully. Only a caller that actually needs the layers
            // and still finds nothing gets a complaint, from the property it asked for.
            if (root == null) return;

            this.showElement    = this.Resolve(root, this.rootUIShowElementName);
            this.closedElement  = this.Resolve(root, this.rootUIClosedElementName);
            this.overlayElement = this.Resolve(root, this.rootUIOverlayElementName);

            this.showLayer    = new(this.showElement);
            this.closedLayer  = new(this.closedElement);
            this.overlayLayer = new(this.overlayElement);

            this.resolved = true;
        }

        private VisualElement Resolve(VisualElement root, string elementName)
        {
            if (string.IsNullOrEmpty(elementName)) return root;

            var element = root.Q<VisualElement>(elementName);

            if (element == null)
            {
                Debug.LogError($"Can not find VisualElement named '{elementName}' in {this.gameObject.name}; falling back to the document root.", this);
                return root;
            }

            return element;
        }
    }
}
