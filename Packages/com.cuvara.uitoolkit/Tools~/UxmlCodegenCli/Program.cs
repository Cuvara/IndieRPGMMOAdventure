namespace Cuvara.UIToolkit.Codegen.Cli
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;

    /// <summary>
    /// Drift check: scans the given roots for <c>.uxml</c> files that HAVE a committed
    /// <c>Generated/&lt;Name&gt;.uxml.g.cs</c>, regenerates each in memory through the
    /// same pure core the Editor uses, and byte-compares. Exit 0 when everything matches;
    /// exit 1 listing every drifted or broken file.
    /// </summary>
    /// <remarks>
    /// Un-enrolled UXML (no generated counterpart) is skipped by design — enrollment is
    /// opt-in, and this tool checks the promise only where one was made. Regenerate
    /// locally via the Editor menu (Assets/Cuvara/Generate UXML Bindings) or simply by
    /// re-saving the UXML, then commit the result.
    /// </remarks>
    internal static class Program
    {
        // Never scanned into: build output, VCS internals, and hidden dirs. '~' folders
        // are NOT excluded — Samples~ may legitimately carry enrolled UXML.
        private static readonly string[] ExcludedDirectoryNames = { "obj", "bin", "Library", "Temp", "Logs" };

        private static int Main(string[] args)
        {
            var roots = args.Length > 0 ? args : new[] { "." };
            var checkedCount = 0;
            var drifted = new List<string>();
            var failed = new List<string>();

            foreach (var root in roots)
            {
                if (!Directory.Exists(root))
                {
                    Console.Error.WriteLine($"error: root '{root}' does not exist");
                    return 2;
                }

                foreach (var uxmlPath in EnumerateUxmlFiles(root))
                {
                    if (!UxmlBindingPipeline.IsEnrolled(uxmlPath)) continue;

                    checkedCount++;
                    var generatedPath = UxmlBindingPipeline.GetGeneratedPath(uxmlPath);
                    try
                    {
                        var fresh = UxmlBindingPipeline.GenerateBytes(uxmlPath);
                        var committed = File.ReadAllBytes(generatedPath);
                        if (!fresh.SequenceEqual(committed))
                        {
                            drifted.Add(generatedPath);
                        }
                    }
                    catch (Exception exception)
                    {
                        failed.Add($"{uxmlPath}: {exception.Message}");
                    }
                }
            }

            Console.WriteLine($"uxml-codegen drift check: {checkedCount} enrolled UXML file(s) checked");

            if (failed.Count > 0)
            {
                Console.Error.WriteLine("FAILED to regenerate (fix the UXML or the enrollment):");
                foreach (var failure in failed) Console.Error.WriteLine($"  {failure}");
            }

            if (drifted.Count > 0)
            {
                Console.Error.WriteLine("DRIFTED — committed file no longer matches its UXML; regenerate and commit:");
                foreach (var path in drifted) Console.Error.WriteLine($"  {path}");
            }

            if (failed.Count > 0 || drifted.Count > 0) return 1;

            Console.WriteLine("all generated bindings are up to date");
            return 0;
        }

        private static IEnumerable<string> EnumerateUxmlFiles(string root)
        {
            var pending = new Stack<string>();
            pending.Push(root);

            while (pending.Count > 0)
            {
                var directory = pending.Pop();

                foreach (var child in Directory.GetDirectories(directory))
                {
                    var name = Path.GetFileName(child);
                    if (name.StartsWith(".", StringComparison.Ordinal)) continue;
                    if (ExcludedDirectoryNames.Contains(name)) continue;
                    pending.Push(child);
                }

                foreach (var file in Directory.GetFiles(directory, "*.uxml"))
                {
                    yield return file;
                }
            }
        }
    }
}
