namespace Cuvara.UIToolkit.Editor
{
    using System;
    using System.IO;
    using System.Linq;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>
    /// An Editor window that scaffolds a new screen, popup or collection item — the UXML,
    /// the USS, the C# (view interface + view + presenter), and a test skeleton — in one
    /// click, emitting code that matches the package's own API rather than the frozen
    /// GameFoundation wizard's.
    /// </summary>
    public sealed class ScreenCreatorWizard : EditorWindow
    {
        private const string WindowTitle = "Screen Creator";
        private const string UxmlPath = "Packages/com.cuvara.uitoolkit/Editor/ScreenCreator/ScreenCreatorWizard.uxml";

        private DropdownField typeDropdown;
        private Toggle        hasModelToggle;
        private TextField     nameField;
        private TextField     outputPathField;
        private Button        browseButton;
        private Button        generateButton;

        [MenuItem("Assets/Cuvara/Create Screen")]
        public static void OpenWindow()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("Screen Creator cannot be used during Play mode.");
                return;
            }

            var window = GetWindow<ScreenCreatorWizard>();
            window.titleContent = new GUIContent(WindowTitle);
            window.minSize = new Vector2(400, 280);
        }

        public void CreateGUI()
        {
            var visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);

            if (visualTree == null)
            {
                this.rootVisualElement.Add(new Label($"Could not load {UxmlPath}"));
                return;
            }

            this.rootVisualElement.Add(visualTree.Instantiate());

            this.typeDropdown    = this.rootVisualElement.Q<DropdownField>("type-dropdown");
            this.hasModelToggle  = this.rootVisualElement.Q<Toggle>("has-model-toggle");
            this.nameField       = this.rootVisualElement.Q<TextField>("name-field");
            this.outputPathField = this.rootVisualElement.Q<TextField>("output-path-field");
            this.browseButton    = this.rootVisualElement.Q<Button>("browse-button");
            this.generateButton  = this.rootVisualElement.Q<Button>("generate-button");

            this.typeDropdown.choices = Enum.GetNames(typeof(ScreenType)).ToList();
            this.typeDropdown.value = nameof(ScreenType.Screen);

            this.typeDropdown.RegisterValueChangedCallback(evt =>
            {
                if (Enum.TryParse(evt.newValue, out ScreenType type) && type == ScreenType.Item)
                {
                    this.hasModelToggle.value = true;
                    this.hasModelToggle.SetEnabled(false);
                }
                else
                {
                    this.hasModelToggle.SetEnabled(true);
                }
            });

            // Default output path from selection
            var selected = Selection.activeObject;
            this.outputPathField.value = selected != null
                ? AssetDatabase.GetAssetPath(selected)
                : "Assets";

            this.browseButton.clicked += () =>
            {
                var path = EditorUtility.OpenFolderPanel("Choose output folder", "Assets", "");
                if (!string.IsNullOrEmpty(path) && path.Contains("Assets"))
                    this.outputPathField.value = FileUtil.GetProjectRelativePath(path);
            };

            this.generateButton.clicked += this.OnGenerate;
        }

        private void OnGenerate()
        {
            var name = this.nameField.value?.Trim();

            if (string.IsNullOrEmpty(name))
            {
                EditorUtility.DisplayDialog(WindowTitle, "Please enter a name.", "OK");
                return;
            }

            if (!Enum.TryParse(this.typeDropdown.value, out ScreenType type))
            {
                EditorUtility.DisplayDialog(WindowTitle, "Invalid screen type.", "OK");
                return;
            }

            var outputPath = this.outputPathField.value?.Trim();
            if (string.IsNullOrEmpty(outputPath)) outputPath = "Assets";
            if (outputPath.EndsWith("/")) outputPath = outputPath[..^1];

            var hasModel = this.hasModelToggle.value;

            // Derive namespace from the output path
            var ns = outputPath.Replace("Assets/", "").Replace("Assets", "");
            ns = string.IsNullOrWhiteSpace(ns) ? name : ns.Replace("/", ".").Replace(" ", "_");

            var absoluteDir = Path.Combine(
                Directory.GetParent(Application.dataPath)!.FullName,
                outputPath);

            if (!Directory.Exists(absoluteDir))
                Directory.CreateDirectory(absoluteDir);

            var filesCreated = 0;

            // 1. C# script
            var scriptContent = ScreenCreatorTemplates.Substitute(
                ScreenCreatorTemplates.SelectScriptTemplate(type, hasModel), ns, name);
            var scriptPath = Path.Combine(absoluteDir, $"{name}Screen.cs");

            if (TryWriteFile(scriptPath, scriptContent)) filesCreated++;

            // 2. UXML
            var uxmlContent = ScreenCreatorTemplates.Substitute(
                ScreenCreatorTemplates.SelectUxmlTemplate(type), ns, name);
            var uxmlPath = Path.Combine(absoluteDir, $"{name}.uxml");

            if (TryWriteFile(uxmlPath, uxmlContent)) filesCreated++;

            // 3. USS (not for items)
            if (type != ScreenType.Item)
            {
                var ussContent = ScreenCreatorTemplates.Substitute(
                    ScreenCreatorTemplates.SelectUssTemplate(), ns, name);
                var ussPath = Path.Combine(absoluteDir, $"{name}.uss");

                if (TryWriteFile(ussPath, ussContent)) filesCreated++;
            }

            // 4. Test skeleton
            var testContent = ScreenCreatorTemplates.Substitute(
                ScreenCreatorTemplates.SelectTestTemplate(), ns, name);
            var testPath = Path.Combine(absoluteDir, $"{name}PresenterTests.cs");

            if (TryWriteFile(testPath, testContent)) filesCreated++;

            AssetDatabase.Refresh();

            Debug.Log($"<color=green>Screen Creator: generated {filesCreated} file(s) for '{name}' at {outputPath}</color>");

            if (filesCreated > 0)
                this.Close();
        }

        private static bool TryWriteFile(string absolutePath, string content)
        {
            if (File.Exists(absolutePath))
            {
                Debug.LogError($"Screen Creator: file already exists — {absolutePath}");
                return false;
            }

            try
            {
                File.WriteAllText(absolutePath, content);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"Screen Creator: could not write {absolutePath} — {e.Message}");
                return false;
            }
        }
    }
}
