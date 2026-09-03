namespace Cuvara.UIToolkit.Codegen
{
    using System.IO;
    using System.Text.RegularExpressions;

    /// <summary>
    /// Decides the namespace a generated bindings class is emitted into, for a given
    /// UXML file path.
    /// </summary>
    /// <remarks>
    /// <para><b>The convention, in priority order</b> (walking from the UXML's directory
    /// upward):</para>
    /// <list type="number">
    /// <item>A <c>.uxml-namespace</c> file — its trimmed content IS the namespace. This is
    /// the configurability escape hatch: drop one next to (or above) your UXML files to
    /// override the default for that subtree.</item>
    /// <item>The nearest <c>*.asmdef</c>'s <c>rootNamespace</c> (its <c>name</c> when
    /// <c>rootNamespace</c> is empty) — the same namespace Unity itself would give a new
    /// script created there, so generated partials land where hand-written view halves
    /// naturally live.</item>
    /// <item><c>UxmlBindings</c>, when neither exists.</item>
    /// </list>
    ///
    /// <para><b>Resolution must be reproducible outside Unity</b>, because the CI drift
    /// check regenerates every enrolled UXML with plain <c>dotnet</c> and byte-compares.
    /// That is why this reads the asmdef with a regex instead of Unity's importer, and why
    /// this file must stay Unity-free (see <see cref="UxmlBindingGenerator"/>).</para>
    /// </remarks>
    public static class UxmlNamespaceResolver
    {
        /// <summary>The namespace used when no override file and no asmdef is found.</summary>
        public const string DefaultNamespace = "UxmlBindings";

        /// <summary>File whose trimmed content overrides the namespace for its subtree.</summary>
        public const string OverrideFileName = ".uxml-namespace";

        private static readonly Regex RootNamespacePattern = new("\"rootNamespace\"\\s*:\\s*\"([^\"]+)\"");
        private static readonly Regex NamePattern = new("\"name\"\\s*:\\s*\"([^\"]+)\"");

        /// <summary>Namespace for the bindings generated from <paramref name="uxmlPath"/>.</summary>
        public static string Resolve(string uxmlPath)
        {
            for (var directory = Path.GetDirectoryName(Path.GetFullPath(uxmlPath));
                 !string.IsNullOrEmpty(directory);
                 directory = Path.GetDirectoryName(directory))
            {
                var overrideFile = Path.Combine(directory, OverrideFileName);
                if (File.Exists(overrideFile))
                {
                    var overridden = File.ReadAllText(overrideFile).Trim();
                    if (overridden.Length > 0) return overridden;
                }

                // Sorted: GetFiles order is filesystem-dependent, and two asmdefs in one
                // directory must not make the namespace vary between machine and CI.
                var asmdefPaths = Directory.GetFiles(directory, "*.asmdef");
                System.Array.Sort(asmdefPaths, System.StringComparer.Ordinal);
                foreach (var asmdefPath in asmdefPaths)
                {
                    var asmdefText = File.ReadAllText(asmdefPath);
                    var rootNamespace = RootNamespacePattern.Match(asmdefText);
                    if (rootNamespace.Success) return rootNamespace.Groups[1].Value;

                    var name = NamePattern.Match(asmdefText);
                    if (name.Success) return name.Groups[1].Value;
                }
            }

            return DefaultNamespace;
        }
    }
}
