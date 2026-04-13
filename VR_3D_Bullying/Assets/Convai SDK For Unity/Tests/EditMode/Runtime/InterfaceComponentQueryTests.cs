using System;
using System.Collections.Generic;
using System.Reflection;
using Convai.Runtime.Utilities;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Convai.Tests.EditMode.Runtime
{
    public class InterfaceComponentQueryTests
    {
        private readonly List<GameObject> _createdObjects = new();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _createdObjects.Count; i++)
            {
                GameObject go = _createdObjects[i];
                if (go != null) Object.DestroyImmediate(go);
            }

            _createdObjects.Clear();
        }

        [Test]
        public void FindObjects_Uses_Cached_Implementer_List()
        {
            var probeGo = new GameObject("Probe");
            _createdObjects.Add(probeGo);
            probeGo.AddComponent<SecondaryLocalProbe>();

            InterfaceComponentQuery.FindObjects<ILocalProbe>();

            FieldInfo cacheField = typeof(InterfaceComponentQuery).GetField(
                "ImplementerCache",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.NotNull(cacheField);

            var cache =
                (Dictionary<Type, Type[]>)cacheField.GetValue(null);
            Assert.IsTrue(cache.TryGetValue(typeof(ILocalProbe), out Type[] initialTypes));
            Assert.NotNull(initialTypes);

            InterfaceComponentQuery.FindObjects<ILocalProbe>();

            Assert.IsTrue(cache.TryGetValue(typeof(ILocalProbe), out Type[] subsequentTypes));
            Assert.AreSame(initialTypes, subsequentTypes);
        }

        [Test]
        public void FindObjects_ExcludeInactive_Skips_Inactive_Objects()
        {
            var activeGo = new GameObject("ActiveProbe");
            _createdObjects.Add(activeGo);
            var activeProbe = activeGo.AddComponent<SecondaryLocalProbe>();

            var inactiveGo = new GameObject("InactiveProbe");
            _createdObjects.Add(inactiveGo);
            var inactiveProbe = inactiveGo.AddComponent<SecondaryLocalProbe>();
            inactiveGo.SetActive(false);

            IReadOnlyList<ILocalProbe> discovered = InterfaceComponentQuery.FindObjects<ILocalProbe>();

            Assert.That(discovered, Has.Member(activeProbe));
            Assert.That(discovered, Has.No.Member(inactiveProbe));
        }

        [Test]
        public void FindObjects_IncludeInactive_Returns_Inactive_Objects()
        {
            var activeGo = new GameObject("ActiveProbe");
            _createdObjects.Add(activeGo);
            var activeProbe = activeGo.AddComponent<SecondaryLocalProbe>();

            var inactiveGo = new GameObject("InactiveProbe");
            _createdObjects.Add(inactiveGo);
            var inactiveProbe = inactiveGo.AddComponent<SecondaryLocalProbe>();
            inactiveGo.SetActive(false);

            IReadOnlyList<ILocalProbe> discovered =
                InterfaceComponentQuery.FindObjects<ILocalProbe>(FindObjectsInactive.Include);

            Assert.That(discovered, Has.Member(activeProbe));
            Assert.That(discovered, Has.Member(inactiveProbe));
        }

        [Test]
        public void FindObjects_Deduplicates_When_Base_And_Derived_Implement_Interface()
        {
            var derivedProbeGo = new GameObject("DerivedProbe");
            _createdObjects.Add(derivedProbeGo);
            var derivedProbe = derivedProbeGo.AddComponent<DerivedLocalProbe>();

            IReadOnlyList<ILocalProbe> discovered = InterfaceComponentQuery.FindObjects<ILocalProbe>();

            int derivedInstanceCount = 0;
            for (int i = 0; i < discovered.Count; i++)
                if (ReferenceEquals(discovered[i], derivedProbe))
                    derivedInstanceCount++;

            Assert.AreEqual(1, derivedInstanceCount,
                "Derived probe should not be duplicated when base and derived types are queried.");
        }

        private interface ILocalProbe
        {
            public int Value { get; }
        }

        private class BaseLocalProbe : MonoBehaviour, ILocalProbe
        {
            public virtual int Value => 1;
        }

        private sealed class DerivedLocalProbe : BaseLocalProbe
        {
            public override int Value => 2;
        }

        private sealed class SecondaryLocalProbe : MonoBehaviour, ILocalProbe
        {
            public int Value => 3;
        }
    }
}
