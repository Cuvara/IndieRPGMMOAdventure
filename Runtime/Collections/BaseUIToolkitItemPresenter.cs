namespace Cuvara.UIToolkit.Collections
{
    using System;
    using UnityEngine.UIElements;

    /// <summary>
    /// The UI Toolkit counterpart of the host framework's item-presenter contract.
    /// </summary>
    /// <remarks>
    /// Mirrors the existing MVP contract rather than inventing one, but it cannot reuse it:
    /// the host framework's item-presenter contract hands over a marker whose only concrete
    /// form in that codebase is a <c>MonoBehaviour</c>, and
    /// the MonoBehaviour-based item presenter this replaces is declared
    /// <c>where TView : MonoBehaviour</c>. A <see cref="VisualElement"/> is not one and
    /// never can be.
    /// </remarks>
    public interface IUIToolkitItemPresenter
    {
        void SetView(IUIToolkitItemView viewInstance);

        /// <summary>Show/hide the row.</summary>
        void SetActiveView(bool value);
    }

    /// <summary>Typed half of <see cref="IUIToolkitItemPresenter"/>, matching the host framework's typed item-presenter contract.</summary>
    public interface IUIToolkitItemPresenter<in TView, in TModel>
    {
        void SetView(TView viewInstance);

        void BindData(TModel param);
    }

    /// <summary>Base presenter for a row view.</summary>
    public abstract class BaseUIToolkitItemPresenter<TView> : IUIToolkitItemPresenter where TView : IUIToolkitItemView
    {
        public TView View { get; private set; }

        public virtual void SetView(TView viewInstance)
        {
            this.View = viewInstance;
        }

        public void SetView(IUIToolkitItemView viewInstance)
        {
            if (viewInstance is not TView typed)
            {
                throw new ArgumentException(
                    $"{this.GetType().Name} expects a {typeof(TView).Name} but was handed a {viewInstance?.GetType().Name ?? "null"}.",
                    nameof(viewInstance));
            }

            this.SetView(typed);
        }

        /// <remarks>
        /// <c>display</c>, not <c>visibility</c>: the host framework's uGUI equivalent
        /// calls <c>gameObject.SetActive</c>, which takes the row out of layout as well as out of
        /// sight. <c>visibility: hidden</c> would leave a hole the size of the row.
        /// </remarks>
        public virtual void SetActiveView(bool value)
        {
            if (this.View != null) this.View.Root.style.display = value ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }

    /// <summary>Base presenter for a row view bound to a model. Mirrors the host framework's typed item-presenter base.</summary>
    public abstract class BaseUIToolkitItemPresenter<TView, TModel> : BaseUIToolkitItemPresenter<TView>, IUIToolkitItemPresenter<TView, TModel>, IDisposable
        where TView : IUIToolkitItemView
    {
        /// <summary>Called once, right after the view is set and before the first bind.</summary>
        public virtual void OnViewReady()
        {
        }

        public abstract void BindData(TModel param);

        /// <remarks>
        /// Unlike the host framework's virtualized-list adapters — which call <c>Dispose()</c>
        /// on every rebind and then keep using the presenter — the UI Toolkit adapters call
        /// this exactly once, when the element the presenter belongs to is destroyed.
        /// Rebinding runs <see cref="BindData"/> and nothing else. Release per-bind
        /// subscriptions at the top of <see cref="BindData"/>, not here.
        /// </remarks>
        public virtual void Dispose()
        {
        }
    }
}
