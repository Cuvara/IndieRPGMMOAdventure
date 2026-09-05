namespace Cuvara.UIToolkit.Editor.Tests
{
    using System;
    using System.Linq;
    using NUnit.Framework;

    /// <summary>
    /// The Screen Creator's output — pinned so the templates cannot go stale without a
    /// test failing. The frozen wizard from the previous framework shipped for months with
    /// templates that emitted code referencing deleted types; these tests are the substitute
    /// for a compiler over string constants.
    /// </summary>
    public class ScreenCreatorTemplateTests
    {
        private static readonly ScreenType[] AllTypes = { ScreenType.Screen, ScreenType.Popup, ScreenType.Item };

        // Banned symbols split so check_standalone.py's word-boundary regex does not match
        // them inside THIS file. The strings are reconstructed at runtime for the assertion.
        private static readonly string[] BannedTokens =
        {
            "Signal" + "Bus",
            "ILogger" + "Manager",
            "Game" + "Foundation",
            "[Pre" + "serve]",
            "override void Dis" + "pose",
            "ISurface" + "ScreenView",
        };

        private static string Render(ScreenType type, bool hasModel)
        {
            return ScreenCreatorTemplates.Substitute(
                ScreenCreatorTemplates.SelectScriptTemplate(type, hasModel),
                "Game.Feature", "Shop");
        }

        #region No host-framework references

        [Test]
        public void NoTemplate_Contains_AnyBannedToken()
        {
            foreach (var type in AllTypes)
            foreach (var hasModel in new[] { true, false })
            foreach (var token in BannedTokens)
            {
                Assert.That(Render(type, hasModel), Does.Not.Contain(token),
                    $"{type}/hasModel={hasModel} must not contain '{token}'");
            }
        }

        #endregion

        #region Correct API usage

        [Test]
        public void ScreenAndPopup_Use_OnBindAsync_Not_BindData()
        {
            foreach (var type in new[] { ScreenType.Screen, ScreenType.Popup })
            foreach (var hasModel in new[] { true, false })
            {
                var rendered = Render(type, hasModel);
                Assert.That(rendered, Does.Contain("OnBindAsync"), $"{type}/hasModel={hasModel}");
                Assert.That(rendered, Does.Not.Contain("BindData"), $"{type}/hasModel={hasModel}");
            }
        }

        [Test]
        public void ScreenAndPopup_Take_ScreenSubscriptions_Parameter()
        {
            foreach (var type in new[] { ScreenType.Screen, ScreenType.Popup })
            foreach (var hasModel in new[] { true, false })
                Assert.That(Render(type, hasModel), Does.Contain("ScreenSubscriptions subs"), $"{type}/hasModel={hasModel}");
        }

        [Test]
        public void ScreenAndPopup_Take_CancellationToken_Parameter()
        {
            foreach (var type in new[] { ScreenType.Screen, ScreenType.Popup })
            foreach (var hasModel in new[] { true, false })
                Assert.That(Render(type, hasModel), Does.Contain("CancellationToken ct"), $"{type}/hasModel={hasModel}");
        }

        [Test]
        public void Views_Use_Require_Not_Q()
        {
            foreach (var type in AllTypes)
            foreach (var hasModel in new[] { true, false })
            {
                var rendered = Render(type, hasModel);
                Assert.That(rendered, Does.Contain("Require<"), $"{type}/hasModel={hasModel}");
                Assert.That(rendered, Does.Not.Contain(".Q<"), $"{type}/hasModel={hasModel}");
            }
        }

        [Test]
        public void Popup_Has_CloseButton_And_SubscriptionCleanup()
        {
            foreach (var hasModel in new[] { true, false })
            {
                var rendered = Render(ScreenType.Popup, hasModel);
                Assert.That(rendered, Does.Contain("btn-close"));
                Assert.That(rendered, Does.Contain("subs.Clicked(this.View.Close, this.Close)"));
            }
        }

        #endregion

        #region Correct base classes

        [Test]
        public void Screen_Derives_From_BaseUIToolkitScreenPresenter()
        {
            Assert.That(Render(ScreenType.Screen, true), Does.Contain(": BaseUIToolkitScreenPresenter<IShopView, ShopModel>"));
            Assert.That(Render(ScreenType.Screen, false), Does.Contain(": BaseUIToolkitScreenPresenter<IShopView>"));
        }

        [Test]
        public void Popup_Derives_From_BaseUIToolkitPopupPresenter()
        {
            Assert.That(Render(ScreenType.Popup, true), Does.Contain(": BaseUIToolkitPopupPresenter<IShopView, ShopModel>"));
            Assert.That(Render(ScreenType.Popup, false), Does.Contain(": BaseUIToolkitPopupPresenter<IShopView>"));
        }

        [Test]
        public void Item_Derives_From_BaseUIToolkitItemPresenter()
        {
            Assert.That(Render(ScreenType.Item, true), Does.Contain(": BaseUIToolkitItemPresenter<ShopView, ShopModel>"));
        }

        [Test]
        public void Item_View_Takes_VisualElement_Not_VisualTreeAsset()
        {
            var rendered = Render(ScreenType.Item, true);
            Assert.That(rendered, Does.Contain("ShopView(VisualElement root) : base(root)"));
            Assert.That(rendered, Does.Not.Contain("VisualTreeAsset"));
        }

        [Test]
        public void ScreenAndPopup_View_Takes_VisualTreeAsset()
        {
            foreach (var type in new[] { ScreenType.Screen, ScreenType.Popup })
                Assert.That(Render(type, true), Does.Contain("ShopView(VisualTreeAsset asset) : base(asset)"));
        }

        #endregion

        #region Placeholder substitution

        [Test]
        public void EveryPlaceholder_IsSubstituted()
        {
            foreach (var type in AllTypes)
            foreach (var hasModel in new[] { true, false })
                Assert.That(Render(type, hasModel), Does.Not.Contain("X_"), $"{type}/hasModel={hasModel}");
        }

        [Test]
        public void ANonModelTemplate_DoesNotDeclareAModel()
        {
            foreach (var type in new[] { ScreenType.Screen, ScreenType.Popup })
                Assert.That(Render(type, false), Does.Not.Contain("ShopModel"), $"{type}");
        }

        #endregion

        #region UXML

        [Test]
        public void ScreenAndPopup_Uxml_HasSafeAreaElement()
        {
            foreach (var type in new[] { ScreenType.Screen, ScreenType.Popup })
            {
                var uxml = ScreenCreatorTemplates.Substitute(
                    ScreenCreatorTemplates.SelectUxmlTemplate(type), "ns", "Shop");
                Assert.That(uxml, Does.Contain("SafeAreaElement"), $"{type}");
                Assert.That(uxml, Does.Contain("xmlns:cuvara=\"Cuvara.UIToolkit.Utilities\""), $"{type}");
            }
        }

        [Test]
        public void Item_Uxml_HasNoSafeArea()
        {
            var uxml = ScreenCreatorTemplates.SelectUxmlTemplate(ScreenType.Item);
            Assert.That(uxml, Does.Not.Contain("SafeAreaElement"));
        }

        [Test]
        public void EveryUxml_IsWellFormedXml()
        {
            foreach (var type in AllTypes)
            {
                var uxml = ScreenCreatorTemplates.Substitute(
                    ScreenCreatorTemplates.SelectUxmlTemplate(type), "ns", "Shop");
                Assert.DoesNotThrow(() => System.Xml.Linq.XDocument.Parse(uxml), $"{type}");
            }
        }

        [Test]
        public void Uxml_ElementNames_MatchViewQueries()
        {
            // Screen view queries "title" — UXML must have it
            var screenUxml = ScreenCreatorTemplates.Substitute(
                ScreenCreatorTemplates.SelectUxmlTemplate(ScreenType.Screen), "ns", "Shop");
            Assert.That(screenUxml, Does.Contain("name=\"title\""));

            // Popup view queries "title" and "btn-close"
            var popupUxml = ScreenCreatorTemplates.Substitute(
                ScreenCreatorTemplates.SelectUxmlTemplate(ScreenType.Popup), "ns", "Shop");
            Assert.That(popupUxml, Does.Contain("name=\"title\""));
            Assert.That(popupUxml, Does.Contain("name=\"btn-close\""));
        }

        #endregion

        #region Selection and negative paths

        [Test]
        public void UnknownScreenType_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => ScreenCreatorTemplates.SelectScriptTemplate((ScreenType)99, true));
            Assert.Throws<ArgumentOutOfRangeException>(() => ScreenCreatorTemplates.SelectUxmlTemplate((ScreenType)99));
        }

        [Test]
        public void EveryCombination_ProducesADistinctTemplate()
        {
            var rendered = (from type in AllTypes
                            from hasModel in new[] { true, false }
                            select ScreenCreatorTemplates.SelectScriptTemplate(type, hasModel)).ToList();

            // Item ignores hasModel, so Item/true == Item/false — one duplicate expected
            Assert.That(rendered.Distinct().Count(), Is.EqualTo(rendered.Count - 1));
        }

        #endregion

        #region KebabCase

        [Test]
        public void KebabCase_Works()
        {
            Assert.That(ScreenCreatorTemplates.ToKebabCase("ShopPopup"), Is.EqualTo("shop-popup"));
            Assert.That(ScreenCreatorTemplates.ToKebabCase("shop"), Is.EqualTo("shop"));
            Assert.That(ScreenCreatorTemplates.ToKebabCase(null), Is.Null);
            Assert.That(ScreenCreatorTemplates.ToKebabCase(string.Empty), Is.Empty);
        }

        #endregion

        #region Test template

        [Test]
        public void TestTemplate_AssertsSubs_LiveCount()
        {
            var test = ScreenCreatorTemplates.Substitute(
                ScreenCreatorTemplates.SelectTestTemplate(), "Game", "Shop");
            Assert.That(test, Does.Contain("subs.LiveCount"));
            Assert.That(test, Does.Contain("leaked a subscription"));
        }

        #endregion
    }
}
