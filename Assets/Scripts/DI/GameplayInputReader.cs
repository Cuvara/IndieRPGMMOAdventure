namespace Scripts.DI
{
    using UnityEngine;

    /// <summary>
    /// The protocol 3 buttons and the pointer, read through whichever input backend the project
    /// enables (new Input System today, <c>activeInputHandler: 1</c>) — the same guard
    /// <c>DotsWorldBridge.ReadKeyboard</c> uses, because the legacy <c>Input</c> class THROWS
    /// under the new backend.
    /// </summary>
    /// <remarks>
    /// Edge-triggered: a key held for ten frames is one jump and one cast, which is what the
    /// server's one-shot fields mean (<c>InputMessage.Jump</c> rides on one step; a cast is one
    /// request). Bindings: <b>Space</b> jump, <b>Q</b> or the <b>right mouse button</b> cast,
    /// <b>E</b> pick up, <b>I</b> inventory panel. The pointer is <c>Pointer.current</c>: mouse,
    /// pen or the primary touch, whichever the platform has.
    /// </remarks>
    public static class GameplayInputReader
    {
        public static bool JumpPressed()
        {
#if ENABLE_INPUT_SYSTEM
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            return keyboard != null && keyboard.spaceKey.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(KeyCode.Space);
#else
            return false;
#endif
        }

        public static bool CastPressed()
        {
#if ENABLE_INPUT_SYSTEM
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            var mouse = UnityEngine.InputSystem.Mouse.current;
            return (keyboard != null && keyboard.qKey.wasPressedThisFrame) ||
                   (mouse != null && mouse.rightButton.wasPressedThisFrame);
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(KeyCode.Q) || Input.GetMouseButtonDown(1);
#else
            return false;
#endif
        }

        public static bool PickupPressed()
        {
#if ENABLE_INPUT_SYSTEM
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            return keyboard != null && keyboard.eKey.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(KeyCode.E);
#else
            return false;
#endif
        }

        public static bool InventoryTogglePressed()
        {
#if ENABLE_INPUT_SYSTEM
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            return keyboard != null && keyboard.iKey.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(KeyCode.I);
#else
            return false;
#endif
        }

        /// <summary>The pointer's screen position, or false with no pointer device (headless).</summary>
        public static bool TryPointerPosition(out Vector2 position)
        {
#if ENABLE_INPUT_SYSTEM
            var pointer = UnityEngine.InputSystem.Pointer.current;
            if (pointer != null)
            {
                position = pointer.position.ReadValue();
                return true;
            }
#elif ENABLE_LEGACY_INPUT_MANAGER
            if (Input.mousePresent)
            {
                position = Input.mousePosition;
                return true;
            }
#endif
            position = default;
            return false;
        }
    }
}
