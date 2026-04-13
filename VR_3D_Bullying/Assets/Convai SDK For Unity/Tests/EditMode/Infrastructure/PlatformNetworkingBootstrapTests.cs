using System;
using System.Reflection;
using Convai.Domain.Abstractions;
using Convai.Domain.EventSystem;
using Convai.Domain.Logging;
using Convai.Infrastructure.Networking;
using Convai.Infrastructure.Networking.Bootstrap;
using Convai.Infrastructure.Networking.Transport;
using Convai.Runtime;
using Convai.Shared.DependencyInjection;
using NUnit.Framework;
using UnityEngine;
using ILogger = Convai.Domain.Logging.ILogger;

namespace Convai.Tests.EditMode.Infrastructure
{
    [TestFixture]
    public class PlatformNetworkingBootstrapTests
    {
        [SetUp]
        public void SetUp()
        {
            ResetStatics();
        }

        [TearDown]
        public void TearDown()
        {
            ResetStatics();
        }

        [Test]
        public void EnsureRegistered_SelectsHighestPrioritySupportedRegistrar_Once()
        {
            var lowerPriority = new TestRegistrar("low", priority: 10, supported: true, registerRequiredServices: true);
            var higherPriority = new TestRegistrar("high", priority: 20, supported: true, registerRequiredServices: true);

            PlatformNetworkingRegistrarRegistry.Register(lowerPriority);
            PlatformNetworkingRegistrarRegistry.Register(higherPriority);

            PlatformNetworkingBootstrap.EnsureRegistered(false);
            PlatformNetworkingBootstrap.EnsureRegistered(false);

            Assert.AreEqual(0, lowerPriority.RegisterServicesCallCount);
            Assert.AreEqual(1, higherPriority.RegisterServicesCallCount);
            Assert.AreEqual("high", PlatformNetworkingBootstrap.ActiveRegistrarId);
            Assert.IsTrue(RealtimeTransportFactory.IsFactoryRegistered);
            Assert.IsTrue(ConvaiServiceLocator.IsRegistered<IConvaiRoomControllerFactory>());
            Assert.IsTrue(ConvaiServiceLocator.IsRegistered<IMicrophoneSourceFactory>());
            Assert.IsTrue(ConvaiServiceLocator.IsRegistered<IVideoSourceFactory>());
            Assert.IsTrue(ConvaiServiceLocator.IsRegistered<IAudioStreamFactory>());
            Assert.IsTrue(ConvaiServiceLocator.IsRegistered<IRealtimeTransportAccessor>());
        }

        [Test]
        public void EnsureRegistered_ThrowsWhenRegistrarOmitsRequiredServices()
        {
            PlatformNetworkingRegistrarRegistry.Register(
                new TestRegistrar("broken", priority: 10, supported: true, registerRequiredServices: false));

            var ex = Assert.Throws<PlatformNetworkingBootstrapException>(
                () => PlatformNetworkingBootstrap.EnsureRegistered(false));

            StringAssert.Contains(nameof(IConvaiRoomControllerFactory), ex.Message);
        }

