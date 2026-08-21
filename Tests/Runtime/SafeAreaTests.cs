
namespace Cuvara.UIToolkit.Tests
{
    using Cuvara.UIToolkit.Utilities;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>
    /// The safe-area arithmetic, at its edges.
    /// </summary>
    /// <remarks>
    /// <para>Every case here is a number a device actually reports. A notch is a top inset
    /// in portrait and a LEFT or RIGHT inset in landscape — the same physical cut-out, a
    /// different axis — and the conversion has to survive a rotation without transposing
    /// anything. The degenerate cases (a zero-area safe rect, a rect reported outside the
    /// screen) are what an editor device simulator produces, not hypotheticals.</para>
    ///
    /// <para>These are plain <c>[Test]</c> rather than <c>[UnityTest]</c> — the maths needs
    /// no panel and no frame — but they live in the PlayMode assembly so the whole tranche
    /// runs in one invocation.</para>
    /// </remarks>
    public class SafeAreaCalculatorTests
    {
        private static readonly Vector2 Portrait  = new(1080f, 1920f);
        private static readonly Vector2 Landscape = new(1920f, 1080f);

        // (left, top, right, bottom), matching CalculatePadding's return order.
        private static void AssertPadding(Vector4 actual, float left, float top, float right, float bottom)
        {
            Assert.That(actual.x, Is.EqualTo(left).Within(0.001f), "left");
            Assert.That(actual.y, Is.EqualTo(top).Within(0.001f), "top");
            Assert.That(actual.z, Is.EqualTo(right).Within(0.001f), "right");
            Assert.That(actual.w, Is.EqualTo(bottom).Within(0.001f), "bottom");
        }

        #region The ordinary cases

        [Test]
        public void NoInset_ASafeAreaEqualToTheScreen_ProducesNoPadding()
        {
            var padding = SafeAreaCalculator.CalculatePadding(new(0f, 0f, 1080f, 1920f), Portrait, 1f);

            AssertPadding(padding, 0f, 0f, 0f, 0f);
        }

        [Test]
        public void NotchAtTheTop_PadsTheTopOnly()
        {
            // Screen.safeArea has its origin bottom-left, so a 90px notch at the top of a
            // 1920-tall screen is a rect ending at y = 1830, not one starting at y = 90.
            var padding = SafeAreaCalculator.CalculatePadding(new(0f, 0f, 1080f, 1830f), Portrait, 1f);

            AssertPadding(padding, 0f, 90f, 0f, 0f);
        }

        [Test]
        public void HomeIndicatorAtTheBottom_PadsTheBottomOnly()
        {
            var padding = SafeAreaCalculator.CalculatePadding(new(0f, 60f, 1080f, 1860f), Portrait, 1f);

            AssertPadding(padding, 0f, 0f, 0f, 60f);
        }

        [Test]
        public void TopAndBottomInsets_AreNotSwapped()
        {
            // The regression test for the Y flip: 90 at the top, 60 at the bottom, and the
            // two must not trade places. A transposed implementation passes both of the
            // single-axis tests above and fails this one.
            var padding = SafeAreaCalculator.CalculatePadding(new(0f, 60f, 1080f, 1770f), Portrait, 1f);

            AssertPadding(padding, 0f, 90f, 0f, 60f);
        }

        [Test]
        public void RotatedToLandscape_TheSameNotchBecomesAHorizontalInset()
        {
            var padding = SafeAreaCalculator.CalculatePadding(new(90f, 0f, 1740f, 1080f), Landscape, 1f);

            AssertPadding(padding, 90f, 0f, 90f, 0f);
        }

        [Test]
        public void RotatedToLandscape_NotchOnOneSideOnly()
        {
            // Rotating a single-notch phone puts the cut-out on the left or the right, not
            // both. An implementation that mirrors the insets passes the symmetric case
            // above and fails here.
            var padding = SafeAreaCalculator.CalculatePadding(new(132f, 0f, 1788f, 1080f), Landscape, 1f);

            AssertPadding(padding, 132f, 0f, 0f, 0f);
        }

        [Test]
        public void PixelsPerPoint_ConvertsScreenPixelsIntoPanelUnits()
        {
            // A panel at 2 screen pixels per panel unit halves every inset. Getting this
            // backwards — multiplying — is the difference between a 45-unit gap and a
            // 180-unit one on a phone.
            var padding = SafeAreaCalculator.CalculatePadding(new(0f, 60f, 1080f, 1770f), Portrait, 2f);

            AssertPadding(padding, 0f, 45f, 0f, 30f);
        }

