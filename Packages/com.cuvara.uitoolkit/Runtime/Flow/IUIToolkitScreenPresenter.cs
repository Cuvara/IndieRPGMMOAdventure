namespace Cuvara.UIToolkit.Flow
{
    using Cuvara.UIToolkit.Core;

    /// <summary>
    /// What the navigator knows about a screen. Deliberately almost nothing.
    /// </summary>
    /// <remarks>
    /// <para>Named with the <c>UIToolkit</c> infix rather than the shorter name it would
    /// otherwise want, because the standalone gate bans the obvious identifier by word-boundary
    /// regex — and that awkwardness is a feature: a type with the shorter name here would be
    /// genuinely confusable with the one in the framework this package replaces.</para>
    ///
    /// <para><b>The navigator drives screens through this and nothing else.</b> It never sees a
    /// concrete presenter type, never sees the model, and never sees the view except as an
    /// <see cref="IUIToolkitView"/> it can parent. Everything that actually opens, binds and
    /// closes is on the base class as internal members, so a host cannot call them out of order
    /// and a screen author never sees them at all.</para>
    /// </remarks>
    public interface IUIToolkitScreenPresenter
    {
        /// <summary>Where this screen is in its lifecycle.</summary>
        ScreenLifecycleState State { get; }

        /// <summary>How this screen behaves in the stack. Set from its registration.</summary>
        ScreenOptions Options { get; }

        /// <summary>The view, once constructed. Null before that.</summary>
        IUIToolkitView View { get; }
    }

    /// <summary>A screen that is opened with a model.</summary>
    /// <remarks>
    /// Separate from the non-generic form rather than folded into it with a nullable model, so
    /// that <c>PushAsync&lt;TPresenter&gt;()</c> and
    /// <c>PushAsync&lt;TPresenter, TModel&gt;(model)</c> are distinguished by the compiler.
    /// Pushing a model-taking screen without its model is then not something you can write, as
    /// opposed to something that fails at runtime with a null.
    /// </remarks>
    /// <typeparam name="TModel">The screen's input. Plain data; the flow never inspects it.</typeparam>
    public interface IUIToolkitScreenPresenter<in TModel> : IUIToolkitScreenPresenter
    {
    }
}
