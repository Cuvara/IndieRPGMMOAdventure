namespace Cuvara.UIToolkit.Collections
{
    using System;
    using System.Linq;
    using System.Reflection;
    using UnityEngine.UIElements;

    /// <summary>
    /// Builds an <see cref="IUIToolkitItemView"/> subclass around a cloned element.
    /// </summary>
    /// <remarks>
    /// The row-level twin of <see cref="View.UIToolkitViewFactory"/>, and split out for the
    /// same reason: the one reflective step in the collection path can then be exercised by
    /// a test with no panel, no container and no <c>ListView</c> in sight.
    ///
    /// <para><c>Activator</c> rather than the DI container, again for the same reason a
    /// screen view is not injected — a row view is the counterpart of a row prefab, and
    /// everything it needs arrives from its presenter, which IS injected.</para>
    /// </remarks>
    public static class UIToolkitItemViewFactory
    {
        /// <summary>Constructs <paramref name="viewType"/> by calling its <c>(VisualElement)</c> constructor.</summary>
        /// <exception cref="ArgumentException">
        /// <paramref name="viewType"/> is not an <see cref="IUIToolkitItemView"/>, is
        /// abstract, or has no public constructor taking a single
        /// <see cref="VisualElement"/>.
        /// </exception>
        public static IUIToolkitItemView Create(Type viewType, VisualElement root)
        {
            if (viewType == null) throw new ArgumentNullException(nameof(viewType));
            if (root == null) throw new ArgumentNullException(nameof(root));

            if (!typeof(IUIToolkitItemView).IsAssignableFrom(viewType))
            {
                throw new ArgumentException($"{viewType.Name} is not an {nameof(IUIToolkitItemView)}; a UI Toolkit collection cannot build it.", nameof(viewType));
            }

            if (viewType.IsAbstract)
            {
                throw new ArgumentException($"{viewType.Name} is abstract and cannot be constructed. A collection's TView must be a concrete view.", nameof(viewType));
            }

            var constructor = viewType.GetConstructor(new[] { typeof(VisualElement) });

            if (constructor == null)
            {
                var found = string.Join(", ", viewType.GetConstructors().Select(Describe));

                throw new ArgumentException(
                    $"{viewType.Name} has no public constructor taking a single {nameof(VisualElement)}, which is how a UI Toolkit "
                    + $"collection builds a row view. Found: {(found.Length == 0 ? "none" : found)}.",
                    nameof(viewType));
            }

            return (IUIToolkitItemView)constructor.Invoke(new object[] { root });
        }

        private static string Describe(ConstructorInfo constructor)
        {
            return $"({string.Join(", ", constructor.GetParameters().Select(parameter => parameter.ParameterType.Name))})";
        }
    }
}
