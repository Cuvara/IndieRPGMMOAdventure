namespace Cuvara.UIToolkit.Flow.Tests
{
    using System.Collections;
    using Cuvara.UIToolkit.Core;
    using Cuvara.UIToolkit.Flow;
    using Cuvara.UIToolkit.Managers;
    using Cuvara.UIToolkit.VContainer;
    using Cysharp.Threading.Tasks;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;
    // global:: because this file's own namespace contains a 'VContainer' segment
    // (Cuvara.UIToolkit.VContainer), which otherwise shadows the real one.
    using global::VContainer;
    using global::VContainer.Unity;
    using Object = UnityEngine.Object;

    /// <summary>
    /// <c>RegisterScreenFlow()</c> against a real container and a real <c>RootUIDocument</c>.
    /// </summary>
    /// <remarks>
    /// <para><b>This file exists because of a defect none of the other tests could see.</b>
    /// <c>ScreenNavigator</c> takes <c>ViewLayers</c> in its constructor, and
    /// <c>RegisterScreenFlow()</c> did not register it — so the extension produced a container
    /// that could not resolve the navigator at all, and every screen in a real project was dead
    /// with "No such registration of type: ViewLayers" as the only clue.</para>
    ///
    /// <para>Sixty-five navigator tests passed throughout. Every one of them constructs
    /// <c>ScreenNavigator</c> directly with layers built by hand, which is exactly what makes
    /// them fast and headless — and exactly what makes them blind to the wiring. The defect was
    /// found by putting the sample in a scene and pressing Play.</para>
    ///
    /// <para>So the rule this file encodes: <b>the composition root needs a test that composes.</b>
    /// A registration extension is code, and code with no caller in a test is code nobody has
    /// run.</para>
    /// </remarks>
    public class ScreenFlowRegistrationTests
    {
        private sealed class TestScope : LifetimeScope
        {
            public System.Action<IContainerBuilder> Installer;

            protected override void Configure(IContainerBuilder builder) { this.Installer?.Invoke(builder); }
        }

        private sealed class EmptyLoader : IVisualTreeAssetLoader
        {
            public UniTask<VisualTreeAsset> LoadAsync(string key) => UniTask.FromResult<VisualTreeAsset>(null);
        }

        private GameObject    documentObject, scopeObject;
        private PanelSettings panelSettings;

        [SetUp]
        public void SetUp() { LogAssert.ignoreFailingMessages = true; }

        [TearDown]
        public void TearDown()
        {
            if (this.scopeObject != null) Object.DestroyImmediate(this.scopeObject);
            if (this.documentObject != null) Object.DestroyImmediate(this.documentObject);
            if (this.panelSettings != null) Object.DestroyImmediate(this.panelSettings);

            LogAssert.ignoreFailingMessages = false;
        }

        private IObjectResolver BuildContainer()
        {
            this.panelSettings = ScriptableObject.CreateInstance<PanelSettings>();

            this.documentObject = new GameObject("UI");
            this.documentObject.SetActive(false);

            var document = this.documentObject.AddComponent<UIDocument>();
            document.panelSettings   = this.panelSettings;
            document.visualTreeAsset = LoadUxml("Packages/com.cuvara.uitoolkit/Runtime/Managers/RootUIDocument.uxml");
            this.documentObject.AddComponent<RootUIDocument>();
            this.documentObject.SetActive(true);

            this.scopeObject = new GameObject("Scope");
            this.scopeObject.SetActive(false);

            var scope = this.scopeObject.AddComponent<TestScope>();

            scope.Installer = builder =>
            {
                builder.RegisterUIToolkit();
                builder.RegisterInstance<IVisualTreeAssetLoader>(new EmptyLoader());
                builder.RegisterScreenFlow();
            };

            this.scopeObject.SetActive(true);

            return scope.Container;
        }

        [UnityTest]
        public IEnumerator RegisterScreenFlowProducesAResolvableNavigator()
        {
            // The regression guard. Before ViewLayers was registered this threw
            // VContainerException and no other test in the package noticed.
            var container = this.BuildContainer();

            yield return null;

            var navigator = container.Resolve<IScreenNavigator>();

            Assert.That(navigator, Is.Not.Null);
            Assert.That(navigator.Depth, Is.Zero);
        }

        [UnityTest]
        public IEnumerator EveryTypeTheFlowNeedsIsRegistered()
        {
            // Resolving each dependency by name rather than relying on the navigator's
            // constructor to surface them one at a time — the failure mode being fixed here
            // reports only the FIRST missing registration, so a second one would hide behind it.
            var container = this.BuildContainer();

            yield return null;

            Assert.That(container.Resolve<ScreenRegistry>(), Is.Not.Null);
            Assert.That(container.Resolve<IScreenScopeFactory>(), Is.Not.Null);
            Assert.That(container.Resolve<System.Func<ViewLayers>>()().Screen, Is.Not.Null, "the layers must come from the scene's RootUIDocument, read late");
            Assert.That(container.Resolve<RootUIDocument>(), Is.Not.Null);
        }

        [UnityTest]
        public IEnumerator RegisterScreenRecordsTheScreenInTheRegistry()
        {
            this.panelSettings = ScriptableObject.CreateInstance<PanelSettings>();

            this.documentObject = new GameObject("UI");
            this.documentObject.SetActive(false);
            var document = this.documentObject.AddComponent<UIDocument>();
            document.panelSettings   = this.panelSettings;
            document.visualTreeAsset = LoadUxml("Packages/com.cuvara.uitoolkit/Runtime/Managers/RootUIDocument.uxml");
            this.documentObject.AddComponent<RootUIDocument>();
            this.documentObject.SetActive(true);

            this.scopeObject = new GameObject("Scope");
            this.scopeObject.SetActive(false);
            var scope = this.scopeObject.AddComponent<TestScope>();

            scope.Installer = builder =>
            {
                builder.RegisterUIToolkit();
                builder.RegisterInstance<IVisualTreeAssetLoader>(new EmptyLoader());
                builder.RegisterScreenFlow();
                builder.RegisterScreen<RegisteredPresenter, TestScreenView>("SomeKey");
            };

            this.scopeObject.SetActive(true);

            yield return null;

            var registry = scope.Container.Resolve<ScreenRegistry>();

            Assert.That(registry.TryGet(typeof(RegisteredPresenter), out var registration), Is.True,
                "RegisterScreen's build callback did not reach the registry");
            Assert.That(registration.AssetKey, Is.EqualTo("SomeKey"));
            Assert.That(registration.ViewType, Is.EqualTo(typeof(TestScreenView)));
        }

        internal sealed class RegisteredPresenter : BaseUIToolkitScreenPresenter<ITestScreenView>
        {
            protected override UniTask OnBindAsync(ScreenSubscriptions subscriptions, System.Threading.CancellationToken cancellationToken)
            {
                return UniTask.CompletedTask;
            }
        }

        private static VisualTreeAsset LoadUxml(string path)
        {
            #if UNITY_EDITOR
            var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(path);
            Assert.That(asset, Is.Not.Null, $"Could not load {path}.");
            return asset;
            #else
            Assert.Ignore("Loads its UXML through the AssetDatabase; Editor only.");
            return null;
            #endif
        }
    }
}