        [Test]
        public void FractionalPixelsPerPoint_ScalesUp()
        {
            var padding = SafeAreaCalculator.CalculatePadding(new(0f, 0f, 1080f, 1830f), Portrait, 0.5f);

            AssertPadding(padding, 0f, 180f, 0f, 0f);
        }

        [Test]
        public void ConformX_False_DropsTheHorizontalInsetsAndKeepsTheVertical()
        {
            var padding = SafeAreaCalculator.CalculatePadding(new(90f, 60f, 900f, 1770f), Portrait, 1f, conformX: false);

            AssertPadding(padding, 0f, 90f, 0f, 60f);
        }

        [Test]
        public void ConformY_False_DropsTheVerticalInsetsAndKeepsTheHorizontal()
        {
            var padding = SafeAreaCalculator.CalculatePadding(new(90f, 60f, 900f, 1770f), Portrait, 1f, conformY: false);

            AssertPadding(padding, 90f, 0f, 90f, 0f);
        }

        [Test]
        public void BothConformsFalse_ProducesNoPaddingEvenWithARealNotch()
        {
            var padding = SafeAreaCalculator.CalculatePadding(new(90f, 60f, 900f, 1770f), Portrait, 1f, conformX: false, conformY: false);

            AssertPadding(padding, 0f, 0f, 0f, 0f);
        }

        #endregion

        #region Degenerate safe areas

        [Test]
        public void AZeroSafeAreaAtTheOrigin_PadsAwayTheWholeScreen()
        {
            // The full-screen-inset edge: nothing is safe. The rect sits at the bottom-left
            // corner with no area, so everything above and to the right of it is inset.
            var padding = SafeAreaCalculator.CalculatePadding(new(0f, 0f, 0f, 0f), Portrait, 1f);

            AssertPadding(padding, 0f, 1920f, 1080f, 0f);
        }

        [Test]
        public void AZeroSafeAreaAtTheCentre_PadsInFromEveryEdge()
        {
            var padding = SafeAreaCalculator.CalculatePadding(new(540f, 960f, 0f, 0f), Portrait, 1f);

            AssertPadding(padding, 540f, 960f, 540f, 960f);
        }

        [Test]
        public void ASafeAreaLargerThanTheScreen_IsClampedAndNeverNegative()
        {
            var padding = SafeAreaCalculator.CalculatePadding(new(-40f, -40f, 1160f, 2000f), Portrait, 1f);

            AssertPadding(padding, 0f, 0f, 0f, 0f);
        }

        [Test]
        public void ASafeAreaEntirelyOffScreen_ClampsToTheNearestEdge()
        {
            // xMin and xMax both clamp to 0, so the whole width is inset from the right.
            var padding = SafeAreaCalculator.CalculatePadding(new(-500f, -500f, 200f, 200f), Portrait, 1f);

            AssertPadding(padding, 0f, 1920f, 1080f, 0f);
        }

        [Test]
        public void ANegativeWidthSafeArea_DoesNotProduceNegativePadding()
        {
            // Rect normalises a negative width itself, but the clamp has to hold even if it
            // did not: no inset may come out below zero, or the element grows instead of
            // shrinking.
            var padding = SafeAreaCalculator.CalculatePadding(new(600f, 900f, -200f, -300f), Portrait, 1f);

            Assert.That(padding.x, Is.GreaterThanOrEqualTo(0f), "left");
            Assert.That(padding.y, Is.GreaterThanOrEqualTo(0f), "top");
            Assert.That(padding.z, Is.GreaterThanOrEqualTo(0f), "right");
            Assert.That(padding.w, Is.GreaterThanOrEqualTo(0f), "bottom");
        }

        #endregion

        #region Inputs with no meaningful answer

        [Test]
        public void AZeroWidthScreen_ProducesNoPaddingRatherThanADivideByZero()
        {
            var padding = SafeAreaCalculator.CalculatePadding(new(0f, 0f, 100f, 100f), new(0f, 1920f), 1f);

            AssertPadding(padding, 0f, 0f, 0f, 0f);
        }

        [Test]
        public void AZeroHeightScreen_ProducesNoPadding()
        {
            var padding = SafeAreaCalculator.CalculatePadding(new(0f, 0f, 100f, 100f), new(1080f, 0f), 1f);

            AssertPadding(padding, 0f, 0f, 0f, 0f);
        }

        [Test]
        public void ANegativeScreen_ProducesNoPadding()
        {
            var padding = SafeAreaCalculator.CalculatePadding(new(0f, 0f, 100f, 100f), new(-1080f, -1920f), 1f);

            AssertPadding(padding, 0f, 0f, 0f, 0f);
        }

