using System.IO;
using NUnit.Framework;
using UnityEditor;

namespace Tests.Editor
{
    /// <summary>
    /// The contract between the toolkit's self-hosted lanes and PlayerBuilder.
    ///
    /// PlayerBuilder.Build is the `-executeMethod` those lanes call by name, and until
    /// now nothing had ever executed a line of it in CI: the Docker lane uses game-ci's
    /// own builder and never reaches this file. So the first run of a 337-line script
    /// would have been on somebody's machine, with a build to salvage.
    ///
    /// These do not build anything. They pin the parts that decide what the artifact is
    /// called, which scene boots, and how the lane's arguments are read — the parts the
    /// toolkit depends on and cannot see.
    /// </summary>
    public sealed class PlayerBuilderContractTests
    {
        // ── Artifact naming ──────────────────────────────────────────────────────
        // Stage 04 validates `release-android-aab` by looking for an .aab. If this
        // returned .apk for a bundle build, the wrong file would ship under the right
        // artifact name — silently, because both are files and both exist.

        [Test]
        public void Android_IsAnApk_WhenTheBundleFlagIsOff()
        {
            var previous = EditorUserBuildSettings.buildAppBundle;
            try
            {
                EditorUserBuildSettings.buildAppBundle = false;
                var path = PlayerBuilder.LocationFor(BuildTarget.Android, "build/Android", "Game");
                Assert.That(Path.GetExtension(path), Is.EqualTo(".apk"));
            }
            finally { EditorUserBuildSettings.buildAppBundle = previous; }
        }

        [Test]
        public void Android_IsAnAab_WhenTheBundleFlagIsOn()
        {
            var previous = EditorUserBuildSettings.buildAppBundle;
            try
            {
                EditorUserBuildSettings.buildAppBundle = true;
                var path = PlayerBuilder.LocationFor(BuildTarget.Android, "build/Android", "Game");
                Assert.That(Path.GetExtension(path), Is.EqualTo(".aab"),
                    "ANDROID_APP_BUNDLE is how the release lane asks for a bundle; this is " +
                    "the only place that request becomes a filename");
            }
            finally { EditorUserBuildSettings.buildAppBundle = previous; }
        }

        [TestCase(BuildTarget.StandaloneWindows64, ".exe")]
        [TestCase(BuildTarget.StandaloneLinux64, ".x86_64")]
        public void DesktopTargets_CarryTheExtensionTheirValidatorLooksFor(
            BuildTarget target, string extension)
        {
            var path = PlayerBuilder.LocationFor(target, "build/Desktop", "Game");
            Assert.That(Path.GetExtension(path), Is.EqualTo(extension));
        }

        [Test]
        public void WebGL_IsADirectory_NotAFile()
        {
            // WebGL emits index.html plus Build/. A file path here would make Unity
            // write the player into a path the upload step does not collect.
            var path = PlayerBuilder.LocationFor(BuildTarget.WebGL, "build/WebGL", "Game");
            Assert.That(path, Is.EqualTo("build/WebGL"));
            Assert.That(Path.GetExtension(path), Is.Empty);
        }

        // ── Argument parsing ─────────────────────────────────────────────────────

        [Test]
        public void ReadArg_TakesTheValueAfterTheFlag()
        {
            var args = new[] { "Unity.exe", "-batchmode", "-buildOutput", "build", "-quit" };
            Assert.That(PlayerBuilder.ReadArg(args, "-buildOutput"), Is.EqualTo("build"));
        }

        [Test]
        public void ReadArg_IsNull_WhenTheFlagIsAbsent()
        {
            var args = new[] { "Unity.exe", "-batchmode" };
            Assert.That(PlayerBuilder.ReadArg(args, "-buildOutput"), Is.Null);
        }

        [Test]
        public void ReadArg_IsNull_WhenTheFlagIsLast_AndHasNoValue()
        {
            // A trailing flag has no value to read. Returning the flag itself, or
            // reading past the end, would put "-buildOutput" in a path.
            var args = new[] { "Unity.exe", "-buildOutput" };
            Assert.That(PlayerBuilder.ReadArg(args, "-buildOutput"), Is.Null);
        }

        [Test]
        public void HasArg_DistinguishesPresenceFromValue()
        {
            var args = new[] { "Unity.exe", "-development" };
            Assert.That(PlayerBuilder.HasArg(args, "-development"), Is.True);
            Assert.That(PlayerBuilder.HasArg(args, "-profiling"), Is.False);
        }

        // ── Scene order ──────────────────────────────────────────────────────────

        [Test]
        public void BootSceneOverride_MovesTheNamedSceneToIndexZero()
        {
            var scenes = new[] { "Assets/Scenes/A.unity", "Assets/Scenes/Boot.unity" };
            var result = PlayerBuilder.ApplyBootSceneOverride(scenes, "Assets/Scenes/Boot.unity");
            Assert.That(result[0], Is.EqualTo("Assets/Scenes/Boot.unity"));
            Assert.That(result, Has.Length.EqualTo(2), "reordering must not drop a scene");
            Assert.That(result, Is.EquivalentTo(scenes), "reordering must not invent one either");
        }

        [Test]
        public void BootSceneOverride_NormalisesWindowsSeparators()
        {
            // The lane may pass a path from a Windows shell; Unity's scene list is
            // always forward-slashed.
            var scenes = new[] { "Assets/Scenes/Boot.unity", "Assets/Scenes/A.unity" };
            var result = PlayerBuilder.ApplyBootSceneOverride(scenes, @"Assets\Scenes\Boot.unity");
            Assert.That(result[0], Is.EqualTo("Assets/Scenes/Boot.unity"));
        }

        [Test]
        public void BootSceneOverride_LeavesTheListAlone_WhenNoSceneIsNamed()
        {
            var scenes = new[] { "Assets/Scenes/A.unity" };
            Assert.That(PlayerBuilder.ApplyBootSceneOverride(scenes, null), Is.SameAs(scenes));
            Assert.That(PlayerBuilder.ApplyBootSceneOverride(scenes, ""), Is.SameAs(scenes));
        }
    }
}
