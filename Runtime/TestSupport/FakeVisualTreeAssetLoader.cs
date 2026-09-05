namespace Cuvara.UIToolkit.TestSupport
{
    using System;
    using System.Collections.Generic;
    using Cuvara.UIToolkit.Core;
    using Cysharp.Threading.Tasks;
    using UnityEngine.UIElements;

    /// <summary>
    /// A dictionary-backed <see cref="IVisualTreeAssetLoader"/> for tests.
    /// </summary>
    /// <remarks>
    /// <para>Register assets with <see cref="Add"/>, then hand this to whatever needs a
    /// loader. <see cref="FailFor"/> makes a key throw on next load — so cancel-during-load
    /// is testable — and <see cref="DelayFrames"/> inserts a multi-frame pause so a test can
    /// observe the window between "load started" and "load finished".</para>
    ///
    /// <para><see cref="LoadCount"/> is what proves the cache works: push, pop, push the same
    /// screen and assert the loader was called once.</para>
    /// </remarks>
    public sealed class FakeVisualTreeAssetLoader : IVisualTreeAssetLoader
    {
        private readonly Dictionary<string, VisualTreeAsset> assets = new();
        private readonly HashSet<string> failKeys = new();
        private readonly Dictionary<string, int> delayKeys = new();

        /// <summary>How many times <see cref="LoadAsync"/> was called.</summary>
        public int LoadCount { get; private set; }

        /// <summary>Registers <paramref name="asset"/> under <paramref name="key"/>.</summary>
        public void Add(string key, VisualTreeAsset asset)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            this.assets[key] = asset ?? throw new ArgumentNullException(nameof(asset));
        }

        /// <summary>Makes the next load of <paramref name="key"/> throw.</summary>
        public void FailFor(string key)
        {
            this.failKeys.Add(key ?? throw new ArgumentNullException(nameof(key)));
        }

        /// <summary>Makes the next load of <paramref name="key"/> wait <paramref name="frames"/> frames.</summary>
        public void DelayFrames(string key, int frames)
        {
            this.delayKeys[key ?? throw new ArgumentNullException(nameof(key))] = frames;
        }

        public async UniTask<VisualTreeAsset> LoadAsync(string key)
        {
            ++this.LoadCount;

            if (this.failKeys.Remove(key))
                throw new KeyNotFoundException($"FakeVisualTreeAssetLoader: deliberately failing for '{key}'.");

            if (this.delayKeys.Remove(key, out var frames))
                await UniTask.DelayFrame(frames);

            if (this.assets.TryGetValue(key, out var asset))
                return asset;

            throw new KeyNotFoundException($"FakeVisualTreeAssetLoader: no asset registered for '{key}'.");
        }
    }
}
