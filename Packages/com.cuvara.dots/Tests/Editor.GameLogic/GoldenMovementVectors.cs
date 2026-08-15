using System;
using System.IO;
using UnityEditor.PackageManager;
using UnityEngine;

namespace Cuvara.DOTS.Tests.GameLogic
{
    /// <summary>
    /// Loads the ADR-10 movement fixtures that ship inside <c>com.rpgmmo.shared-gamelogic</c>, and
    /// compares floats bit-for-bit.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The same files are replayed by the server's suite and by
    /// <c>com.cuvara.netcode/Tests/Editor/GoldenVectorTests.cs</c>. This copy exists because the two
    /// packages cannot reference each other's test assemblies, and it is deliberately narrower —
    /// only <c>movement.json</c>, because movement is the only rule the seam currently routes.
    /// Combat and validation stay the netcode package's job until the seam exposes them.
    /// </para>
    /// <para>
    /// Bits, not tolerance. A tolerance comparison passes on precisely the divergence these fixtures
    /// exist to catch, and it also calls <c>0f</c> equal to <c>-0f</c> and <c>NaN</c> unequal to
    /// itself.
    /// </para>
    /// </remarks>
    internal static class GoldenMovementVectors
    {
        private const string PackageName = "com.rpgmmo.shared-gamelogic";

        /// <summary>
        /// Fixture directory, resolved through the package manager rather than by an
        /// <c>Assets/</c>-relative path: a git package lives under
        /// <c>Library/PackageCache/&lt;name&gt;@&lt;hash&gt;</c> and the hash moves with the tag.
        /// </summary>
        private static string Directory
        {
            get
            {
                var package = PackageInfo.FindForPackageName(PackageName);
                if (package == null)
                {
                    throw new DirectoryNotFoundException(
                        $"package '{PackageName}' is not resolved — check Packages/manifest.json");
                }

                var directory = Path.Combine(package.resolvedPath, "GoldenVectors");
                if (!System.IO.Directory.Exists(directory))
                {
                    throw new DirectoryNotFoundException(
                        $"'{directory}' does not exist — the pinned tag ships no golden vectors");
                }

                return directory;
            }
        }

        /// <summary>
        /// Reads <c>movement.json</c>. A fixture that cannot be read must surface as one loud
        /// failure, never as an empty and silently green suite.
        /// </summary>
        public static MovementCase[] Load()
        {
            var json = File.ReadAllText(Path.Combine(Directory, "movement.json"));
            var document = JsonUtility.FromJson<MovementCaseFile>(json);
            var cases = document?.cases;
            if (cases == null || cases.Length == 0)
            {
                throw new InvalidDataException("movement.json produced no cases");
            }

            return cases;
        }

        public static string Hex(float value) =>
            "0x" + unchecked((uint)BitConverter.SingleToInt32Bits(value)).ToString("X8");

        public static float Float(string hex)
        {
            if (string.IsNullOrEmpty(hex))
            {
                throw new ArgumentException("empty float literal in a fixture", nameof(hex));
            }

            var digits = hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? hex.Substring(2) : hex;
            return BitConverter.Int32BitsToSingle(unchecked((int)Convert.ToUInt32(digits, 16)));
        }

        public static void AssertBitEqual(string expectedHex, float actual, string because)
        {
            var expectedBits = unchecked((int)Convert.ToUInt32(expectedHex.Substring(2), 16));
            var actualBits = BitConverter.SingleToInt32Bits(actual);
            if (expectedBits != actualBits)
            {
                throw new NUnit.Framework.AssertionException(
                    $"{because}: expected {expectedHex} ({Float(expectedHex)}), got {Hex(actual)} ({actual})");
            }
        }
    }

    /// <summary>Fixture schema. Public fields on a [Serializable] class is what JsonUtility binds.</summary>
    [Serializable]
    public sealed class MovementCaseFile
    {
        public MovementCase[] cases;
    }

    /// <inheritdoc cref="MovementCaseFile"/>
    [Serializable]
    public sealed class MovementCase
    {
        public string name;
        public string posX;
        public string posY;
        public string moveX;
        public string moveY;
        public string speed;
        public string dt;
        public string minX;
        public string minY;
        public string maxX;
        public string maxY;
        public bool dead;
        public string expectedResult;
        public string expectedX;
        public string expectedY;
    }
}
