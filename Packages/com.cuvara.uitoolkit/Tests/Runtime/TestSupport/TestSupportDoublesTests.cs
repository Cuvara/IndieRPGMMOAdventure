namespace Cuvara.UIToolkit.Tests.TestSupport
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using Cuvara.UIToolkit.Core;
    using Cuvara.UIToolkit.Flow;
    using Cuvara.UIToolkit.TestSupport;
    using Cysharp.Threading.Tasks;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;

    /// <summary>
    /// Each shipped test double must prove its own contract, so a consumer trusts it and
    /// never needs to write their own.
    /// </summary>
    public class TestSupportDoublesTests
    {
        #region FakeVisualTreeAssetLoader

        [UnityTest]
        public IEnumerator FakeLoader_ReturnsRegisteredAsset() => UniTask.ToCoroutine(async () =>
        {
            var loader = new FakeVisualTreeAssetLoader();
            var asset  = LoadTestUxml();
            loader.Add("key", asset);

            var result = await loader.LoadAsync("key");

            Assert.That(result, Is.SameAs(asset));
            Assert.That(loader.LoadCount, Is.EqualTo(1));
        });

        [UnityTest]
        public IEnumerator FakeLoader_ThrowsForUnknownKey() => UniTask.ToCoroutine(async () =>
        {
            var loader = new FakeVisualTreeAssetLoader();

            try
            {
                await loader.LoadAsync("missing");
                Assert.Fail("expected KeyNotFoundException");
            }
            catch (KeyNotFoundException)
            {
            }
        });

        [UnityTest]
        public IEnumerator FakeLoader_FailFor_ThrowsOnce() => UniTask.ToCoroutine(async () =>
        {
            var loader = new FakeVisualTreeAssetLoader();
            var asset  = LoadTestUxml();
            loader.Add("key", asset);
            loader.FailFor("key");

            try
            {
                await loader.LoadAsync("key");
                Assert.Fail("expected KeyNotFoundException");
            }
            catch (KeyNotFoundException)
            {
            }

            // Second call should succeed — FailFor is one-shot
            var result = await loader.LoadAsync("key");
            Assert.That(result, Is.SameAs(asset));
        });

        [UnityTest]
        public IEnumerator FakeLoader_LoadCount_IncrementsOnEveryCall() => UniTask.ToCoroutine(async () =>
        {
            var loader = new FakeVisualTreeAssetLoader();
            var asset  = LoadTestUxml();
            loader.Add("key", asset);

            await loader.LoadAsync("key");
            await loader.LoadAsync("key");

            Assert.That(loader.LoadCount, Is.EqualTo(2));
        });

        #endregion

        #region FakeScopeFactory

        [Test]
        public void FakeScopeFactory_CountsCreatedAndDisposed()
        {
            var factory = new FakeScopeFactory();
            factory.Bind<string>(() => "hello");

            Assert.That(factory.Created, Is.Zero);
            Assert.That(factory.Disposed, Is.Zero);

            var scope = factory.CreateScreenScope();
            Assert.That(factory.Created, Is.EqualTo(1));

            var resolved = scope.Resolve(typeof(string));
            Assert.That(resolved, Is.EqualTo("hello"));

            scope.Dispose();
            Assert.That(factory.Disposed, Is.EqualTo(1));
        }

        [Test]
        public void FakeScopeFactory_ResolveAfterDisposeThrows()
        {
            var factory = new FakeScopeFactory();
            factory.Bind<string>(() => "x");

            var scope = factory.CreateScreenScope();
            scope.Dispose();

            Assert.Throws<ObjectDisposedException>(() => scope.Resolve(typeof(string)));
        }

        [Test]
        public void FakeScopeFactory_ResolveUnboundTypeThrows()
        {
            var factory = new FakeScopeFactory();
            var scope   = factory.CreateScreenScope();

            Assert.Throws<InvalidOperationException>(() => scope.Resolve(typeof(int)));
        }

        #endregion

        #region FakeViewLayer and RecordingViewSurface

        [Test]
        public void RecordingViewSurface_RecordsParentHistory()
        {
            var layer1  = new FakeViewLayer();
            var layer2  = new FakeViewLayer();
            var surface = new RecordingViewSurface();

            Assert.That(surface.CurrentParent, Is.Null);
            Assert.That(surface.ReparentCount, Is.Zero);

            surface.SetParent(layer1);
            Assert.That(surface.CurrentParent, Is.SameAs(layer1));
            Assert.That(surface.ReparentCount, Is.EqualTo(1));

            surface.SetParent(layer2);
            Assert.That(surface.CurrentParent, Is.SameAs(layer2));
            Assert.That(surface.ReparentCount, Is.EqualTo(2));
            Assert.That(surface.ParentHistory[0], Is.SameAs(layer1));
            Assert.That(surface.ParentHistory[1], Is.SameAs(layer2));
        }

        #endregion

        #region SpyViewModelSink

        [Test]
        public void SpyViewModelSink_RecordsPushes()
        {
            var spy = new SpyViewModelSink<string>();

            Assert.That(spy.PushCount, Is.Zero);

            spy.Push("first");
            spy.Push("second");

            Assert.That(spy.PushCount, Is.EqualTo(2));
            Assert.That(spy.Values[0], Is.EqualTo("first"));
            Assert.That(spy.Values[1], Is.EqualTo("second"));
            Assert.That(spy.Last, Is.EqualTo("second"));
        }

        [Test]
        public void SpyViewModelSink_ClearResetsHistory()
        {
            var spy = new SpyViewModelSink<int>();
            spy.Push(42);
            spy.Clear();

            Assert.That(spy.PushCount, Is.Zero);
            Assert.That(spy.Last, Is.EqualTo(default(int)));
        }

        #endregion

        #region FakeBackSource

        [Test]
        public void FakeBackSource_NoHandler_ReportsUnhandled()
        {
            var source = new FakeBackSource();

            var handled = source.PressBack();

            Assert.That(handled, Is.False);
            Assert.That(source.PressCount, Is.EqualTo(1));
            Assert.That(source.HandledCount, Is.Zero);
            Assert.That(source.UnhandledCount, Is.EqualTo(1));
        }

        [Test]
        public void FakeBackSource_HandlerReturnsTrue_ReportsHandled()
        {
            var source = new FakeBackSource();
            source.BackHandler = () => true;

            var handled = source.PressBack();

            Assert.That(handled, Is.True);
            Assert.That(source.HandledCount, Is.EqualTo(1));
            Assert.That(source.UnhandledCount, Is.Zero);
        }

        [Test]
        public void FakeBackSource_HandlerReturnsFalse_ReportsUnhandled()
        {
            var source = new FakeBackSource();
            source.BackHandler = () => false;

            var handled = source.PressBack();

            Assert.That(handled, Is.False);
            Assert.That(source.PressCount, Is.EqualTo(1));
            Assert.That(source.HandledCount, Is.Zero);
            Assert.That(source.UnhandledCount, Is.EqualTo(1));
        }

        #endregion

        #region ScreenSubscriptionsAssertions

        [Test]
        public void AssertAllReleased_PassesAfterCleanDispose()
        {
            var subs = new ScreenSubscriptions();
            subs.AddAction(() => { });
            subs.Dispose();

            Assert.DoesNotThrow(() => subs.AssertAllReleased());
        }

        [Test]
        public void AssertAllReleased_ThrowsIfNotDisposed()
        {
            var subs = new ScreenSubscriptions();
            subs.AddAction(() => { });

            Assert.Throws<InvalidOperationException>(() => subs.AssertAllReleased());
        }

        #endregion

        #region Asset cache in UIToolkitViewFactory

        [UnityTest]
        public IEnumerator AssetCache_SecondLoadUsesCache() => UniTask.ToCoroutine(async () =>
        {
            var loader  = new FakeVisualTreeAssetLoader();
            var asset   = LoadTestUxml();
            loader.Add("screen", asset);

            var factory = new Cuvara.UIToolkit.View.UIToolkitViewFactory(loader);

            await factory.CreateAsync<TestCacheView>("screen");
            await factory.CreateAsync<TestCacheView>("screen");

            Assert.That(loader.LoadCount, Is.EqualTo(1), "the loader must only be called once — the second load should come from cache");
        });

        [UnityTest]
        public IEnumerator AssetCache_ClearCacheForcesFreshLoad() => UniTask.ToCoroutine(async () =>
        {
            var loader  = new FakeVisualTreeAssetLoader();
            var asset   = LoadTestUxml();
            loader.Add("screen", asset);

            var factory = new Cuvara.UIToolkit.View.UIToolkitViewFactory(loader);

            await factory.CreateAsync<TestCacheView>("screen");
            factory.ClearCache();
            await factory.CreateAsync<TestCacheView>("screen");

            Assert.That(loader.LoadCount, Is.EqualTo(2), "after ClearCache the loader must be called again");
        });

        internal sealed class TestCacheView : Cuvara.UIToolkit.View.BaseUIToolkitView, IUIToolkitView
        {
            public TestCacheView(VisualTreeAsset asset) : base(asset) { }
        }

        #endregion

        private static VisualTreeAsset LoadTestUxml()
        {
            #if UNITY_EDITOR
            var path  = "Packages/com.cuvara.uitoolkit/Tests/Runtime/TestView.uxml";
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
