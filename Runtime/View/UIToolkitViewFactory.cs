namespace Cuvara.UIToolkit.View
{
    using System;
    using System.Linq;
    using System.Reflection;
    using Cuvara.UIToolkit.Core;
    using Cysharp.Threading.Tasks;
    using UnityEngine.UIElements;

    /// <summary>
    /// Builds an <see cref="IUIToolkitView"/> from the <see cref="VisualTreeAsset"/> it was
    /// authored against — either one you already hold, or one loaded by key.
    /// </summary>
    /// <remarks>
    /// <para>The static <see cref="Create(Type, VisualTreeAsset)"/> is the whole of the
    /// tricky part — turning a <c>Type</c> and an asset into a live view — and it is
    /// static and public so it can be exercised by a test with no panel, no container, no
    /// loader and no <c>UIDocument</c> in sight.</para>
    ///
    /// <para><c>Activator</c>-style construction rather than a DI container, deliberately:
    /// a UI Toolkit view is the counterpart of a uGUI view prefab, and a prefab is never
    /// injected either. Everything a view needs arrives from whatever drives it. Keeping
    /// the view container-free is also what keeps this callable from a test.</para>
    /// </remarks>
    public sealed class UIToolkitViewFactory
    {
        private readonly IVisualTreeAssetLoader loader;

        /// <summary>Builds views by loading their UXML through <paramref name="loader"/>.</summary>
        public UIToolkitViewFactory(IVisualTreeAssetLoader loader)
        {
            this.loader = loader ?? throw new ArgumentNullException(nameof(loader));
        }

        /// <summary>Loads the UXML under <paramref name="key"/> and constructs <typeparamref name="TView"/> from it.</summary>
        public async UniTask<TView> CreateAsync<TView>(string key) where TView : IUIToolkitView
        {
            return (TView)await this.CreateAsync(typeof(TView), key);
        }

        /// <summary>The non-generic form, for a type only known at runtime.</summary>
        public async UniTask<IUIToolkitView> CreateAsync(Type viewType, string key)
        {
            if (viewType == null) throw new ArgumentNullException(nameof(viewType));
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("A view key cannot be null or empty.", nameof(key));

            var visualTreeAsset = await this.loader.LoadAsync(key);

            if (visualTreeAsset == null)
            {
                // A loader that answers null rather than throwing is a real shape — a
                // dictionary-backed one, say. Name the key here, because the alternative is
                // an ArgumentNullException out of the constructor that says only
                // "visualTreeAsset".
                throw new InvalidOperationException($"The loader returned no {nameof(VisualTreeAsset)} for key '{key}'.");
            }

            return Create(viewType, visualTreeAsset);
        }

        /// <summary>
        /// Constructs <paramref name="viewType"/> by calling its
        /// <c>(VisualTreeAsset)</c> constructor.
        /// </summary>
        /// <exception cref="ArgumentException">
        /// <paramref name="viewType"/> is not an <see cref="IUIToolkitView"/>, is abstract,
        /// or has no public constructor taking a single <see cref="VisualTreeAsset"/>.
        /// </exception>
        public static IUIToolkitView Create(Type viewType, VisualTreeAsset visualTreeAsset)
        {
            if (viewType == null) throw new ArgumentNullException(nameof(viewType));
            if (visualTreeAsset == null) throw new ArgumentNullException(nameof(visualTreeAsset));

            if (!typeof(IUIToolkitView).IsAssignableFrom(viewType))
            {
                throw new ArgumentException($"{viewType.Name} is not an {nameof(IUIToolkitView)}; this factory cannot build it.", nameof(viewType));
            }

            if (viewType.IsAbstract)
            {
                throw new ArgumentException($"{viewType.Name} is abstract and cannot be constructed. A screen's view type must be concrete.", nameof(viewType));
            }

            var constructor = viewType.GetConstructor(new[] { typeof(VisualTreeAsset) });

            if (constructor == null)
            {
                // Named explicitly, because the alternative — a MissingMethodException out
                // of Activator — says only "no matching constructor" and leaves the reader
                // to work out which constructor was wanted.
                var found = string.Join(", ", viewType.GetConstructors().Select(Describe));

                throw new ArgumentException(
                    $"{viewType.Name} has no public constructor taking a single {nameof(VisualTreeAsset)}, which is how this factory "
                    + $"builds a view. Found: {(found.Length == 0 ? "none" : found)}.",
                    nameof(viewType));
            }

            return (IUIToolkitView)constructor.Invoke(new object[] { visualTreeAsset });
        }

        /// <summary>Generic convenience over <see cref="Create(Type, VisualTreeAsset)"/>.</summary>
        public static TView Create<TView>(VisualTreeAsset visualTreeAsset) where TView : IUIToolkitView
        {
            return (TView)Create(typeof(TView), visualTreeAsset);
        }

        private static string Describe(ConstructorInfo constructor)
        {
            return $"({string.Join(", ", constructor.GetParameters().Select(parameter => parameter.ParameterType.Name))})";
        }
    }
}
