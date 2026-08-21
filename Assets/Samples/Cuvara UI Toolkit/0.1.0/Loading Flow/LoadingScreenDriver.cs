namespace Cuvara.UIToolkit.Samples.LoadingFlow
{
    using UnityEngine;
    using UnityEngine.SceneManagement;
    using UnityEngine.UIElements;

    /// <summary>
    /// Drives the loading screen animation directly via MonoBehaviour — no VContainer,
    /// no navigator. Runs from Awake, so the progress bar starts on the very first frame.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class LoadingScreenDriver : MonoBehaviour
    {
        private static readonly (float target, string status)[] Steps =
        {
            (0.05f, "Initializing engine..."),
            (0.15f, "Loading core assets..."),
            (0.30f, "Preparing shaders..."),
            (0.45f, "Building world..."),
            (0.60f, "Loading UI..."),
            (0.75f, "Connecting systems..."),
            (0.88f, "Finalizing..."),
            (0.95f, "Almost ready..."),
            (1.00f, "Done!")
        };

        private static readonly string[] Tips =
        {
            "Tip: Press ESC to open the menu during gameplay.",
            "Tip: Explore dungeons with friends for bonus loot!",
            "Tip: The gateway redirects you \u2014 gameplay goes direct to the server.",
            "Tip: Your position is predicted client-side for smooth movement.",
            "Tip: Area of Interest keeps bandwidth low in crowded zones.",
            "Tip: Each map runs on its own game server instance."
        };

        private VisualElement progressFill;
        private Label status;
        private Label progressPercent;
        private Label tip;
        private VisualElement spinner;

        private int stepIndex;
        private int tipIndex;
        private float stepTimer;
        private float tipTimer;
        private float currentProgress;
        private bool done;

        private void OnEnable()
        {
            var doc = GetComponent<UIDocument>();
            var root = doc.rootVisualElement;
            if (root == null) return;

            this.progressFill = root.Q("progress-fill");
            this.status = root.Q<Label>("status");
            this.progressPercent = root.Q<Label>("progress-percent");
            this.tip = root.Q<Label>("tip");
            this.spinner = root.Q("spinner");

            this.stepIndex = 0;
            this.tipIndex = 0;
            this.stepTimer = 0f;
            this.tipTimer = 0f;
            this.currentProgress = 0f;
            this.done = false;

            ApplyStep(0);
        }

        private void Update()
        {
            if (this.done || this.progressFill == null) return;

            this.stepTimer += Time.deltaTime;
            this.tipTimer += Time.deltaTime;

            // Smoothly animate progress toward target
            if (this.stepIndex < Steps.Length)
            {
                float target = Steps[this.stepIndex].target;
                this.currentProgress = Mathf.MoveTowards(this.currentProgress, target, Time.deltaTime * 0.8f);

                int pct = Mathf.RoundToInt(this.currentProgress * 100f);
                this.progressFill.style.width = Length.Percent(pct);
                if (this.progressPercent != null) this.progressPercent.text = $"{pct}%";

                // Pulse spinner
                if (this.spinner != null)
                {
                    float pulse = Mathf.PingPong(Time.time * 3f, 1f);
                    this.spinner.style.opacity = 0.3f + pulse * 0.7f;
                }

                // Advance step when reached target and delay elapsed
                float stepDelay = Random.Range(0.3f, 0.7f);
                if (this.currentProgress >= target - 0.001f && this.stepTimer >= stepDelay)
                {
                    this.stepIndex++;
                    this.stepTimer = 0f;
                    if (this.stepIndex < Steps.Length)
                        ApplyStep(this.stepIndex);
                }
            }

            // Rotate tips
            if (this.tipTimer >= 2f && this.tipIndex < Tips.Length && this.tip != null)
            {
                this.tip.text = Tips[this.tipIndex++];
                this.tipTimer = 0f;
            }

            // All steps done → load MainScene
            if (this.stepIndex >= Steps.Length && !this.done)
            {
                this.done = true;
                if (this.spinner != null) this.spinner.style.opacity = 0f;
                Invoke(nameof(LoadMainScene), 0.5f);
            }
        }

        private void ApplyStep(int idx)
        {
            if (this.status != null)
                this.status.text = Steps[idx].status;
        }

        private void LoadMainScene()
        {
            SceneManager.LoadScene("MainScene");
        }
    }
}