        [Test]
        public void AZeroPixelsPerPoint_ProducesNoPadding()
        {
            // What a detached element reports. Returning zero is what lets the element be
            // refreshed before it is attached without writing an Infinity into a style.
            var padding = SafeAreaCalculator.CalculatePadding(new(0f, 0f, 1080f, 1830f), Portrait, 0f);

            AssertPadding(padding, 0f, 0f, 0f, 0f);
        }

        [Test]
        public void ANegativePixelsPerPoint_ProducesNoPadding()
        {
            var padding = SafeAreaCalculator.CalculatePadding(new(0f, 0f, 1080f, 1830f), Portrait, -2f);

            AssertPadding(padding, 0f, 0f, 0f, 0f);
        }

        [Test]
        public void ANaNPixelsPerPoint_ProducesNoPadding()
        {
            var padding = SafeAreaCalculator.CalculatePadding(new(0f, 0f, 1080f, 1830f), Portrait, float.NaN);

            AssertPadding(padding, 0f, 0f, 0f, 0f);
        }

        [Test]
        public void AnInfinitePixelsPerPoint_ProducesNoPadding()
        {
            var padding = SafeAreaCalculator.CalculatePadding(new(0f, 0f, 1080f, 1830f), Portrait, float.PositiveInfinity);

            AssertPadding(padding, 0f, 0f, 0f, 0f);
        }

        [Test]
        public void NoInputIsEverRejectedByThrowing()
        {
            // A geometry callback is not a place an exception can usefully surface from.
            Assert.DoesNotThrow(() => SafeAreaCalculator.CalculatePadding(new(float.NaN, float.NaN, float.NaN, float.NaN), new(float.NaN, float.NaN), float.NaN));
        }

        #endregion
    }

    /// <summary>
    /// <see cref="SafeAreaElement"/>'s own wiring: that it asks
    /// <see cref="SafeAreaCalculator"/> the right question and spends the answer correctly.
    /// </summary>
    public class SafeAreaElementTests
    {
        private static SafeAreaElement Detached(Rect safeArea, Vector2 screenSize, float pixelsPerPoint)
        {
            return new()
            {
                SafeAreaSource       = () => safeArea,
                ScreenSizeSource     = () => screenSize,
                PixelsPerPointSource = () => pixelsPerPoint,
            };
        }

        [Test]
        public void PaddingMode_WritesTheInsetsAsPadding()
        {
            var element = Detached(new(0f, 60f, 1080f, 1770f), new(1080f, 1920f), 1f);

            element.Refresh(true);

            Assert.That(element.style.paddingTop.value.value, Is.EqualTo(90f).Within(0.001f));
            Assert.That(element.style.paddingBottom.value.value, Is.EqualTo(60f).Within(0.001f));
            Assert.That(element.style.paddingLeft.value.value, Is.EqualTo(0f).Within(0.001f));
            Assert.That(element.style.paddingRight.value.value, Is.EqualTo(0f).Within(0.001f));
        }

        [Test]
        public void PaddingMode_LeavesTheElementItselfWhereItWas()
        {
            // The uGUI docstring's usage (2): the background still reaches under the notch.
            var element = Detached(new(0f, 60f, 1080f, 1770f), new(1080f, 1920f), 1f);

            element.Refresh(true);

            Assert.That(element.style.position.value, Is.Not.EqualTo(Position.Absolute),
                "Padding mode must not take the element out of flow; only Inset mode does.");
        }

        [Test]
        public void InsetMode_WritesTheInsetsAsAbsoluteEdges()
        {
            var element = Detached(new(0f, 60f, 1080f, 1770f), new(1080f, 1920f), 1f);

            element.ApplyMode = SafeAreaApplyMode.Inset;

            Assert.That(element.style.position.value, Is.EqualTo(Position.Absolute));
            Assert.That(element.style.top.value.value, Is.EqualTo(90f).Within(0.001f));
            Assert.That(element.style.bottom.value.value, Is.EqualTo(60f).Within(0.001f));
            Assert.That(element.style.left.value.value, Is.EqualTo(0f).Within(0.001f));
            Assert.That(element.style.right.value.value, Is.EqualTo(0f).Within(0.001f));
        }

