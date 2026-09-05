namespace Cuvara.DOTS.Physics
{
    /// <summary>
    /// Published when an entity enters or exits a trigger volume.
    /// </summary>
    public readonly struct EntityTriggerEvent
    {
        public readonly int EntityIndexA;
        public readonly int EntityIndexB;

        /// <summary>True on the frame the overlap started; false on the frame it ended.</summary>
        public readonly bool Entered;

        public EntityTriggerEvent(int entityIndexA, int entityIndexB, bool entered)
        {
            EntityIndexA = entityIndexA;
            EntityIndexB = entityIndexB;
            Entered = entered;
        }
    }
}
