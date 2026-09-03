namespace Cuvara.UIToolkit.Tests
{
    using System;
    using NUnit.Framework;
    using UnityEngine.UIElements;

    /// <summary>
    /// Exercises the COMMITTED generated bindings (<c>Generated/ConfirmPopup.uxml.g.cs</c>,
    /// generated from <c>ConfirmPopup.uxml</c>) rather than a string fixture: the partial
    /// class compiles, <c>AssignQueries</c> wires every property, and a tree missing one
    /// element fails through <c>Require</c>. This is the consuming side of the codegen —
    /// the generator's own behaviour is covered in <c>Tests/Editor</c>.
    /// </summary>
    public class GeneratedConfirmPopupTests
    {
        private static VisualElement BuildMatchingTree()
        {
            // The same shape ConfirmPopup.uxml describes, built directly — CloneTree needs
            // an imported asset, and these tests must also pass where only the compiled
            // code exists.
            var root = new VisualElement();
            var popupRoot = new VisualElement { name = "popup-root" };
            popupRoot.Add(new Label { name = "popup-title" });
            popupRoot.Add(new Label { name = "popup-body" });
            var buttonRow = new VisualElement { name = "button-row" };
            buttonRow.Add(new Button { name = "btn-ok" });
            buttonRow.Add(new Button { name = "btn-cancel" });
            popupRoot.Add(buttonRow);
            root.Add(popupRoot);
            return root;
        }

        [Test]
        public void AssignQueries_WiresEveryProperty()
        {
            var root = BuildMatchingTree();
            var popup = new ConfirmPopup();

            popup.AssignQueries(root);

            Assert.That(popup.PopupRoot, Is.SameAs(root.Q<VisualElement>("popup-root")));
            Assert.That(popup.PopupTitle, Is.SameAs(root.Q<Label>("popup-title")));
            Assert.That(popup.PopupBody, Is.SameAs(root.Q<Label>("popup-body")));
            Assert.That(popup.ButtonRow, Is.SameAs(root.Q<VisualElement>("button-row")));
            Assert.That(popup.BtnOk, Is.SameAs(root.Q<Button>("btn-ok")));
            Assert.That(popup.BtnCancel, Is.SameAs(root.Q<Button>("btn-cancel")));
        }

        [Test]
        public void AMissingElement_FailsLoudly_NamingIt()
        {
            var root = BuildMatchingTree();
            root.Q<Button>("btn-cancel").RemoveFromHierarchy();

            var exception = Assert.Throws<InvalidOperationException>(() => new ConfirmPopup().AssignQueries(root));

            Assert.That(exception.Message, Does.Contain("btn-cancel"));
        }
    }
}