        [Test]
        public void NativeTransportBootstrap_RegistersNativeRegistrarThatCanBeAppliedCentrally()
        {
            Type bootstrapType = Type.GetType(
                "Convai.Infrastructure.Networking.Native.NativeTransportBootstrap, Convai.Infrastructure.Networking.Native");
            Assert.IsNotNull(bootstrapType, "Expected the native bootstrap type to be available.");

            MethodInfo initializeMethod = bootstrapType.GetMethod(
                "Initialize",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.IsNotNull(initializeMethod, "Expected the native bootstrap Initialize method to be available.");

            initializeMethod.Invoke(null, null);

            bool foundNativeRegistrar = false;
            var registrars = PlatformNetworkingRegistrarRegistry.GetRegistrars();
            for (int i = 0; i < registrars.Count; i++)
            {
                if (!string.Equals(registrars[i].Id, "native", StringComparison.Ordinal)) continue;
                foundNativeRegistrar = true;
                break;
            }

            Assert.IsTrue(foundNativeRegistrar, "Expected the native bootstrap to register the native networking registrar.");

            PlatformNetworkingBootstrap.EnsureRegistered(false);

            Assert.AreEqual("native", PlatformNetworkingBootstrap.ActiveRegistrarId);
            Assert.IsTrue(RealtimeTransportFactory.IsFactoryRegistered);
            Assert.IsTrue(ConvaiServiceLocator.IsRegistered<IConvaiRoomControllerFactory>());
            Assert.IsTrue(ConvaiServiceLocator.IsRegistered<IMicrophoneSourceFactory>());
            Assert.IsTrue(ConvaiServiceLocator.IsRegistered<IVideoSourceFactory>());
            Assert.IsTrue(ConvaiServiceLocator.IsRegistered<IAudioStreamFactory>());
            Assert.IsTrue(ConvaiServiceLocator.IsRegistered<IRealtimeTransportAccessor>());
        }

        private static void ResetStatics()
        {
            ConvaiServiceBootstrap.ShutdownManually();

            if (ConvaiServiceLocator.IsInitialized)
            {
                ConvaiServiceLocator.Shutdown();
            }

            PlatformNetworkingBootstrap.ResetForTests();
            PlatformNetworkingRegistrarRegistry.ResetForTests();
            RealtimeTransportFactory.ClearFactory();
        }

        private sealed class TestRegistrar : IPlatformNetworkingRegistrar
        {
            private readonly bool _supported;
            private readonly bool _registerRequiredServices;

            internal TestRegistrar(string id, int priority, bool supported, bool registerRequiredServices)
            {
                Id = id;
                Priority = priority;
                _supported = supported;
                _registerRequiredServices = registerRequiredServices;
            }

            public string Id { get; }
            public int Priority { get; }
            public int RegisterServicesCallCount { get; private set; }

            public bool SupportsCurrentEnvironment() => _supported;

            public void RegisterServices()
            {
                RegisterServicesCallCount++;
                RealtimeTransportFactory.RegisterFactory(static () => null);

                if (!_registerRequiredServices) return;

                ConvaiServiceLocator.Register(
                    ServiceDescriptor.Singleton<IConvaiRoomControllerFactory>(static _ => new TestRoomControllerFactory()));
                ConvaiServiceLocator.Register(
                    ServiceDescriptor.Singleton<IMicrophoneSourceFactory>(static _ => new TestMicrophoneSourceFactory()));
                ConvaiServiceLocator.Register(
                    ServiceDescriptor.Singleton<IVideoSourceFactory>(static _ => new TestVideoSourceFactory()));
                ConvaiServiceLocator.Register(
                    ServiceDescriptor.Singleton<IAudioStreamFactory>(static _ => new TestAudioStreamFactory()));
                ConvaiServiceLocator.Register(
                    ServiceDescriptor.Singleton<IRealtimeTransportAccessor>(static _ => new TestRealtimeTransportAccessor()));
            }
        }

        private sealed class TestRoomControllerFactory : IConvaiRoomControllerFactory
        {
            public IConvaiRoomController Create(
                ICharacterRegistry characterRegistry,
                IPlayerSession playerSession,
                IConfigurationProvider config,
                IMainThreadDispatcher dispatcher,
                ILogger logger,
                IEventHub eventHub,
                INarrativeSectionNameResolver sectionNameResolver = null) => null;
        }

        private sealed class TestMicrophoneSourceFactory : IMicrophoneSourceFactory
        {
            public IMicrophoneSource Create(string deviceName, int deviceIndex = 0, GameObject hostObject = null) => null;
            public string[] GetAvailableDevices() => Array.Empty<string>();
        }

        private sealed class TestVideoSourceFactory : IVideoSourceFactory
        {
            public IVideoSource CreateFromRenderTexture(RenderTexture texture, string name = null) => null;
            public IVideoSource CreateFromCamera(Camera camera, int width, int height, string name = null) => null;
            public IVideoSource CreateFromCanvasCapture(string name = null, int targetFrameRate = 15) => null;
        }

        private sealed class TestAudioStreamFactory : IAudioStreamFactory
        {
            public IDisposable Create(IRemoteAudioTrack track, AudioSource audioSource) => null;
        }

        private sealed class TestRealtimeTransportAccessor : IRealtimeTransportAccessor
        {
            public IRealtimeTransport Transport => null;
        }
    }
}