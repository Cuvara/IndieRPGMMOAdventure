namespace Scripts.UI
{
    using System.Collections.Generic;
    using Cuvara.UIToolkit.Core;
    using Cysharp.Threading.Tasks;
    using UnityEngine.AddressableAssets;
    using UnityEngine.ResourceManagement.AsyncOperations;
    using UnityEngine.UIElements;

    /// <summary>
    /// Loads UXML assets via Addressables. The key passed to <see cref="LoadAsync"/>
    /// is the Addressable address — set it to the same string used in
    /// <c>RegisterScreen&lt;T, V&gt;(key)</c>.
    /// </summary>
    public sealed class AddressableAssetLoader : IVisualTreeAssetLoader
    {
        public async UniTask<VisualTreeAsset> LoadAsync(string key)
        {
            var handle = Addressables.LoadAssetAsync<VisualTreeAsset>(key);
            await handle.Task;
            if (handle.Status != AsyncOperationStatus.Succeeded || handle.Result == null)
                throw new KeyNotFoundException(
                    $"Addressables failed for key '{key}'. Status: {handle.Status}. Check the address is set on the UXML asset.");
            return handle.Result;
        }
    }
}
