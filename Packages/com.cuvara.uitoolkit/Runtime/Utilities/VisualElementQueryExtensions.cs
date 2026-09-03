namespace Cuvara.UIToolkit.Utilities
{
    using System;
    using UnityEngine.UIElements;

    /// <summary>
    /// Query extensions that fail loudly. <c>Q&lt;T&gt;</c> answers "is it there?";
    /// <see cref="Require{T}"/> states "it must be", and a UXML edit that breaks the
    /// contract surfaces as one precise exception at bind time instead of a
    /// <c>NullReferenceException</c> three frames later with the query long off the stack.
    /// </summary>
    /// <remarks>
    /// This is also the primitive the UXML codegen emits: every generated
    /// <c>AssignQueries</c> resolves its elements through <see cref="Require{T}"/>, so a
    /// generated binding and a hand-written one fail with the same message. See
    /// <c>Documentation~/UXML-CODEGEN.md</c>.
    /// </remarks>
    public static class VisualElementQueryExtensions
    {
        /// <summary>
        /// <c>Q&lt;T&gt;(name)</c> that throws instead of returning null.
        /// </summary>
        /// <param name="root">The element to query under.</param>
        /// <param name="name">The UXML <c>name</c> attribute value to find.</param>
        /// <returns>The element — never null.</returns>
        /// <exception cref="InvalidOperationException">No descendant of
        /// <paramref name="root"/> has that name AND that type. A right-named element of
        /// the wrong type throws too, because <c>Q&lt;T&gt;</c> filters on both.</exception>
        public static T Require<T>(this VisualElement root, string name)
            where T : VisualElement
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("Element name must be non-empty.", nameof(name));

            var element = root.Q<T>(name);
            if (element == null)
            {
                var rootName = string.IsNullOrEmpty(root.name) ? $"<unnamed {root.GetType().Name}>" : root.name;
                throw new InvalidOperationException(
                    $"Required element '{name}' of type {typeof(T).Name} was not found under root '{rootName}'. " +
                    $"Check the UXML: an element of that type must carry name=\"{name}\" (the 'name' attribute, exact and case-sensitive).");
            }

            return element;
        }
    }
}
