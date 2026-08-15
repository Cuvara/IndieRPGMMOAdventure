using UnityEngine;

namespace Scripts
{
    /// <summary>
    /// Caps the render frame rate, because an uncapped one makes prediction look worse the
    /// faster the machine is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why this exists.</b> The simulation advances in whole ticks — 60 Hz by default,
    /// set by the server and read off the wire. Until the render path interpolates
    /// <i>within</i> a tick, the on-screen position only changes when a tick completes. So
    /// the still-frame ratio is <c>1 - tickRate/fps</c>: at 500 fps that is <b>82.7%
    /// of frames showing no movement at all</b> (measured, not estimated), which reads as a
    /// stutter that gets <i>worse</i> on faster hardware. Capping near the tick rate puts
    /// roughly one frame per tick and most frames move.
    /// </para>
    /// <para>
    /// <b>This is a mitigation, not the fix.</b> The fix is sub-tick interpolation on the
    /// render path, so the rendered position moves every frame while the simulation still
    /// steps in whole ticks. Once that ships, this cap is free to rise or go away — it will
    /// be a battery and thermal decision rather than a smoothness one. <b>Do not treat the
    /// cap as the reason motion is smooth</b>, or raising it later will look like a
    /// regression in something else.
    /// </para>
    /// <para>
    /// <b>Configuring it.</b> Pass <c>-targetFps N</c> on the command line — no rebuild
    /// needed, which is the point:
    /// </para>
    /// <code>
    /// IndieRPGMMOAdventure.exe -targetFps 144   // cap at 144
    /// IndieRPGMMOAdventure.exe -targetFps 0     // uncapped, the old behaviour
    /// </code>
    /// <para>
    /// With no argument the cap is <see cref="DefaultTargetFps"/>. VSync is off project-wide
    /// (<c>vSyncCount: 0</c>); <see cref="Application.targetFrameRate"/> is ignored while
    /// VSync is on, so turning VSync on in Quality Settings silently overrides everything
    /// here.
    /// </para>
    /// </remarks>
    public static class FrameRateCap
    {
        /// <summary>Cap applied when no <c>-targetFps</c> argument is present.</summary>
        /// <remarks>
        /// <para>
        /// <b>Uncapped, because capping was tried and did not help.</b> A 60 cap was shipped
        /// on the theory that one frame per simulation tick would hide the local player's
        /// stutter. Tested on a real build: <b>it did not</b>. That is evidence, and it
        /// rules the frame rate out as the cause rather than leaving it as a suspect —
        /// which also makes it wrong to keep a cap that costs frames and buys nothing.
        /// </para>
        /// <para>
        /// It is also the worst possible cap if the frame rate ever were the issue: at
        /// exactly the tick rate, frames and ticks beat against each other, so some frames
        /// see two ticks and some see none. A cap meant to smooth motion should be a
        /// multiple of the tick rate, not equal to it.
        /// </para>
        /// <para>
        /// The mechanism stays so the rate can be pinned for a measurement or for battery
        /// and thermals, which is what a cap is legitimately for.
        /// </para>
        /// </remarks>
        public const int DefaultTargetFps = 0;

        private const string Arg = "-targetFps";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Apply()
        {
            int target = ResolveFromCommandLine(System.Environment.GetCommandLineArgs());

            // -1 is Unity's "as fast as the platform allows". Anything <= 0 from the user
            // means "uncapped", and -1 is how that is spelled.
            Application.targetFrameRate = target > 0 ? target : -1;

            Debug.Log($"[FrameRateCap] targetFrameRate = {Application.targetFrameRate} " +
                      $"(vSyncCount = {QualitySettings.vSyncCount}; a non-zero vSyncCount " +
                      "overrides this entirely). Override with -targetFps N.");
        }

        /// <summary>
        /// Reads the cap from <paramref name="args"/>, falling back to
        /// <see cref="DefaultTargetFps"/>. Internal rather than private so it is testable
        /// without launching a player.
        /// </summary>
        internal static int ResolveFromCommandLine(string[] args)
        {
            if (args == null) return DefaultTargetFps;

            for (int i = 0; i < args.Length - 1; i++)
            {
                if (!string.Equals(args[i], Arg, System.StringComparison.OrdinalIgnoreCase))
                    continue;

                // A malformed value falls back rather than throwing: a bad launch argument
                // should not stop the game starting.
                return int.TryParse(args[i + 1], out int parsed) ? parsed : DefaultTargetFps;
            }

            return DefaultTargetFps;
        }
    }
}
