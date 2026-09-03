namespace Cuvara.UIToolkit.Codegen
{
    using System.Text;

    /// <summary>
    /// Converts UXML <c>name</c> attribute values (conventionally kebab-case) into C#
    /// PascalCase identifiers.
    /// </summary>
    /// <remarks>
    /// <para>Every non-alphanumeric character is a separator, so <c>popup-title</c>,
    /// <c>popup_title</c> and <c>popup.title</c> all become <c>PopupTitle</c>. Consecutive
    /// separators collapse (<c>a--b</c> → <c>AB</c>), a leading separator is dropped
    /// (<c>-x</c> → <c>X</c>), and digits are kept verbatim (<c>slot-2</c> → <c>Slot2</c>).
    /// A name whose identifier would START with a digit gets a leading underscore, because
    /// C# forbids the bare form.</para>
    ///
    /// <para><b>This file must stay Unity-free.</b> It is compiled outside Unity by the
    /// drift-check CLI under <c>Tools~/UxmlCodegenCli/</c>; a <c>UnityEngine</c> or
    /// <c>UnityEditor</c> using here breaks that build.</para>
    /// </remarks>
    public static class UxmlIdentifier
    {
        /// <summary>PascalCase identifier for a UXML name, or an empty string when the
        /// name contains no letter or digit at all.</summary>
        public static string ToPascalCase(string uxmlName)
        {
            if (string.IsNullOrEmpty(uxmlName)) return string.Empty;

            var builder = new StringBuilder(uxmlName.Length);
            var startOfSegment = true;
            foreach (var character in uxmlName)
            {
                if (!char.IsLetterOrDigit(character))
                {
                    startOfSegment = true;
                    continue;
                }

                builder.Append(startOfSegment ? char.ToUpperInvariant(character) : character);
                startOfSegment = false;
            }

            if (builder.Length == 0) return string.Empty;
            if (char.IsDigit(builder[0])) builder.Insert(0, '_');
            return builder.ToString();
        }
    }
}
