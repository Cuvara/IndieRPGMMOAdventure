namespace Cuvara.UIToolkit.Utilities
{
    using System;
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>How a <see cref="SafeAreaElement"/> spends the insets it calculates.</summary>
    public enum SafeAreaApplyMode
    {
        /// <summary>
        /// Pad the element inwards. The element itself still covers the whole screen, so a
        /// background on it reaches under the notch — which is what the uGUI
        /// <c>SafeArea</c> docstring recommends as usage (2), and is the reason it is the
        /// default here.
        /// </summary>
        Padding,

        /// <summary>
        /// Inset the element itself: <c>position: absolute</c> plus the four edges. The
        /// element then IS the safe area, background and all — the uGUI component's
        /// <c>stretch = true</c> behaviour, where the panel's anchors are moved to the
        /// safe rect.
        /// </summary>
        Inset,
    }

    /// <summary>
    /// The UI Toolkit form of <c>Scripts/UIModule/Utilities/UIStuff/SafeArea.cs</c>: keeps
    /// its content inside <see cref="Screen.safeArea"/> on notched devices.
    /// </summary>
    /// <remarks>
    /// <para><b>Add alongside; the uGUI component is untouched.</b> A uGUI screen still
    /// uses <c>SafeArea</c> on a <c>RectTransform</c>. This is what a UI Toolkit screen
    /// uses instead, and the two never meet.</para>
    ///
    /// <para><b>Why not <c>PanelSettings.SetScreenToPanelSpaceFunction</c>.</b> That was
    /// the prior guess and it is the wrong tool — it is verified present in 6000.3.9f1,
    /// and verified to be about something else. Its documented job is
    /// "the transformation from screen space to panel space", i.e. where a POINTER lands.
    /// Using it for a safe area would move the touch coordinates without moving a single
    /// pixel of layout: the UI would still draw under the notch, and clicks would now land
    /// somewhere other than what was drawn. Insets on an element are the layout mechanism,
    /// so insets on an element are what this uses.</para>
    ///
    /// <para><b>What replaces the uGUI <c>Update()</c> poll.</b> The uGUI component reads
    /// <c>Screen.safeArea</c> every frame because there is no notification when a device
    /// rotates or the OS changes the cut-out. There still is not one, so this keeps a poll
    /// — but on the panel's own scheduler at <see cref="PollIntervalMilliseconds"/> rather
    /// than per frame, plus an immediate re-apply on <see cref="GeometryChangedEvent"/>
    /// (which DOES fire on a resize) and on attach. The poll exists for the rotation case
    /// where the panel size happens not to change; everything else is event-driven. Like
    /// the uGUI original it re-styles only when the computed insets actually change.</para>
    ///
    /// <para><b>Sources are injectable.</b> <see cref="SafeAreaSource"/>,
    /// <see cref="ScreenSizeSource"/> and <see cref="PixelsPerPointSource"/> default to the
    /// live values and exist so a test can drive a notch through the element without a
    /// device. <see cref="SafeAreaCalculator"/> is where the maths is tested; these are
    /// what let the element's own wiring be tested too.</para>
    /// </remarks>
    [UxmlElement]
    public partial class SafeAreaElement : VisualElement
    {
        /// <summary>How often the safe area is re-read when nothing else has changed.</summary>
        public const long PollIntervalMilliseconds = 250;

        /// <summary>USS class added to every instance, for styling and for UQuery.</summary>
        public const string UssClassName = "gf-safe-area";

        [UxmlAttribute("conform-x")] public bool ConformX { get => this.conformX; set { this.conformX = value; this.Refresh(true); } }

        [UxmlAttribute("conform-y")] public bool ConformY { get => this.conformY; set { this.conformY = value; this.Refresh(true); } }

        [UxmlAttribute("apply-mode")] public SafeAreaApplyMode ApplyMode { get => this.applyMode; set { this.applyMode = value; this.Refresh(true); } }

        private bool              conformX  = true;
        private bool              conformY  = true;
        private SafeAreaApplyMode applyMode = SafeAreaApplyMode.Padding;

        /// <summary>The safe rect in screen pixels. Defaults to <see cref="Screen.safeArea"/>.</summary>
        public Func<Rect> SafeAreaSource { get; set; }

        /// <summary>The screen size in pixels. Defaults to <c>Screen.width/height</c>.</summary>
        public Func<Vector2> ScreenSizeSource { get; set; }

        /// <summary>
        /// The screen-pixel to panel-unit ratio. Defaults to the attached panel's
        /// <see cref="IPanel.scaledPixelsPerPoint"/>, or 0 while detached — which
        /// <see cref="SafeAreaCalculator"/> reads as "no meaningful answer yet".
        /// </summary>
        public Func<float> PixelsPerPointSource { get; set; }

        /// <summary>The insets last written to the style, in panel units, (left, top, right, bottom).</summary>
        public Vector4 AppliedPadding { get; private set; }

        private bool hasApplied;

        private IVisualElementScheduledItem pollItem;

        public SafeAreaElement()
        {
            this.AddToClassList(UssClassName);

            // A container, not a control: it must not eat clicks aimed at whatever is
            // behind the padded strip. Children keep their own picking mode.
            this.pickingMode = PickingMode.Ignore;

            this.RegisterCallback<AttachToPanelEvent>(this.OnAttachToPanel);
            this.RegisterCallback<DetachFromPanelEvent>(this.OnDetachFromPanel);
            this.RegisterCallback<GeometryChangedEvent>(_ => this.Refresh());
        }

        private void OnAttachToPanel(AttachToPanelEvent evt)
        {
            this.Refresh(true);

            // Every() on the panel scheduler, so the poll dies with the panel rather than
            // outliving it the way a MonoBehaviour Update would.
            this.pollItem ??= this.schedule.Execute(() => this.Refresh()).Every(PollIntervalMilliseconds);
            this.pollItem.Resume();
        }

        private void OnDetachFromPanel(DetachFromPanelEvent evt)
        {
            this.pollItem?.Pause();
        }

        /// <summary>Recomputes the insets and writes them to the style if they moved.</summary>
        /// <param name="force">Apply even when the computed insets equal the last applied ones.</param>
        public void Refresh(bool force = false)
        {
            var padding = SafeAreaCalculator.CalculatePadding(
                this.SafeAreaSource?.Invoke() ?? Screen.safeArea,
                this.ScreenSizeSource?.Invoke() ?? new Vector2(Screen.width, Screen.height),
                this.PixelsPerPointSource?.Invoke() ?? (this.panel?.scaledPixelsPerPoint ?? 0f),
                this.conformX,
                this.conformY);

            if (!force && this.hasApplied && padding == this.AppliedPadding) return;

            this.AppliedPadding = padding;
            this.hasApplied     = true;

            switch (this.applyMode)
            {
                case SafeAreaApplyMode.Inset:
                    // Clear the other mode's leftovers, so flipping ApplyMode at runtime
                    // does not leave the element inset AND padded by the same amount.
                    this.SetPadding(0f);

                    this.style.position = Position.Absolute;
                    this.style.left     = padding.x;
                    this.style.top      = padding.y;
                    this.style.right    = padding.z;
                    this.style.bottom   = padding.w;
                    break;

                case SafeAreaApplyMode.Padding:
                default:
                    this.style.paddingLeft   = padding.x;
                    this.style.paddingTop    = padding.y;
                    this.style.paddingRight  = padding.z;
                    this.style.paddingBottom = padding.w;
                    break;
            }
        }

        private void SetPadding(float value)
        {
            this.style.paddingLeft   = value;
            this.style.paddingTop    = value;
            this.style.paddingRight  = value;
            this.style.paddingBottom = value;
        }
    }
}
