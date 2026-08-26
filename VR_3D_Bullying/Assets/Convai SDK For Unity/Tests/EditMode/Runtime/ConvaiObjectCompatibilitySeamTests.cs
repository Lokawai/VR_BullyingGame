using Convai.Shared.Compatibility;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Convai.Tests.EditMode.Runtime
{
    /// <summary>
    ///     Behavioural cover for the version-compatibility seams. The guard test proves nothing
    ///     bypasses them; this proves they answer correctly on the editor the suite runs on.
    /// </summary>
    /// <remarks>
    ///     Ids are session-scoped by contract, so these assertions deliberately check identity and
    ///     distinctness rather than any particular numeric value — the value differs by editor band
    ///     (see <see cref="ConvaiObjectId" />).
    /// </remarks>
    [TestFixture]
    public sealed class ConvaiObjectCompatibilitySeamTests
    {
        [SetUp]
        public void SetUp()
        {
            _active = new GameObject(ActiveName);
            _inactive = new GameObject(InactiveName);
            _inactive.SetActive(false);
        }

        [TearDown]
        public void TearDown()
        {
            if (_active != null) Object.DestroyImmediate(_active);
            if (_inactive != null) Object.DestroyImmediate(_inactive);
        }

        private const string ActiveName = "ConvaiSeamTests_Active";
        private const string InactiveName = "ConvaiSeamTests_Inactive";

        private GameObject _active;
        private GameObject _inactive;

        [Test]
        public void Of_IsStableForTheSameObject_DistinctAcrossObjects_AndZeroForNull()
        {
            long activeId = ConvaiObjectId.Of(_active);

            Assert.That(activeId, Is.Not.Zero, "A live object must have a non-zero id.");
            Assert.That(ConvaiObjectId.Of(_active), Is.EqualTo(activeId),
                "The same object must report the same id within a session.");
            Assert.That(ConvaiObjectId.Of(_inactive), Is.Not.EqualTo(activeId),
                "Two distinct objects must not share an id.");
            Assert.That(ConvaiObjectId.Of(null), Is.Zero, "A null object must report zero, not throw.");
        }

        [Test]
        public void TryResolve_RoundTripsAnObjectThroughItsId()
        {
            long id = ConvaiObjectId.Of(_active);

            Assert.That(ConvaiObjectId.TryResolve(id, out Object resolved), Is.True,
                "A freshly minted id must resolve back to its object.");
            Assert.That(resolved, Is.SameAs(_active));

            Assert.That(ConvaiObjectId.TryResolve(0L, out Object none), Is.False,
                "Zero is the documented 'no object' id.");
            Assert.That(none, Is.Null);
        }

        [Test]
        public void TryResolve_ReturnsFalseForAnIdThatNoLongerNamesAnything()
        {
            long id = ConvaiObjectId.Of(_active);
            Object.DestroyImmediate(_active);
            _active = null;

            Assert.That(ConvaiObjectId.TryResolve(id, out Object resolved), Is.False,
                "An id whose object is gone must report failure, not hand back a destroyed reference.");
            Assert.That(resolved, Is.Null);
        }

        [Test]
        public void TryResolveTyped_UnwrapsBetweenGameObjectAndComponent()
        {
            BoxCollider component = _active.AddComponent<BoxCollider>();

            Assert.That(ConvaiObjectId.TryResolve(ConvaiObjectId.Of(_active), out BoxCollider fromGameObject),
                Is.True, "A GameObject's id must resolve to a component it carries.");
            Assert.That(fromGameObject, Is.SameAs(component));

            Assert.That(ConvaiObjectId.TryResolve(ConvaiObjectId.Of(component), out GameObject fromComponent),
                Is.True, "A component's id must resolve to its GameObject.");
            Assert.That(fromComponent, Is.SameAs(_active));

            Assert.That(ConvaiObjectId.TryResolve(ConvaiObjectId.Of(_active), out Light absent), Is.False,
                "Asking for a component the object does not carry must fail, not throw.");
            Assert.That(absent, Is.Null);
        }

        [Test]
        public void All_BoolOverload_MatchesTheExplicitInactiveMode()
        {
            Assert.That(ConvaiObjectFind.All<GameObject>(true),
                Is.EquivalentTo(ConvaiObjectFind.All<GameObject>(FindObjectsInactive.Include)),
                "All(true) must mean Include.");
            Assert.That(ConvaiObjectFind.All<GameObject>(false),
                Is.EquivalentTo(ConvaiObjectFind.All<GameObject>(FindObjectsInactive.Exclude)),
                "All(false) must mean Exclude.");
        }

        [Test]
        public void All_HonoursTheInactiveSelection()
        {
            GameObject[] included = ConvaiObjectFind.All<GameObject>(FindObjectsInactive.Include);
            GameObject[] excluded = ConvaiObjectFind.All<GameObject>(FindObjectsInactive.Exclude);

            Assert.That(included, Has.Member(_active));
            Assert.That(included, Has.Member(_inactive),
                "Include must return objects on inactive GameObjects.");
            Assert.That(excluded, Has.Member(_active));
            Assert.That(excluded, Has.No.Member(_inactive),
                "Exclude must leave inactive GameObjects out.");
        }
    }
}
