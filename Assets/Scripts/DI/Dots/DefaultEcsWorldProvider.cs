#if CUVARA_DOTS && CUVARA_DOTS_VCONTAINER
namespace Scripts.DI.Dots
{
    using System;
    using Unity.Entities;

    /// <summary>
    /// The production <see cref="IEcsWorldProvider"/>: <c>World.DefaultGameObjectInjectionWorld</c>,
    /// read at each access, or a fixed world when a test hands one in.
    /// </summary>
    public sealed class DefaultEcsWorldProvider : IEcsWorldProvider
    {
        private readonly World fixedWorld;

        /// <summary>Resolves the default injection world on every access.</summary>
        public DefaultEcsWorldProvider()
        {
        }

        /// <summary>Always returns <paramref name="world"/>; the test seam (a throwaway world).</summary>
        public DefaultEcsWorldProvider(World world)
        {
            this.fixedWorld = world;
        }

        /// <inheritdoc />
        public World World
        {
            get
            {
                var world = this.fixedWorld ?? World.DefaultGameObjectInjectionWorld;
                if (world == null)
                {
                    throw new InvalidOperationException(
                        "No ECS world: World.DefaultGameObjectInjectionWorld is null. " +
                        "The ECS world is created by the Entities bootstrap; it does not exist yet, or has been disposed.");
                }

                return world;
            }
        }
    }
}
#endif
