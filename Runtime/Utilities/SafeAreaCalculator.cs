namespace Cuvara.UIToolkit.Utilities
{
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>
    /// The arithmetic behind <see cref="SafeAreaElement"/>: screen-pixel
    /// <see cref="Screen.safeArea"/> in, panel-unit insets out.
    /// </summary>
    /// <remarks>
    /// <para><b>Why this is a separate static class.</b> It is the only part of the safe
    /// area that can be wrong in a way a test can catch. Everything else —
    /// registering for geometry changes, assigning four style properties — is plumbing.
    /// Pulling the maths out means the edge cases (no inset, a degenerate safe area, a
    /// landscape panel, a safe area reported larger than the screen) are exercised
    /// without a panel, a screen or a device in sight.</para>
    ///
    /// <para><b>Why <c>pixelsPerPoint</c> and not a panel size.</b>
    /// <c>Screen.safeArea</c> is in real screen pixels; a <c>VisualElement</c>'s
    /// <c>style.paddingLeft</c> is in panel units. <see cref="IPanel.scaledPixelsPerPoint"/>
    /// is exactly the ratio between them — it already folds in
    /// <see cref="PanelSettings.scaleMode"/>, the reference resolution and the match
    /// value — so dividing by it is the whole conversion. Deriving the same factor from
    /// a panel rect divided by the screen rect would work only while the panel covers
    /// the full screen, which stops being true the moment anyone sets a
    /// <c>targetTexture</c>.</para>
    ///
    /// <para><b>The Y axis flips.</b> <c>Screen.safeArea</c> has its origin at the bottom
    /// left, UI Toolkit lays out from the top left. So the TOP inset is measured from
    /// <c>safeArea.yMax</c> up to the screen height, and the BOTTOM inset is
    /// <c>safeArea.yMin</c> itself — not the other way round, which is the single easiest
    /// thing to get backwards here and puts the notch cut-out along the home bar.</para>
    /// </remarks>
    public static class SafeAreaCalculator
    {
        /// <summary>
        /// The four insets, in panel units, that keep content inside <paramref name="safeArea"/>.
        /// </summary>
        /// <param name="safeArea">Normally <see cref="Screen.safeArea"/>, in screen pixels, origin bottom-left.</param>
        /// <param name="screenSize">Normally <c>new Vector2(Screen.width, Screen.height)</c>, in screen pixels.</param>
        /// <param name="pixelsPerPoint">Normally <see cref="IPanel.scaledPixelsPerPoint"/>.</param>
        /// <param name="conformX">Ignore the horizontal insets when false, as the uGUI <c>SafeArea</c> does.</param>
        /// <param name="conformY">Ignore the vertical insets when false.</param>
        /// <returns>
        /// <c>(x, y, z, w)</c> = <c>(left, top, right, bottom)</c> — the same order the CSS
        /// shorthand uses, so the mapping onto <c>paddingLeft/Top/Right/Bottom</c> reads
        /// straight through.
        /// </returns>
        /// <remarks>
        /// Returns <see cref="Vector4.zero"/> — no inset at all — rather than throwing, for
        /// any input that cannot produce a meaningful answer: a zero or negative screen,
        /// or a non-positive <paramref name="pixelsPerPoint"/>. Both happen legitimately,
        /// on the frames before a panel has been laid out; a screen that is briefly not
        /// inset is a far better failure than an exception out of a geometry callback or a
        /// NaN written into a style.
        /// </remarks>
        public static Vector4 CalculatePadding(Rect safeArea, Vector2 screenSize, float pixelsPerPoint, bool conformX = true, bool conformY = true)
        {
            if (screenSize.x <= 0f || screenSize.y <= 0f) return Vector4.zero;
            if (pixelsPerPoint <= 0f || float.IsNaN(pixelsPerPoint) || float.IsInfinity(pixelsPerPoint)) return Vector4.zero;

            // A safe area reported outside the screen is not a thing that should ever
            // happen, and does: editor device simulators and some OEM builds report a
            // rect a pixel or two over. Clamping here rather than trusting the platform
            // keeps every inset non-negative without a second Max() at the bottom.
            var xMin = Mathf.Clamp(safeArea.xMin, 0f, screenSize.x);
            var xMax = Mathf.Clamp(safeArea.xMax, xMin, screenSize.x);
            var yMin = Mathf.Clamp(safeArea.yMin, 0f, screenSize.y);
            var yMax = Mathf.Clamp(safeArea.yMax, yMin, screenSize.y);

            var left   = conformX ? xMin / pixelsPerPoint : 0f;
            var right  = conformX ? (screenSize.x - xMax) / pixelsPerPoint : 0f;
            var bottom = conformY ? yMin / pixelsPerPoint : 0f;
            var top    = conformY ? (screenSize.y - yMax) / pixelsPerPoint : 0f;

            return new(left, top, right, bottom);
        }

        /// <summary>The live inputs, read off <see cref="Screen"/>, for the common case.</summary>
        public static Vector4 CalculatePadding(float pixelsPerPoint, bool conformX = true, bool conformY = true)
        {
            return CalculatePadding(Screen.safeArea, new(Screen.width, Screen.height), pixelsPerPoint, conformX, conformY);
        }
    }
}
