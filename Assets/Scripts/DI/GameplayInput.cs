namespace Scripts.DI
{
    using System;
    using UnityEngine;

    /// <summary>
    /// Where the gameplay controls come from: the movement vector, the one-shot buttons and the
    /// aim point. <c>DotsWorldBridge</c> and <c>GameplayPanelDriver</c> read every
    /// control through an injected <see cref="IGameplayInputSource"/>, never from a device directly.
    /// </summary>
    /// <remarks>
    /// The production source is <see cref="DeviceGameplayInputSource"/> (keyboard, mouse, pointer).
    /// An automated test run (the rpg-factory testlab, compiled only with
    /// <c>CUVARA_TESTLAB</c>) installs a scripted source instead. Everything downstream of the read
    /// stays on the production path: the send schedule, <c>SendInput</c>, the predictor's
    /// <c>RecordInput</c>, clock steering and projectile prediction. Only the values change.
    /// Device input needs window focus under the Input System's default background behaviour;
    /// a scripted source does not.
    /// </remarks>
    public interface IGameplayInputSource
    {
        /// <summary>Movement direction in wire space (x right, y forward), each axis in [-1, 1].</summary>
        void ReadMove(out float x, out float y);

        /// <summary>True on the frame a jump is requested (edge-triggered).</summary>
        bool JumpPressed();

        /// <summary>True on the frame a projectile cast is requested (edge-triggered).</summary>
        bool CastPressed();

        /// <summary>True on the frame an item pickup is requested (edge-triggered).</summary>
        bool PickupPressed();

        /// <summary>True on the frame the inventory panel is toggled (edge-triggered).</summary>
        bool InventoryTogglePressed();

        /// <summary>The pointer's screen position, or false when the source has no pointer.</summary>
        bool TryPointerPosition(out Vector2 position);

        /// <summary>
        /// An explicit aim point in wire space (x, y ground plane, z up) for a cast, or false to
        /// aim with the pointer as usual.
        /// </summary>
        bool TryAimPoint(Vector3 feet, out Vector3 aim);
    }

    /// <summary>
    /// Obsolete static forwarder to the injected <see cref="GameplayInputRouter"/>. Inject
    /// <see cref="IGameplayInputSource"/> instead; this stays for one release so a missed caller
    /// still works, and nothing in production code reads it.
    /// </summary>
    [Obsolete("Inject IGameplayInputSource (GameplayInputRouter) instead. Removed after one release.")]
    public static class GameplayInput
    {
        /// <summary>The router of the live root container; set by its constructor.</summary>
        internal static GameplayInputRouter Router;
        private static IGameplayInputSource pendingSource;

        internal static void AttachRouter(GameplayInputRouter router)
        {
            Router = router;
            var source = pendingSource;
            pendingSource = null;
            if (source != null) router.Override(source);
        }

        /// <summary>The routed source; pending assignment or devices before a container has built a router. Setting null clears the override.</summary>
        public static IGameplayInputSource Source
        {
            get => (IGameplayInputSource)Router ?? pendingSource ?? DeviceGameplayInputSource.Instance;
            set
            {
                if (value == null)
                {
                    pendingSource = null;
                    Router?.ClearOverride();
                }
                else if (Router == null)
                {
                    pendingSource = value;
                }
                else
                {
                    Router?.Override(value);
                }
            }
        }

        /// <summary>Statics survive a play session when domain reload is disabled; start without a router.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay()
        {
            Router = null;
            pendingSource = null;
        }
    }

    /// <summary>
    /// The player's devices: WASD/arrows for movement and <see cref="GameplayInputReader"/> for the
    /// buttons and the pointer. Never supplies an explicit aim point.
    /// </summary>
    public sealed class DeviceGameplayInputSource : IGameplayInputSource
    {
        public static readonly DeviceGameplayInputSource Instance = new DeviceGameplayInputSource();

        private DeviceGameplayInputSource()
        {
        }

        /// <summary>
        /// WASD/arrows through whichever input backend the project enables. This project runs
        /// "Input System Package (New)" (<c>activeInputHandler: 1</c>), under which the legacy
        /// <c>UnityEngine.Input</c> class THROWS rather than returning zero, so the legacy call
        /// is compiled only when the legacy manager is actually enabled.
        /// </summary>
        public void ReadMove(out float x, out float y)
        {
#if ENABLE_INPUT_SYSTEM
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard == null)
            {
                // No keyboard device: a headless or automated run. Not an error.
                x = 0f;
                y = 0f;
                return;
            }

            static float Axis(bool positive, bool negative) => (positive ? 1f : 0f) - (negative ? 1f : 0f);
            x = Axis(keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed,
                     keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed);
            y = Axis(keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed,
                     keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed);
#elif ENABLE_LEGACY_INPUT_MANAGER
            // Raw, not smoothed: GetAxis's acceleration curve would make the predicted vector
            // differ from what the player would say they pressed.
            x = Input.GetAxisRaw("Horizontal");
            y = Input.GetAxisRaw("Vertical");
#else
            x = 0f;
            y = 0f;
#endif
        }

        public bool JumpPressed() => GameplayInputReader.JumpPressed();

        public bool CastPressed() => GameplayInputReader.CastPressed();

        public bool PickupPressed() => GameplayInputReader.PickupPressed();

        public bool InventoryTogglePressed() => GameplayInputReader.InventoryTogglePressed();

        public bool TryPointerPosition(out Vector2 position) => GameplayInputReader.TryPointerPosition(out position);

        public bool TryAimPoint(Vector3 feet, out Vector3 aim)
        {
            aim = default;
            return false;
        }
    }
}
