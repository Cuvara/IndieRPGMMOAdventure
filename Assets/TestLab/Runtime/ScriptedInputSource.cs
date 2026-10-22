namespace TestLab
{
    using Scripts.DI;
    using UnityEngine;

    /// <summary>
    /// The testlab's <see cref="IGameplayInputSource"/>: serves the controls the
    /// <see cref="TestLabRunner"/> planned for this frame. It replaces only the device reads; the
    /// game's send schedule, prediction and casting run unchanged on these values.
    /// </summary>
    /// <remarks>
    /// Buttons are latched until read, because the bridge samples them once per frame but sends
    /// on its own schedule: a cast planned for a frame between two sends must still reach the
    /// next send, exactly as a key press would.
    /// </remarks>
    public sealed class ScriptedInputSource : IGameplayInputSource
    {
        private float moveX;
        private float moveY;
        private bool jump;
        private bool cast;
        private bool hasAim;
        private Vector3 aim;

        /// <summary>Raised from <see cref="ReadMove"/>: the vector the game is about to send.</summary>
        public System.Action<float, float> MoveRead;

        /// <summary>
        /// Raised when the game consumes a latched cast press. Presses planned faster than the game
        /// reads them collapse into one, exactly as a held key would.
        /// </summary>
        public System.Action CastRead;

        public void Set(in PlannedInput input)
        {
            this.moveX = input.MoveX;
            this.moveY = input.MoveY;
            this.jump |= input.Jump;
            if (input.Cast)
            {
                this.cast = true;
                this.hasAim = input.HasAim;
                this.aim = input.Aim;
            }
        }

        public void Clear()
        {
            this.moveX = 0f;
            this.moveY = 0f;
            this.jump = false;
            this.cast = false;
            this.hasAim = false;
        }

        public void ReadMove(out float x, out float y)
        {
            x = this.moveX;
            y = this.moveY;
            this.MoveRead?.Invoke(x, y);
        }

        public bool JumpPressed()
        {
            bool value = this.jump;
            this.jump = false;
            return value;
        }

        public bool CastPressed()
        {
            // The aim stays latched: the bridge asks for it after it has consumed the press.
            bool value = this.cast;
            this.cast = false;
            if (value)
            {
                this.CastRead?.Invoke();
            }

            return value;
        }

        public bool PickupPressed() => false;

        public bool InventoryTogglePressed() => false;

        public bool TryPointerPosition(out Vector2 position)
        {
            position = default;
            return false;
        }

        public bool TryAimPoint(Vector3 feet, out Vector3 aimPoint)
        {
            if (!this.hasAim)
            {
                aimPoint = default;
                return false;
            }

            // The planner aims at the target's ground point; cast at the caster's height.
            aimPoint = new Vector3(this.aim.x, this.aim.y, feet.z);
            this.hasAim = false;
            return true;
        }
    }
}
