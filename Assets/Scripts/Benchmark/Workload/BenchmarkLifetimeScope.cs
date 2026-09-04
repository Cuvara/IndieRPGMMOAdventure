#if CUVARA_DOTS && CUVARA_DOTS_VCONTAINER
namespace Scripts.Benchmark.Workload
{
    using VContainer;
    using VContainer.Unity;

    /// <summary>
    /// The benchmark scene's container: a child of the project root scope that injects the
    /// scene's <see cref="BenchmarkWorkload"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This scope registers almost nothing on purpose. <c>VContainerSettings</c> names
    /// <c>GameLifetimeScope</c> as the project's root scope, so any scene scope — this one
    /// included — is parented to it automatically, and that root already calls the game's
    /// own <c>RegisterDots()</c>: registry, pools, provisioner, <c>IViewAssetProvider</c>,
    /// and the build callback that installs <c>DotsViewBootstrap</c> into the default
    /// world. Registering dots again here would be a second <c>RegisterMessagePipe</c> and
    /// a second view-layer registration — the benchmark must price the wiring the game
    /// ships, not a parallel copy of it.
    /// </para>
    /// <para>
    /// The root also registers networking and Nakama, but registration alone connects to
    /// nothing: <c>NetworkBootstrap</c> is injected only where a scene hosts one, and this
    /// scene deliberately does not — no netcode runs, no server is needed
    /// (<c>docs/DEVICE-BENCHMARK.md</c>). What drives the entities instead is the dots
    /// package's local simulation, installed by <see cref="BenchmarkWorkload"/>.
    /// </para>
    /// <para>
    /// <c>RegisterComponentInHierarchy</c> resolves eagerly and throws when the component
    /// is absent — correct here, because a benchmark scene without its workload is a
    /// mis-authored scene that should fail loudly at load, not measure an empty world.
    /// </para>
    /// </remarks>
    public sealed class BenchmarkLifetimeScope : LifetimeScope
    {
        protected override void Configure(IContainerBuilder builder)
        {
            base.Configure(builder);
            builder.RegisterComponentInHierarchy<BenchmarkWorkload>();
        }
    }
}
#endif
