namespace Cuvara.UIToolkit.ViewModel
{
    using System;
    using System.Collections.Generic;
    using System.Runtime.CompilerServices;
    using UnityEngine.UIElements;

    /// <summary>
    /// The base for a ViewModel a View binds to with runtime data binding: notifying
    /// property setters in one call, via <see cref="Set{T}"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>Notifying is mandatory, not polite.</b> UI Toolkit's binding system has two
    /// ways to notice a data source changed: a notification from
    /// <see cref="INotifyBindablePropertyChanged"/>, or — for a source that does not
    /// implement it — <b>version-polling the source on every UI update</b>. A non-notifying
    /// source therefore silently re-evaluates its bindings per frame, which is exactly the
    /// per-frame work this package's contract ("update on data change, not per frame")
    /// forbids. Deriving from this class and routing every mutable property through
    /// <see cref="Set{T}"/> is what keeps a bound screen change-driven.</para>
    ///
    /// <para><b>Where this sits in the hybrid convention.</b> Runtime data binding is a
    /// View-internal implementation detail behind the same <c>IView</c> interfaces the MVP
    /// flow already uses: a Presenter (or an ECS sink) writes plain C# properties on a
    /// subclass of this, and the View — the only layer that knows UI Toolkit exists —
    /// assigns <c>Root.dataSource</c> and wires elements with <c>SetBinding</c>. Commands,
    /// clicks and navigation stay on <c>ScreenSubscriptions</c>; binding is for values that
    /// change during a screen's life. See <c>Documentation~/HYBRID-DATA-BINDING.md</c>.</para>
    ///
    /// <para><b>A subclass stays plain C#.</b> Nothing here touches a
    /// <c>VisualElement</c>, a panel or a scene — the UIElements reference is the interface
    /// and its event-args struct, both plain types — so a ViewModel's behaviour is testable
    /// with NUnit alone: mutate a property, assert the event and its property name.</para>
    ///
    /// <para>Mark bindable properties with <c>[CreateProperty]</c> (from
    /// <c>Unity.Properties</c>) so the binding system resolves them through a generated
    /// property bag rather than reflection, and use <c>nameof</c> for every binding path so
    /// a rename is a compile error instead of a silently dead binding.</para>
    ///
    /// <para>Not thread-safe, deliberately — the same reasoning as
    /// <c>ScreenSubscriptions</c>: everything that reads these notifications runs on the
    /// main thread, because a <c>VisualElement</c> cannot be touched from anywhere else.</para>
    /// </remarks>
    public abstract class BindableViewModel : INotifyBindablePropertyChanged
    {
        /// <summary>Raised after a property's value actually changed. Lower-case name as
        /// <see cref="INotifyBindablePropertyChanged"/> declares it.</summary>
        public event EventHandler<BindablePropertyChangedEventArgs> propertyChanged;

        /// <summary>
        /// Assigns <paramref name="value"/> to <paramref name="field"/> and raises
        /// <see cref="propertyChanged"/> — only when the value actually changed.
        /// </summary>
        /// <remarks>
        /// Equality is <see cref="EqualityComparer{T}.Default"/>, so a value type compares
        /// by value, a string by content, and a reference type by whatever equality it
        /// declares. Writing an equal value raises nothing — the binding system is never
        /// asked to re-evaluate a binding whose source did not change.
        /// </remarks>
        /// <param name="field">The backing field of the calling property.</param>
        /// <param name="value">The value being assigned.</param>
        /// <param name="property">Filled in by the compiler with the calling property's
        /// name; pass it explicitly only when raising for a property other than the caller.</param>
        /// <returns>True when the value changed (and the event was raised); false when it
        /// was already equal.</returns>
        protected bool Set<T>(ref T field, T value, [CallerMemberName] string property = "")
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;

            field = value;
            this.Notify(property);
            return true;
        }

        /// <summary>
        /// Raises <see cref="propertyChanged"/> unconditionally for <paramref name="property"/>.
        /// </summary>
        /// <remarks>
        /// For the rare property that cannot go through <see cref="Set{T}"/> — a computed
        /// property whose inputs changed, say. Prefer <see cref="Set{T}"/>: an unconditional
        /// raise on an unchanged value is exactly the redundant re-evaluation the guard
        /// exists to prevent.
        /// </remarks>
        protected void Notify([CallerMemberName] string property = "")
        {
            this.propertyChanged?.Invoke(this, new BindablePropertyChangedEventArgs(property));
        }
    }
}
