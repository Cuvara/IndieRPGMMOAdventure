namespace Cuvara.UIToolkit.Tests
{
    using System;
    using Cuvara.UIToolkit.Utilities;
    using NUnit.Framework;
    using UnityEngine.UIElements;

    /// <summary>
    /// <c>Require&lt;T&gt;</c> is <c>Q&lt;T&gt;</c> with the null branch turned into an
    /// exception. What is worth pinning is the exception's CONTENT: the message must name
    /// the missing element, its expected type and the root it was sought under, because
    /// that message is the entire debugging experience when a UXML edit breaks a binding.
    /// </summary>
    public class RequireQueryTests
    {
        [Test]
        public void AnExistingElement_IsReturned()
        {
            var root = new VisualElement { name = "popup-root" };
            var title = new Label { name = "popup-title" };
            root.Add(title);

            Assert.That(root.Require<Label>("popup-title"), Is.SameAs(title));
        }

        [Test]
        public void AnElementNestedDeeper_IsStillFound()
        {
            // Require wraps Q, which searches the whole subtree — not just direct children.
            var root = new VisualElement { name = "popup-root" };
            var row = new VisualElement { name = "button-row" };
            var ok = new Button { name = "btn-ok" };
            root.Add(row);
            row.Add(ok);

            Assert.That(root.Require<Button>("btn-ok"), Is.SameAs(ok));
        }

        [Test]
        public void AMissingElement_Throws_NamingElementTypeAndRoot()
        {
            var root = new VisualElement { name = "popup-root" };

            var exception = Assert.Throws<InvalidOperationException>(() => root.Require<Label>("popup-title"));

            Assert.That(exception.Message, Does.Contain("popup-title"), "must name the missing element");
            Assert.That(exception.Message, Does.Contain(nameof(Label)), "must name the expected type");
            Assert.That(exception.Message, Does.Contain("popup-root"), "must name the root searched under");
            Assert.That(exception.Message, Does.Contain("name"), "must hint at the UXML name attribute");
        }

        [Test]
        public void AWrongTypedElement_Throws()
        {
            // The name exists — on a Label. Requiring a Button must fail, and the message
            // must carry the type that was expected, because "it's there, why does it
            // throw" is exactly the situation the message has to explain.
            var root = new VisualElement { name = "popup-root" };
            root.Add(new Label { name = "btn-ok" });

            var exception = Assert.Throws<InvalidOperationException>(() => root.Require<Button>("btn-ok"));

            Assert.That(exception.Message, Does.Contain("btn-ok"));
            Assert.That(exception.Message, Does.Contain(nameof(Button)));
        }

        [Test]
        public void ANullRoot_ThrowsArgumentNull()
        {
            VisualElement root = null;
            Assert.Throws<ArgumentNullException>(() => root.Require<Label>("popup-title"));
        }

        [Test]
        public void AnEmptyName_ThrowsArgument()
        {
            Assert.Throws<ArgumentException>(() => new VisualElement().Require<Label>(""));
        }
    }
}
