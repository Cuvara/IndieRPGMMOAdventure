namespace Cuvara.UIToolkit.Flow
{
    using System;

    /// <summary>One screen's container scope. Disposing it ends the screen.</summary>
    /// <remarks>
    /// <para>A screen's lifetime IS this object's lifetime. The presenter, the view, anything the
    /// screen registered and any screen-scoped services all go when this does — one concept, one
    /// <c>Dispose</c>, nothing for a screen author to remember.</para>
    ///
    /// <para>This is also the only place in the flow where <c>Dispose</c> appears, which is the
    /// point: in the framework this replaces, closing and suspending both disposed an object that
    /// then kept living, and every screen carried unregister-then-register boilerplate as a
    /// result. Here <c>Dispose</c> means end of life and nothing else.</para>
    /// </remarks>
    public interface IScreenScope : IDisposable
    {
        /// <summary>Builds or resolves <paramref name="type"/> from this screen's scope.</summary>
        object Resolve(Type type);
    }

    /// <summary>Creates a container scope per screen.</summary>
    /// <remarks>
    /// <para><b>Why the navigator talks to this instead of to a container directly.</b> The
    /// navigator lives in the package's core assembly, which references no DI framework at all —
    /// the VContainer integration is a separate, optional assembly. Naming a container type here
    /// would drag that dependency into the core and make it mandatory.</para>
    ///
    /// <para>The larger payoff is testing. A test implements this with a dictionary and a counter
    /// in about fifteen lines, so the navigator's whole stack, suspend/resume and — crucially —
    /// scope-disposal behaviour can be exercised with no container, no scene and no panel. A
    /// navigator that could only be tested inside a real container would be a navigator whose
    /// disposal guarantees were argued rather than asserted.</para>
    /// </remarks>
    public interface IScreenScopeFactory
    {
        /// <summary>Creates the scope a single screen will live in.</summary>
        IScreenScope CreateScreenScope();
    }
}
