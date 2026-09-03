namespace Cuvara.UIToolkit.Codegen
{
    using System;

    /// <summary>
    /// Raised when a UXML document cannot produce a valid bindings class: duplicate
    /// <c>name</c> values, names whose PascalCase forms collide, a name that yields no
    /// identifier, or a property that would collide with the class name itself.
    /// </summary>
    /// <remarks>
    /// A distinct type rather than <see cref="InvalidOperationException"/> so the Editor
    /// integration and the drift CLI can tell "this UXML is malformed for codegen" (report
    /// it, keep going) apart from a genuine bug. Unity-free on purpose — see
    /// <see cref="UxmlBindingGenerator"/>.
    /// </remarks>
    public sealed class UxmlCodegenException : Exception
    {
        public UxmlCodegenException(string message)
            : base(message)
        {
        }
    }
}
