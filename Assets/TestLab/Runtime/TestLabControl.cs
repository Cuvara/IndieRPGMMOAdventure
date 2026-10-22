namespace TestLab
{
    using System;
    using UnityEngine;

    /// <summary>
    /// <c>control.json</c>, written by the orchestrator and polled by every client of a run: the
    /// shared start instant, the user id behind each client index, and an abort switch.
    /// </summary>
    [Serializable]
    public sealed class TestLabControl
    {
        /// <summary>Scenario clock zero, Unix epoch milliseconds; 0 = not started.</summary>
        public long start_at_unix_ms;

        /// <summary>User id of each client index, so <c>toward_client</c> can find its target.</summary>
        public string[] client_user_ids = Array.Empty<string>();

        /// <summary>True stops the run: the client writes what it has and quits.</summary>
        public bool abort;

        public static bool TryParse(string json, out TestLabControl control)
        {
            control = null;
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            try
            {
                control = JsonUtility.FromJson<TestLabControl>(json);
                if (control != null)
                {
                    control.client_user_ids ??= Array.Empty<string>();
                }

                return control != null;
            }
            catch (ArgumentException)
            {
                // A half-written file read mid-replace; the next poll reads the whole one.
                return false;
            }
        }
    }
}
