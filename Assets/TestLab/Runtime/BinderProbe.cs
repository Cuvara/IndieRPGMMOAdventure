namespace TestLab
{
    using System;
    using System.Reflection;
    using Cuvara.Netcode.View;
    using UnityEngine;

    /// <summary>
    /// Reads the <see cref="WorldViewBinder"/> that draws remote entities in the shipping client:
    /// its render clock (<see cref="WorldViewBinder.RenderTick"/>) and the last server tick it fed.
    /// Test-only; reflection, so no production type grows an accessor for the testlab.
    /// </summary>
    /// <remarks>
    /// <para><b>Why the binder and not the ECS timeline.</b> <c>DotsWorldBridge</c> feeds
    /// <c>DotsEntityView</c> untimed <c>SetState</c> commands (tick 0). The drain therefore never
    /// calls <c>InterpolationClock.NoteSnapshot</c>, the ECS <c>InterpolationTimeline</c> clock
    /// never gets a sample, and remote positions are the binder's managed interpolation. The binder's
    /// clock is the one remote entities are drawn at; the ECS clock is a fallback for a consumer of
    /// the timed path (<c>SetStateAtTick</c>).</para>
    /// <para><b>One frame of latency, by construction.</b> The binder ticks in
    /// <c>DotsWorldBridge.Update</c> and enqueues the states; <c>NetworkViewCommandSystem</c> drains
    /// them in <c>InitializationSystemGroup</c>, at the start of the NEXT frame. So the positions
    /// drawn in frame N were evaluated at the binder's <c>RenderTick</c> as it stood after frame
    /// N-1's <c>Update</c>. The runner captures it in <c>LateUpdate</c> and attributes it to the
    /// following frame.</para>
    /// </remarks>
    public sealed class BinderProbe
    {
        private const string BridgeType = "Scripts.DI.Dots.DotsWorldBridge, NDC.Scripts.DI";

        private Type bridgeType;
        private FieldInfo binderField;
        private UnityEngine.Object bridge;
        private double nextLookup;

        /// <summary>Why the binder cannot be read, or null while it can (or has not been looked for yet).</summary>
        public string Unavailable { get; private set; }

        public bool TryRead(double now, out double renderTick, out long lastServerTick)
        {
            renderTick = double.NaN;
            lastServerTick = -1;
            var binder = this.Resolve(now);
            if (binder == null)
            {
                return false;
            }

            renderTick = binder.RenderTick;
            lastServerTick = binder.LastServerTick;
            return true;
        }

        private WorldViewBinder Resolve(double now)
        {
            if (this.bridge == null)
            {
                // A scene without the bridge, or one not installed yet: look again once a second.
                if (now < this.nextLookup)
                {
                    return null;
                }

                this.nextLookup = now + 1.0;
                if (this.bridgeType == null)
                {
                    this.bridgeType = Type.GetType(BridgeType, false);
                    if (this.bridgeType == null)
                    {
                        this.Unavailable = "type " + BridgeType + " not found";
                        return null;
                    }

                    foreach (var f in this.bridgeType.GetFields(BindingFlags.Instance | BindingFlags.NonPublic))
                    {
                        if (f.FieldType == typeof(WorldViewBinder))
                        {
                            this.binderField = f;
                            break;
                        }
                    }

                    if (this.binderField == null)
                    {
                        this.Unavailable = "DotsWorldBridge has no WorldViewBinder field";
                        return null;
                    }
                }

                this.bridge = UnityEngine.Object.FindAnyObjectByType(this.bridgeType);
                if (this.bridge == null)
                {
                    return null;
                }
            }

            this.Unavailable = null;
            return this.binderField.GetValue(this.bridge) as WorldViewBinder;
        }
    }
}
