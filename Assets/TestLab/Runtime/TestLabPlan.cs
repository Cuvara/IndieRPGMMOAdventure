namespace TestLab
{
    using System;
    using UnityEngine;

    /// <summary>
    /// One client's part of a testlab scenario, written by the rpg-factory testlab orchestrator
    /// as <c>plan.json</c> and handed to the player with <c>-testlab-plan</c>. Parsed with
    /// <see cref="JsonUtility"/>, so every field is a public field with a default.
    /// </summary>
    /// <remarks>
    /// Times are seconds on the scenario clock, which starts at the instant the orchestrator
    /// writes into <c>control.json</c> once every client of the run is in the world. The same
    /// instant for every client is what makes coordinated movement coordinated.
    /// </remarks>
    [Serializable]
    public sealed class TestLabPlan
    {
        /// <summary>Schema id; the runner refuses any other.</summary>
        public const string CurrentSchema = "rpg.testlab.plan.v1";

        public string schema = string.Empty;
        public string run_id = string.Empty;
        public string scenario_id = string.Empty;

        /// <summary>Map the server runs; selects which mirrored collision boxes the arena shows.</summary>
        public string map = string.Empty;
        public int client_index;

        /// <summary>Scenario length after the start instant.</summary>
        public float duration_s = 30f;

        /// <summary>Rate of entity/position samples written to <c>samples.ndjson</c>.</summary>
        public float sample_hz = 20f;

        /// <summary>
        /// Rendered step above <c>speed x dt x jump_factor + jump_slack</c> counts as a jump
        /// (snap / teleport) for an entity.
        /// </summary>
        public float jump_factor = 3f;

        public float jump_slack = 0.25f;

        /// <summary>Fastest legitimate ground speed of any entity, world units per second.</summary>
        public float max_speed = 6f;

        /// <summary>Screenshot instants on the scenario clock.</summary>
        public float[] screenshots_s = Array.Empty<float>();

        /// <summary>Extra screenshots when a jump is detected, rate-limited, at most this many.</summary>
        public int anomaly_screenshots = 3;

        public bool overlay = true;

        /// <summary>
        /// Write the netcode predictor's call trace to <c>trace.ndjson</c> (netcode &gt;= 8986908;
        /// <c>docs/TESTLAB.md</c> "Predictor call trace"). Off by default: it costs formatting per call.
        /// </summary>
        public bool predictor_trace;
        public bool quit_on_done = true;

        public TestLabStep[] steps = Array.Empty<TestLabStep>();
        public TestLabAction[] actions = Array.Empty<TestLabAction>();

        public static TestLabPlan Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new FormatException("testlab plan is empty");
            }

            var plan = JsonUtility.FromJson<TestLabPlan>(json);
            if (plan == null || plan.schema != CurrentSchema)
            {
                throw new FormatException(
                    $"testlab plan schema '{plan?.schema}' is not '{CurrentSchema}'");
            }

            plan.steps ??= Array.Empty<TestLabStep>();
            plan.actions ??= Array.Empty<TestLabAction>();
            plan.screenshots_s ??= Array.Empty<float>();
            foreach (var step in plan.steps)
            {
                if (!TestLabStep.IsKnownMode(step.mode))
                {
                    throw new FormatException($"testlab plan: unknown step mode '{step.mode}'");
                }
            }

            foreach (var action in plan.actions)
            {
                if (!TestLabAction.IsKnownKind(action.kind))
                {
                    throw new FormatException($"testlab plan: unknown action kind '{action.kind}'");
                }
            }

            return plan;
        }
    }

    /// <summary>
    /// A movement segment, active for <c>t0 &lt;= t &lt; t1</c> on the scenario clock. Outside every
    /// segment the client is idle.
    /// </summary>
    [Serializable]
    public sealed class TestLabStep
    {
        public const string Idle = "idle";
        public const string Move = "move";
        public const string Circle = "circle";
        public const string TowardPoint = "toward_point";
        public const string TowardNearestEnemy = "toward_nearest_enemy";
        public const string AwayFromNearestEnemy = "away_from_nearest_enemy";
        public const string TowardClient = "toward_client";

        public float t0;
        public float t1;
        public string mode = Idle;

        /// <summary><see cref="Move"/>: direction in wire space; normalised by the runner.</summary>
        public float x;
        public float y;

        /// <summary><see cref="Circle"/>: angular rate in radians per second, sign = direction.</summary>
        public float rate = 1f;

        /// <summary><see cref="TowardPoint"/>: wire-space target.</summary>
        public float px;
        public float py;

        /// <summary>Stop within this distance of the target (point, enemy or client).</summary>
        public float stop_distance = 0.5f;

        /// <summary><see cref="TowardClient"/>: index of the client to walk to.</summary>
        public int target_client = -1;

        public static bool IsKnownMode(string mode) =>
            mode == Idle || mode == Move || mode == Circle || mode == TowardPoint ||
            mode == TowardNearestEnemy || mode == AwayFromNearestEnemy || mode == TowardClient;
    }

    /// <summary>
    /// An action. Input actions repeat every <c>interval_s</c> (<see cref="CastSpam"/>:
    /// <c>interval_ms</c>) while <c>t0 &lt;= t &lt; t1</c>; the one-shot actions
    /// (<see cref="IsOneShot"/>) fire once, at the first frame with <c>t &gt;= t0</c>.
    /// </summary>
    [Serializable]
    public sealed class TestLabAction
    {
        public const string CastNearestEnemy = "cast_nearest_enemy";
        public const string CastForward = "cast_forward";
        public const string Jump = "jump";

        /// <summary>Cast aimed at the wire-space point (<see cref="x"/>, <see cref="y"/>), in range or not.</summary>
        public const string CastAtPoint = "cast_at_point";

        /// <summary>Cast every <see cref="interval_ms"/>, at the nearest enemy in range, else forward.</summary>
        public const string CastSpam = "cast_spam";

        /// <summary>One-shot: block the main thread for <see cref="ms"/> milliseconds.</summary>
        public const string Stall = "stall";

        /// <summary>One-shot: leave through <c>NetworkClient.Disconnect</c>, write the summary, end the run.</summary>
        public const string Quit = "quit";

        /// <summary>One-shot: drop the gameplay link so netcode's own automatic reconnect runs.</summary>
        public const string Reconnect = "reconnect";

        public float t0;
        public float t1;
        public string kind = CastNearestEnemy;
        public float interval_s = 1f;

        /// <summary>Only enemies within this range count for <see cref="CastNearestEnemy"/> and <see cref="CastSpam"/>.</summary>
        public float range = 12f;

        /// <summary><see cref="CastAtPoint"/>: wire-space aim point.</summary>
        public float x;
        public float y;

        /// <summary><see cref="CastSpam"/>: interval in milliseconds; below the cooldown on purpose.</summary>
        public float interval_ms = 100f;

        /// <summary><see cref="Stall"/>: how long to block, milliseconds.</summary>
        public int ms = 250;

        public static bool IsKnownKind(string kind) =>
            kind == CastNearestEnemy || kind == CastForward || kind == Jump ||
            kind == CastAtPoint || kind == CastSpam || IsOneShot(kind);

        /// <summary>Fired once by the runner, never by the input planner.</summary>
        public static bool IsOneShot(string kind) => kind == Stall || kind == Quit || kind == Reconnect;
    }
}