        [Test]
        public void SwitchingFromPaddingToInset_ClearsThePadding()
        {
            // Otherwise the element is inset by the notch AND padded by it — a double gap
            // that only shows up on a notched device, which is the worst place to find it.
            var element = Detached(new(0f, 60f, 1080f, 1770f), new(1080f, 1920f), 1f);

            element.Refresh(true);
            Assert.That(element.style.paddingTop.value.value, Is.EqualTo(90f).Within(0.001f), "precondition");

            element.ApplyMode = SafeAreaApplyMode.Inset;

            Assert.That(element.style.paddingTop.value.value, Is.EqualTo(0f).Within(0.001f));
            Assert.That(element.style.paddingBottom.value.value, Is.EqualTo(0f).Within(0.001f));
        }

        [Test]
        public void ChangingConformX_ReappliesImmediately()
        {
            var element = Detached(new(90f, 60f, 900f, 1770f), new(1080f, 1920f), 1f);

            element.Refresh(true);
            Assert.That(element.AppliedPadding.x, Is.EqualTo(90f).Within(0.001f), "precondition");

            element.ConformX = false;

            Assert.That(element.AppliedPadding.x, Is.EqualTo(0f).Within(0.001f));
            Assert.That(element.AppliedPadding.y, Is.EqualTo(90f).Within(0.001f), "the vertical inset must survive");
        }

        [Test]
        public void ANewElementDefaultsToConformingOnBothAxes()
        {
            var element = new SafeAreaElement();

            Assert.That(element.ConformX, Is.True);
            Assert.That(element.ConformY, Is.True);
            Assert.That(element.ApplyMode, Is.EqualTo(SafeAreaApplyMode.Padding));
        }

        [Test]
        public void ANewElementDoesNotSwallowClicks()
        {
            // It is a container around content, not a control. Picking Position here would
            // make the whole screen unclickable through it.
            Assert.That(new SafeAreaElement().pickingMode, Is.EqualTo(PickingMode.Ignore));
        }

        [Test]
        public void DetachedWithNoSources_AppliesNothing()
        {
            // The negative path that matters: constructed but never attached, so there is no
            // panel to ask for a scale. It must not throw and must not write an inset.
            var element = new SafeAreaElement();

            Assert.DoesNotThrow(() => element.Refresh(true));
            Assert.That(element.AppliedPadding, Is.EqualTo(Vector4.zero));
        }

        [Test]
        public void RefreshWithoutForce_DoesNotRewriteAnUnchangedValue()
        {
            var safeArea = new Rect(0f, 60f, 1080f, 1770f);
            var element  = Detached(safeArea, new(1080f, 1920f), 1f);

            element.Refresh(true);
            var first = element.AppliedPadding;

            element.Refresh();

            Assert.That(element.AppliedPadding, Is.EqualTo(first));
        }

        [Test]
        public void ItCanBeAuthoredInUxml_WithItsAttributes()
        {
            // The claim the wizard's UXML template depends on: [UxmlElement] really does
            // register this type under its namespace, and the two attributes really do
            // deserialise. Nothing else in the package instantiates a SafeAreaElement from
            // UXML, so without this the template would be a guess.
            #if UNITY_EDITOR
            var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Packages/com.cuvara.uitoolkit/Tests/Runtime/SafeAreaScreen.uxml");
            Assert.That(asset, Is.Not.Null, "Could not load the safe-area UXML.");

            var element = asset.CloneTree().Q<SafeAreaElement>("safe-area");

            Assert.That(element, Is.Not.Null, "The <gf:SafeAreaElement> in the UXML did not resolve to a SafeAreaElement.");
            Assert.That(element.ConformX, Is.False, "conform-x=\"false\" did not deserialise.");
            Assert.That(element.ConformY, Is.True, "conform-y should have kept its default.");
            Assert.That(element.ApplyMode, Is.EqualTo(SafeAreaApplyMode.Inset), "apply-mode=\"Inset\" did not deserialise.");
            #else
            Assert.Ignore("Loads its UXML through the AssetDatabase; Editor only.");
            #endif
        }

        [Test]
        public void AChangedSafeArea_IsPickedUpByRefresh()
        {
            // The rotation case, without a device: the source changes underneath and the
            // next poll must notice.
            var safeArea = new Rect(0f, 0f, 1080f, 1920f);

            var element = new SafeAreaElement
            {
                SafeAreaSource       = () => safeArea,
                ScreenSizeSource     = () => new(1080f, 1920f),
                PixelsPerPointSource = () => 1f,
            };

            element.Refresh(true);
            Assert.That(element.AppliedPadding, Is.EqualTo(Vector4.zero), "precondition");

            safeArea = new(0f, 0f, 1080f, 1830f);
            element.Refresh();

            Assert.That(element.AppliedPadding.y, Is.EqualTo(90f).Within(0.001f));
        }
    }
}

