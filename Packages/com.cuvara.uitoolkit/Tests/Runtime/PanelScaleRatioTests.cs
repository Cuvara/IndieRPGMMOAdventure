
namespace Cuvara.UIToolkit.Tests
{
    using Cuvara.UIToolkit.Utilities;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>
    /// The UI Toolkit replacement for <c>ScaleScreenRatio</c>, which drives a
    /// <c>CanvasScaler</c>'s <c>matchWidthOrHeight</c>.
    /// </summary>
    /// <remarks>
    /// The rule under test is the uGUI component's, unchanged: a screen at or above the
    /// standard ratio for its orientation matches WIDTH (1), anything below matches HEIGHT
    /// (0). The standard ratio itself depends on the orientation — 1.8 landscape, 0.56
    /// portrait — which is the part that is easy to drop, and the square case is what
    /// catches dropping it.
    /// </remarks>
    public class PanelScaleRatioTests
    {
        [Test]
        public void ATallPortraitPhone_MatchesHeight()
        {
            // 1080x2400 is 0.45, below the 0.56 portrait standard: keep the width, scale the
            // height.
            Assert.That(PanelScaleRatio.CalculateMatch(new(1080f, 2400f)), Is.EqualTo(0f));
        }

        [Test]
        public void AStandard16By9Portrait_MatchesWidth()
        {
            // 1080x1920 is 0.5625, just above 0.56 — deliberately the closest real case to
            // the threshold, so an inverted comparison shows up here.
            Assert.That(PanelScaleRatio.CalculateMatch(new(1080f, 1920f)), Is.EqualTo(1f));
        }

        [Test]
        public void ATabletPortrait_MatchesWidth()
        {
            Assert.That(PanelScaleRatio.CalculateMatch(new(1536f, 2048f)), Is.EqualTo(1f));
        }

        [Test]
        public void AStandard16By9Landscape_MatchesHeight()
        {
            // 1920x1080 is 1.777, below the 1.8 landscape standard. The uGUI component says
            // 0 here and so must this.
            Assert.That(PanelScaleRatio.CalculateMatch(new(1920f, 1080f)), Is.EqualTo(0f));
        }

        [Test]
        public void AnUltraWideLandscape_MatchesWidth()
        {
            Assert.That(PanelScaleRatio.CalculateMatch(new(2400f, 1080f)), Is.EqualTo(1f));
        }

        [Test]
        public void ASquareScreen_IsTreatedAsPortrait()
        {
            // width > height is false, so the PORTRAIT standard applies: 1.0 >= 0.56 -> 1.
            // Reading it as landscape would give 1.0 < 1.8 -> 0, the opposite answer.
            Assert.That(PanelScaleRatio.CalculateMatch(new(1000f, 1000f)), Is.EqualTo(1f));
        }

        [Test]
        public void AZeroHeightScreen_MatchesHeightRatherThanDividingByZero()
        {
            Assert.That(PanelScaleRatio.CalculateMatch(new(1080f, 0f)), Is.EqualTo(0f));
        }

        [Test]
        public void AZeroWidthScreen_MatchesHeight()
        {
            Assert.That(PanelScaleRatio.CalculateMatch(new(0f, 1920f)), Is.EqualTo(0f));
        }

        [Test]
        public void ANegativeScreen_MatchesHeight()
        {
            Assert.That(PanelScaleRatio.CalculateMatch(new(-1080f, -1920f)), Is.EqualTo(0f));
        }

        [Test]
        public void TheMatchValueIsAlwaysZeroOrOne()
        {
            // match is a 0..1 lerp; anything between would be a silent behaviour change from
            // the uGUI component, which only ever writes the endpoints.
            foreach (var size in new[] { new Vector2(1080f, 1920f), new(1920f, 1080f), new(1000f, 1000f), new(1080f, 2400f) })
            {
                var match = PanelScaleRatio.CalculateMatch(size);
                Assert.That(match, Is.EqualTo(0f).Or.EqualTo(1f), $"for {size}");
            }
        }

        #region Applying it to a PanelSettings

        [Test]
        public void Apply_SetsTheTwoModesThatMakeMatchMeanAnything()
        {
            var settings = ScriptableObject.CreateInstance<PanelSettings>();

            try
            {
                settings.scaleMode       = PanelScaleMode.ConstantPixelSize;
                settings.screenMatchMode = PanelScreenMatchMode.Expand;

                PanelScaleRatio.Apply(settings, new(1080f, 1920f));

                Assert.That(settings.scaleMode, Is.EqualTo(PanelScaleMode.ScaleWithScreenSize));
                Assert.That(settings.screenMatchMode, Is.EqualTo(PanelScreenMatchMode.MatchWidthOrHeight));
                Assert.That(settings.match, Is.EqualTo(1f));
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void Apply_WritesTheMatchForTheSizeItIsGiven_NotTheLiveScreen()
        {
            var settings = ScriptableObject.CreateInstance<PanelSettings>();

            try
            {
                PanelScaleRatio.Apply(settings, new(1080f, 2400f));

                Assert.That(settings.match, Is.EqualTo(0f));
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void Apply_ToNullSettings_DoesNothingRatherThanThrowing()
        {
            // A UIDocument with no PanelSettings assigned is a wiring mistake that is already
            // reported elsewhere; throwing out of Awake on top of it helps nobody.
            Assert.DoesNotThrow(() => PanelScaleRatio.Apply(null, new(1080f, 1920f)));
        }

        [Test]
        public void CloneSettings_ProducesASeparateInstance()
        {
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            settings.name = "Original";

            PanelSettings clone = null;

            try
            {
                clone = PanelScaleRatio.CloneSettings(settings);

                Assert.That(clone, Is.Not.Null);
                Assert.That(clone, Is.Not.SameAs(settings));
                Assert.That(clone.name, Does.Contain("Original"));
            }
            finally
            {
                if (clone != null) Object.DestroyImmediate(clone);
                Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void WritingToTheClone_LeavesTheOriginalAssetAlone()
        {
            // The whole reason cloning exists: PanelSettings is a shared project asset, and
            // a runtime write to it is a source-control diff, not a runtime tweak.
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            settings.scaleMode = PanelScaleMode.ConstantPixelSize;
            settings.match     = 0.25f;

            PanelSettings clone = null;

            try
            {
                clone = PanelScaleRatio.CloneSettings(settings);
                PanelScaleRatio.Apply(clone, new(1080f, 1920f));

                Assert.That(clone.match, Is.EqualTo(1f), "the clone must be updated");
                Assert.That(settings.match, Is.EqualTo(0.25f), "the original must not be");
                Assert.That(settings.scaleMode, Is.EqualTo(PanelScaleMode.ConstantPixelSize), "the original must not be");
            }
            finally
            {
                if (clone != null) Object.DestroyImmediate(clone);
                Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void CloneSettings_OfNull_ReturnsNullRatherThanThrowing()
        {
            Assert.That(PanelScaleRatio.CloneSettings(null), Is.Null);
        }

        #endregion
    }
}

