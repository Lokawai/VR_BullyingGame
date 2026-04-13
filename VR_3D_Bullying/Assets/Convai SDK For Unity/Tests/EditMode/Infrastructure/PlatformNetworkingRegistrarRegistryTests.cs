using Convai.Infrastructure.Networking.Bootstrap;
using NUnit.Framework;

namespace Convai.Tests.EditMode.Infrastructure
{
    [TestFixture]
    public class PlatformNetworkingRegistrarRegistryTests
    {
        [SetUp]
        public void SetUp()
        {
            PlatformNetworkingRegistrarRegistry.ResetForTests();
        }

        [TearDown]
        public void TearDown()
        {
            PlatformNetworkingRegistrarRegistry.ResetForTests();
        }

        [Test]
        public void Register_IgnoresDuplicateRegistrarOfSameTypeAndId()
        {
            PlatformNetworkingRegistrarRegistry.Register(new DuplicateSafeRegistrar());
            PlatformNetworkingRegistrarRegistry.Register(new DuplicateSafeRegistrar());

            var registrars = PlatformNetworkingRegistrarRegistry.GetRegistrars();

            Assert.AreEqual(1, registrars.Count);
            Assert.AreEqual("duplicate-safe", registrars[0].Id);
        }

        [Test]
        public void Register_ThrowsWhenDifferentRegistrarTypeReusesSameId()
        {
            PlatformNetworkingRegistrarRegistry.Register(new DuplicateSafeRegistrar());

            var ex = Assert.Throws<System.InvalidOperationException>(
                () => PlatformNetworkingRegistrarRegistry.Register(new ConflictingRegistrar()));

            StringAssert.Contains("duplicate-safe", ex.Message);
        }

        private sealed class DuplicateSafeRegistrar : IPlatformNetworkingRegistrar
        {
            public string Id => "duplicate-safe";
            public int Priority => 0;
            public bool SupportsCurrentEnvironment() => true;
            public void RegisterServices() { }
        }

        private sealed class ConflictingRegistrar : IPlatformNetworkingRegistrar
        {
            public string Id => "duplicate-safe";
            public int Priority => 10;
            public bool SupportsCurrentEnvironment() => true;
            public void RegisterServices() { }
        }
    }
}