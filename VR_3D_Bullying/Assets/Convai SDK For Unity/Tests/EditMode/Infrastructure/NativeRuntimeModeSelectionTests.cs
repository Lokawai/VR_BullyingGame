using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Convai.Domain.EventSystem;
using Convai.Domain.Logging;
using Convai.Domain.Models;
using Convai.Infrastructure.Networking;
using Convai.Infrastructure.Networking.Models;
using Convai.Infrastructure.Networking.Transport;
using Convai.Runtime;
using Convai.Runtime.Networking.Media;
using Convai.Shared.Types;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using ILogger = Convai.Domain.Logging.ILogger;
using Object = UnityEngine.Object;

namespace Convai.Tests.EditMode.Infrastructure
{
    [TestFixture]
    public class NativeRuntimeModeSelectionTests
    {
        [Test]
        public void ConvaiSettings_DefaultsNativeRuntimeMode_ToTransport()
        {
            var settings = ScriptableObject.CreateInstance<ConvaiSettings>();
            try
            {
                CollectionAssert.AreEqual(new[] { nameof(NativeRuntimeMode.Transport) },
                    Enum.GetNames(typeof(NativeRuntimeMode)));
                Assert.AreEqual(NativeRuntimeMode.Transport, settings.NativeRuntimeMode);
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void ConvaiSettings_InstanceLoadsTransportDefault_FromSerializedAsset()
        {
            ConvaiSettings originalSettings = GetConvaiSettingsInstance();
            var rolloutSettings = Resources.Load<ConvaiSettings>("ConvaiSettings");

            try
            {
                Assert.IsNotNull(rolloutSettings,
                    "The serialized ConvaiSettings asset should exist in Resources.");

                SetConvaiSettingsInstance(null);

                Assert.AreEqual(NativeRuntimeMode.Transport, ConvaiSettings.Instance.NativeRuntimeMode,
                    "The serialized ConvaiSettings asset should resolve to the transport-only runtime.");
            }
            finally
            {
                SetConvaiSettingsInstance(originalSettings);
            }
        }

        [Test]
        public void ConvaiSettingsAsset_SerializesTransportRuntimeModeOnly()
        {
            var settingsAsset = AssetDatabase.LoadAssetAtPath<ConvaiSettings>("Assets/Resources/ConvaiSettings.asset");

            Assert.IsNotNull(settingsAsset,
                "The landing set should retain the ConvaiSettings asset with an explicit transport-only runtime value.");

            SerializedObject serializedSettings = new(settingsAsset);
            SerializedProperty nativeRuntimeModeProperty = serializedSettings.FindProperty("_nativeRuntimeMode");

            Assert.IsNotNull(nativeRuntimeModeProperty,
                "The serialized ConvaiSettings asset should still contain the native runtime mode field for migration normalization.");
            Assert.AreEqual((int)NativeRuntimeMode.Transport, nativeRuntimeModeProperty.intValue,
                "The serialized ConvaiSettings asset should no longer retain a stale direct runtime value after the transport-only cleanup wave.");
        }

        [Test]
        public void TransportOnlyLandingSet_RemovesLegacyDirectSelectionInfrastructure()
        {
            Assert.IsNull(
                Type.GetType(
                    "Convai.Infrastructure.Networking.Native.NativeRuntimeModeResolver, Convai.Infrastructure.Networking.Native"),
                "The legacy native runtime mode resolver should be deleted once the native runtime becomes transport-only.");

            AssertConstructorContainsOnlyTransportFactory(
                "Convai.Infrastructure.Networking.Native.NativeRealtimeTransportAccessor",
                "Transport-only accessors should not retain runtime-mode selector injection.");
            AssertConstructorContainsOnlyTransportFactory(
                "Convai.Infrastructure.Networking.Native.NativeRoomControllerFactory",
                "Transport-only factories should not retain runtime-mode selector or rollback injection.");
        }

        [Test]
        public void NativeBootstrapAssembly_IsExplicitlyPreservedAgainstIL2CPPStripping()
        {
            Type bootstrapType = ResolveNativeType("Convai.Infrastructure.Networking.Native.NativeTransportBootstrap");
            Assert.IsNotNull(bootstrapType, "Expected the native bootstrap type to be available in EditMode tests.");

            Assembly assembly = bootstrapType.Assembly;
            Assert.IsTrue(HasAttribute(assembly, "UnityEngine.Scripting.AlwaysLinkAssemblyAttribute"),
                "The native networking assembly should be marked with [assembly: AlwaysLinkAssembly] so Quest/IL2CPP builds keep the runtime bootstrap.");
            Assert.IsTrue(HasAttribute(assembly, "UnityEngine.Scripting.PreserveAttribute"),
                "The native networking assembly should be marked with [assembly: Preserve] so Quest/IL2CPP builds keep the runtime bootstrap.");
            Assert.IsTrue(HasAttribute(bootstrapType, "UnityEngine.Scripting.PreserveAttribute"),
                "NativeTransportBootstrap should be marked with [Preserve] so the bootstrap type itself is not stripped.");

            MethodInfo initializeMethod = bootstrapType.GetMethod(
                "Initialize",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.IsNotNull(initializeMethod,
                "Expected the native bootstrap to expose an Initialize method for RuntimeInitializeOnLoadMethod registration.");
            Assert.IsTrue(HasAttribute(initializeMethod, "UnityEngine.Scripting.PreserveAttribute"),
                "NativeTransportBootstrap.Initialize should be marked with [Preserve] so IL2CPP keeps the method entry point.");
        }

        [Test]
        public void LinkXml_PreservesNativeNetworkingAssembly_ForIL2CPPPlayers()
        {
            string linkXmlPath = Path.GetFullPath(
                Path.Combine(UnityEngine.Application.dataPath, "..", "Packages/com.convai.convai-sdk-for-unity/SDK/link.xml"));

            Assert.IsTrue(File.Exists(linkXmlPath), $"Expected link.xml to exist at '{linkXmlPath}'.");

            string linkXml = File.ReadAllText(linkXmlPath);
            StringAssert.Contains(
                "<assembly fullname=\"Convai.Infrastructure.Networking.Native\" preserve=\"all\"/>",
                linkXml,
                "link.xml should preserve the native networking assembly so Quest/IL2CPP builds keep the room-controller bootstrap types.");
        }

        [Test]
        public void NativeRuntimeSelection_CollapsesLegacyConfigAndDiagnostics_ToTransportOnly()
        {
            ConvaiSettings originalSettings = GetConvaiSettingsInstance();
            var settings = ScriptableObject.CreateInstance<ConvaiSettings>();
            TestLogger logger = new();

            try
            {
                SetConvaiSettingsInstance(settings);
                SetSerializedNativeRuntimeModeValue(settings, 0);

                Assert.AreEqual(NativeRuntimeMode.Transport, settings.NativeRuntimeMode,
                    "Stale serialized native runtime values should collapse to the transport-only runtime.");
                Assert.IsNull(typeof(IRealtimeTransportAccessor).GetProperty("UsesTransportConnectionPath"),
                    "The transport accessor should not expose the deleted direct-vs-transport observability flag.");

                var accessorTransport = new TestRealtimeTransport();
                int accessorTransportCalls = 0;
                var accessor = CreateInternal<IRealtimeTransportAccessor>(
                    "Convai.Infrastructure.Networking.Native.NativeRealtimeTransportAccessor",
                    new Func<IRealtimeTransport>(() =>
                    {
                        accessorTransportCalls++;
                        return accessorTransport;
                    }));

                TestRoomFacade transportRoom = new();
                var factoryTransport = new TestRealtimeTransport { Room = transportRoom };
                int factoryTransportCalls = 0;
                var factory = CreateInternal<IConvaiRoomControllerFactory>(
                    "Convai.Infrastructure.Networking.Native.NativeRoomControllerFactory",
                    new Func<IRealtimeTransport>(() =>
                    {
                        factoryTransportCalls++;
                        return factoryTransport;
                    }));

                Assert.AreSame(accessorTransport, accessor.Transport);
                Assert.AreEqual(1, accessorTransportCalls);

                IConvaiRoomController controller = factory.Create(
                    new TestCharacterRegistry(),
                    new TestPlayerSession(),
                    new TestConfigurationProvider(),
                    new ImmediateDispatcher(),
                    logger,
                    new EventHub(new ImmediateScheduler()));

                Assert.AreSame(transportRoom, controller.CurrentRoom);
                Assert.AreEqual(1, factoryTransportCalls);
                Assert.AreEqual(1, logger.Entries.Count,
                    "Creating the native room controller should emit a single transport-only diagnostic log entry.");
                Assert.AreEqual(LogLevel.Info, logger.Entries[0].Level);
                Assert.AreEqual(LogCategory.Transport, logger.Entries[0].Category);
                StringAssert.Contains("transport-backed", logger.Entries[0].Message);
                StringAssert.DoesNotContain("Direct", logger.Entries[0].Message);
                StringAssert.DoesNotContain("fallback", logger.Entries[0].Message);
                StringAssert.DoesNotContain("rollback", logger.Entries[0].Message);
            }
            finally
            {
                SetConvaiSettingsInstance(originalSettings);
                Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void NativeRealtimeTransportAccessor_ReturnsTransportInstance()
        {
            var transport = new TestRealtimeTransport();
            int createTransportCalls = 0;
            var accessor = CreateInternal<IRealtimeTransportAccessor>(
                "Convai.Infrastructure.Networking.Native.NativeRealtimeTransportAccessor",
                new Func<IRealtimeTransport>(() =>
                {
                    createTransportCalls++;
                    return transport;
                }));

            Assert.AreSame(transport, accessor.Transport);
            Assert.AreEqual(1, createTransportCalls);
        }

        [Test]
        public void NativeRoomControllerFactory_CreatesTransportBackedController()
        {
            var transport = new TestRealtimeTransport();
            int createTransportCalls = 0;
            var factory = CreateInternal<IConvaiRoomControllerFactory>(
                "Convai.Infrastructure.Networking.Native.NativeRoomControllerFactory",
                new Func<IRealtimeTransport>(() =>
                {
                    createTransportCalls++;
                    return transport;
                }));

            IConvaiRoomController controller = factory.Create(
                new TestCharacterRegistry(),
                new TestPlayerSession(),
                new TestConfigurationProvider(),
                new ImmediateDispatcher(),
                new TestLogger(),
                new EventHub(new ImmediateScheduler()));

            Assert.IsNull(
                controller.GetType()
                    .GetProperty("UsesTransportAbstraction", BindingFlags.Instance | BindingFlags.Public),
                "Controller should not retain the deleted transport-vs-direct support property.");
            Assert.AreEqual(1, createTransportCalls);
        }

        [Test]
        public void NativeRoomController_CurrentRoom_ResolvesTransportRoom()
        {
            var transportRoom = new TestRoomFacade();
            var transport = new TestRealtimeTransport { Room = transportRoom };

            int createTransportCalls = 0;
            var factory = CreateInternal<IConvaiRoomControllerFactory>(
                "Convai.Infrastructure.Networking.Native.NativeRoomControllerFactory",
                new Func<IRealtimeTransport>(() =>
                {
                    createTransportCalls++;
                    return transport;
                }));

            IConvaiRoomController controller = factory.Create(
                new TestCharacterRegistry(),
                new TestPlayerSession(),
                new TestConfigurationProvider(),
                new ImmediateDispatcher(),
                new TestLogger(),
                new EventHub(new ImmediateScheduler()));

            Assert.IsNotNull(controller.CurrentRoom);
            Assert.AreSame(transportRoom, controller.CurrentRoom);
            Assert.AreEqual(1, createTransportCalls);
        }

        [Test]
        public void
            NativeRoomController_ApplyRemoteAudioPreference_UsesRoomFacadeControlAndRaisesRemoteAudioStateEventsInTransportMode()
        {
            const string characterId = "char-1";
            const string participantId = "participant-1";
            const string participantIdentity = "remote-identity";

            TestRemoteAudioTrack track = new();
            TestRemoteParticipant participant = new(participantId, participantIdentity, track);
            TestRoomFacade transportRoom = new() { RemoteParticipants = new[] { participant } };

            TestRealtimeTransport transport = new() { Room = transportRoom };

            var factory = CreateInternal<IConvaiRoomControllerFactory>(
                "Convai.Infrastructure.Networking.Native.NativeRoomControllerFactory",
                new Func<IRealtimeTransport>(() => transport));

            CharacterDescriptor descriptor = new("instance-1", characterId, "Character", participantId, false);
            IConvaiRoomController controller = factory.Create(
                new TestCharacterRegistry(descriptor),
                new TestPlayerSession(),
                new TestConfigurationProvider(),
                new ImmediateDispatcher(),
                new TestLogger(),
                new EventHub(new ImmediateScheduler()));

            string unsubscribedParticipantId = null;
            string unsubscribedCharacterId = null;
            IRemoteAudioTrack subscribedTrack = null;
            string subscribedParticipantId = null;
            string subscribedCharacterId = null;
            int subscribedCount = 0;

            controller.OnRemoteAudioTrackUnsubscribed += (sid, unsubscribedCharacter) =>
            {
                unsubscribedParticipantId = sid;
                unsubscribedCharacterId = unsubscribedCharacter;
            };

            controller.OnRemoteAudioTrackSubscribed += (audioTrack, sid, resolvedCharacterId) =>
            {
                subscribedTrack = audioTrack;
                subscribedParticipantId = sid;
                subscribedCharacterId = resolvedCharacterId;
                subscribedCount++;
            };

            controller.ApplyRemoteAudioPreference(characterId, false);
            controller.ApplyRemoteAudioPreference(characterId, true);

            CollectionAssert.AreEqual(new[] { false, true }, track.EnabledValues);
            Assert.AreEqual(participantId, unsubscribedParticipantId);
            Assert.AreEqual(characterId, unsubscribedCharacterId);
            Assert.AreSame(track, subscribedTrack);
            Assert.AreEqual(participantId, subscribedParticipantId);
            Assert.AreEqual(characterId, subscribedCharacterId);
            Assert.AreEqual(1, subscribedCount);
        }

        [Test]
        public void
            NativeRoomController_TransportMode_TrackSubscribed_RaisesRemoteAudioSubscribedEvent_WhenPolicyAllows()
        {
            const string characterId = "char-1";
            const string participantId = "participant-1";
            const string participantIdentity = "remote-identity";

            TestRemoteAudioTrack track = new();
            TestRemoteParticipant participant = new(participantId, participantIdentity, track);
            TestRealtimeTransport transport = new()
            {
                Room = new TestRoomFacade { RemoteParticipants = new[] { participant } }
            };

            var factory = CreateInternal<IConvaiRoomControllerFactory>(
                "Convai.Infrastructure.Networking.Native.NativeRoomControllerFactory",
                new Func<IRealtimeTransport>(() => transport));

            CharacterDescriptor descriptor = new("instance-1", characterId, "Character", participantId, false);
            IConvaiRoomController controller = factory.Create(
                new TestCharacterRegistry(descriptor),
                new TestPlayerSession(),
                new TestConfigurationProvider(),
                new ImmediateDispatcher(),
                new TestLogger(),
                new EventHub(new ImmediateScheduler()));

            controller.SetAudioSubscriptionPolicy(identity =>
                string.Equals(identity, participantIdentity, StringComparison.Ordinal));

            IRemoteAudioTrack subscribedTrack = null;
            string subscribedParticipantId = null;
            string subscribedCharacterId = null;
            int subscribedCount = 0;
            controller.OnRemoteAudioTrackSubscribed += (audioTrack, sid, resolvedCharacterId) =>
            {
                subscribedTrack = audioTrack;
                subscribedParticipantId = sid;
                subscribedCharacterId = resolvedCharacterId;
                subscribedCount++;
            };

            InvokeNonPublicInstanceMethod(
                controller,
                "OnTransportTrackSubscribed",
                new TrackInfo(track.Sid, participantId, participantIdentity, TrackKind.Audio, track.Name));

            Assert.AreEqual(1, subscribedCount,
                "Transport-selected mode should surface the same remote-audio subscribed event contract instead of silently skipping it.");
            Assert.AreSame(track, subscribedTrack);
            Assert.AreEqual(participantId, subscribedParticipantId);
            Assert.AreEqual(characterId, subscribedCharacterId);
            Assert.IsEmpty(track.EnabledValues,
                "Allowing the subscription policy should not force-disable the resolved transport audio track.");
        }

        [Test]
        public void
            NativeRoomController_TransportMode_TrackSubscribed_WaitsForRoomFacadeTrackWrapper_WhenTransportMetadataArrivesFirst()
        {
            const string characterId = "char-1";
            const string participantId = "participant-1";
            const string participantIdentity = "convai-bot";

            TestRemoteAudioTrack track = new();
            TestRemoteParticipant participant = new(participantId, participantIdentity);
            TestRoomFacade room = new() { RemoteParticipants = new[] { participant } };
            TestRealtimeTransport transport = new() { Room = room };

            var factory = CreateInternal<IConvaiRoomControllerFactory>(
                "Convai.Infrastructure.Networking.Native.NativeRoomControllerFactory",
                new Func<IRealtimeTransport>(() => transport));

            CharacterDescriptor descriptor = new("instance-1", characterId, "Character", participantId, false);
            IConvaiRoomController controller = factory.Create(
                new TestCharacterRegistry(descriptor),
                new TestPlayerSession(),
                new TestConfigurationProvider(),
                new ImmediateDispatcher(),
                new TestLogger(),
                new EventHub(new ImmediateScheduler()));

            controller.SetAudioSubscriptionPolicy(identity =>
                string.Equals(identity, participantIdentity, StringComparison.Ordinal));

            IRemoteAudioTrack subscribedTrack = null;
            string subscribedParticipantId = null;
            string subscribedCharacterId = null;
            int subscribedCount = 0;
            controller.OnRemoteAudioTrackSubscribed += (audioTrack, sid, resolvedCharacterId) =>
            {
                subscribedTrack = audioTrack;
                subscribedParticipantId = sid;
                subscribedCharacterId = resolvedCharacterId;
                subscribedCount++;
            };

            InvokeNonPublicInstanceMethod(
                controller,
                "OnTransportTrackSubscribed",
                new TrackInfo(track.Sid, participantId, participantIdentity, TrackKind.Audio, track.Name));

            Assert.AreEqual(0, subscribedCount,
                "Transport metadata alone should not raise the subscribed event before the room facade exposes the resolved audio track.");

            room.RaiseAudioTrackSubscribed(track, participant);

            Assert.AreEqual(1, subscribedCount);
            Assert.AreSame(track, subscribedTrack);
            Assert.AreEqual(participantId, subscribedParticipantId);
            Assert.AreEqual(characterId, subscribedCharacterId);
        }

        [Test]
        public void NativeRoomController_TransportMode_TrackSubscribed_DisablesRemoteAudio_WhenPolicyBlocks()
        {
            const string participantId = "participant-1";
            const string participantIdentity = "remote-identity";

            TestRemoteAudioTrack track = new();
            TestRemoteParticipant participant = new(participantId, participantIdentity, track);
            TestRealtimeTransport transport = new()
            {
                Room = new TestRoomFacade { RemoteParticipants = new[] { participant } }
            };

            var factory = CreateInternal<IConvaiRoomControllerFactory>(
                "Convai.Infrastructure.Networking.Native.NativeRoomControllerFactory",
                new Func<IRealtimeTransport>(() => transport));

            IConvaiRoomController controller = factory.Create(
                new TestCharacterRegistry(),
                new TestPlayerSession(),
                new TestConfigurationProvider(),
                new ImmediateDispatcher(),
                new TestLogger(),
                new EventHub(new ImmediateScheduler()));

            controller.SetAudioSubscriptionPolicy(_ => false);

            int subscribedCount = 0;
            controller.OnRemoteAudioTrackSubscribed += (_, _, _) => subscribedCount++;

            InvokeNonPublicInstanceMethod(
                controller,
                "OnTransportTrackSubscribed",
                new TrackInfo(track.Sid, participantId, participantIdentity, TrackKind.Audio, track.Name));

            Assert.AreEqual(0, subscribedCount);
            CollectionAssert.AreEqual(new[] { false }, track.EnabledValues,
                "Transport-selected mode should still enforce the native remote-audio policy by disabling the resolved transport audio track.");
        }

        [Test]
        public void
            NativeRoomController_TransportMode_TrackUnsubscribed_ClearsPendingTransportAudioSubscription_WhenWrapperArrivesLater()
        {
            const string participantId = "participant-1";
            const string participantIdentity = "convai-bot";

            TestRemoteAudioTrack track = new();
            TestRemoteParticipant participant = new(participantId, participantIdentity);
            TestRoomFacade room = new() { RemoteParticipants = new[] { participant } };
            TestRealtimeTransport transport = new() { Room = room };

            var factory = CreateInternal<IConvaiRoomControllerFactory>(
                "Convai.Infrastructure.Networking.Native.NativeRoomControllerFactory",
                new Func<IRealtimeTransport>(() => transport));

            IConvaiRoomController controller = factory.Create(
                new TestCharacterRegistry(),
                new TestPlayerSession(),
                new TestConfigurationProvider(),
                new ImmediateDispatcher(),
                new TestLogger(),
                new EventHub(new ImmediateScheduler()));

            int subscribedCount = 0;
            int unsubscribedCount = 0;
            controller.OnRemoteAudioTrackSubscribed += (_, _, _) => subscribedCount++;
            controller.OnRemoteAudioTrackUnsubscribed += (_, _) => unsubscribedCount++;

            InvokeNonPublicInstanceMethod(
                controller,
                "OnTransportTrackSubscribed",
                new TrackInfo(track.Sid, participantId, participantIdentity, TrackKind.Audio, track.Name));

            InvokeNonPublicInstanceMethod(
                controller,
                "OnTransportTrackUnsubscribed",
                new TrackInfo(track.Sid, participantId, participantIdentity, TrackKind.Audio, track.Name));

            room.RaiseAudioTrackSubscribed(track, participant);

            Assert.AreEqual(0, subscribedCount,
                "A delayed room-facade audio wrapper should not resurrect a transport subscription that was already unsubscribed.");
            Assert.AreEqual(1, unsubscribedCount);
        }

        [Test]
        public void NativeRoomController_TransportMode_TrackUnsubscribed_RaisesRemoteAudioUnsubscribedEvent()
        {
            const string participantId = "participant-1";
            const string participantIdentity = "remote-identity";

            TestRemoteAudioTrack track = new();
            TestRealtimeTransport transport = new()
            {
                Room = new TestRoomFacade
                {
                    RemoteParticipants = new[]
                    {
                        new TestRemoteParticipant(participantId, participantIdentity, track)
                    }
                }
            };

            var factory = CreateInternal<IConvaiRoomControllerFactory>(
                "Convai.Infrastructure.Networking.Native.NativeRoomControllerFactory",
                new Func<IRealtimeTransport>(() => transport));

            IConvaiRoomController controller = factory.Create(
                new TestCharacterRegistry(),
                new TestPlayerSession(),
                new TestConfigurationProvider(),
                new ImmediateDispatcher(),
                new TestLogger(),
                new EventHub(new ImmediateScheduler()));

            string unsubscribedParticipantId = null;
            string unsubscribedCharacterId = null;
            int unsubscribedCount = 0;
            controller.OnRemoteAudioTrackUnsubscribed += (sid, characterId) =>
            {
                unsubscribedParticipantId = sid;
                unsubscribedCharacterId = characterId;
                unsubscribedCount++;
            };

            InvokeNonPublicInstanceMethod(
                controller,
                "OnTransportTrackUnsubscribed",
                new TrackInfo(track.Sid, participantId, participantIdentity, TrackKind.Audio, track.Name));

            Assert.AreEqual(1, unsubscribedCount,
                "Transport-selected mode should surface the same remote-audio unsubscribed event contract instead of silently dropping transport unsubscribe notifications.");
            Assert.AreEqual(participantId, unsubscribedParticipantId);
            Assert.IsNull(unsubscribedCharacterId,
                "Transport track-unsubscribe parity currently preserves the controller contract of raising an unsubscribe event without requiring a character mapping.");
        }

        [Test]
        public void NativeRoomController_SetMicMuted_UsesTransportRoomForMicState()
        {
            TestLocalParticipant localParticipant = new();
            var transportRoom = new TestRoomFacade { LocalParticipant = localParticipant };
            var transport = new TestRealtimeTransport { Room = transportRoom };

            var factory = CreateInternal<IConvaiRoomControllerFactory>(
                "Convai.Infrastructure.Networking.Native.NativeRoomControllerFactory",
                new Func<IRealtimeTransport>(() => transport));

            TestPlayerSession playerSession = new();
            IConvaiRoomController controller = factory.Create(
                new TestCharacterRegistry(),
                playerSession,
                new TestConfigurationProvider(),
                new ImmediateDispatcher(),
                new TestLogger(),
                new EventHub(new ImmediateScheduler()));

            bool? lastMuteEvent = null;
            int muteEventCount = 0;
            controller.OnMicMuteChanged += muted =>
            {
                lastMuteEvent = muted;
                muteEventCount++;
            };

            controller.SetMicMuted(true);

            Assert.IsTrue(controller.IsMicMuted);
            Assert.IsTrue(playerSession.IsMicMuted);
            Assert.AreEqual(true, lastMuteEvent);
            Assert.AreEqual(1, muteEventCount);
            Assert.AreEqual(1, localParticipant.SetAudioMutedCallCount);
            Assert.IsTrue(localParticipant.LastAudioMutedValue);

            controller.SetMicMuted(false);

            Assert.IsFalse(controller.IsMicMuted);
            Assert.IsFalse(playerSession.IsMicMuted);
            Assert.AreEqual(false, lastMuteEvent);
            Assert.AreEqual(2, muteEventCount);
            Assert.AreEqual(2, localParticipant.SetAudioMutedCallCount);
            Assert.IsFalse(localParticipant.LastAudioMutedValue);
        }

        [Test]
        public async Task
            NativeRoomController_TransportMode_CurrentRoomSupportsAudioTrackManagerMicPublishWithStoredMuteState()
        {
            TestLocalParticipant localParticipant = new();
            var transportRoom = new TestRoomFacade { LocalParticipant = localParticipant };
            var transport = new TestRealtimeTransport { Room = transportRoom };

            var factory = CreateInternal<IConvaiRoomControllerFactory>(
                "Convai.Infrastructure.Networking.Native.NativeRoomControllerFactory",
                new Func<IRealtimeTransport>(() => transport));

            IConvaiRoomController controller = factory.Create(
                new TestCharacterRegistry(),
                new TestPlayerSession(),
                new TestConfigurationProvider(),
                new ImmediateDispatcher(),
                new TestLogger(),
                new EventHub(new ImmediateScheduler()));

            Assert.AreSame(transportRoom, controller.CurrentRoom);

            using var manager = new AudioTrackManager(
                () => controller.CurrentRoom,
                new TestCharacterRegistry(),
                new TestLogger(),
                _ => null);

            TestMicrophoneSource microphoneSource = new("transport-mic");
            manager.SetMicMuted(true);

            bool published =
                await manager.PublishMicrophoneAsync(microphoneSource, AudioPublishOptions.DefaultMicrophone);

            Assert.IsTrue(published);
            Assert.AreEqual(1, localParticipant.PublishAudioCallCount);
            Assert.AreSame(microphoneSource, localParticipant.LastPublishedSource);
            Assert.IsTrue(microphoneSource.IsCapturing);
            Assert.IsTrue(microphoneSource.IsMuted,
                "Transport-selected mode should still apply stored native mic mute state when the shared AudioTrackManager publish path runs.");
        }

        [Test]
        public void NativeRoomController_CurrentRoom_RemainsStableSelectedLocalMediaOwnerDuringMicStateChanges()
        {
            TestLocalParticipant localParticipant = new();
            var transportRoom = new TestRoomFacade { LocalParticipant = localParticipant };
            var transport = new TestRealtimeTransport { Room = transportRoom };

            var factory = CreateInternal<IConvaiRoomControllerFactory>(
                "Convai.Infrastructure.Networking.Native.NativeRoomControllerFactory",
                new Func<IRealtimeTransport>(() => transport));

            TestPlayerSession playerSession = new();
            IConvaiRoomController controller = factory.Create(
                new TestCharacterRegistry(),
                playerSession,
                new TestConfigurationProvider(),
                new ImmediateDispatcher(),
                new TestLogger(),
                new EventHub(new ImmediateScheduler()));

            IRoomFacade initialRoom = controller.CurrentRoom;
            IRoomFacade secondRoom = controller.CurrentRoom;

            Assert.IsNotNull(initialRoom);
            Assert.AreSame(initialRoom, secondRoom,
                "The transport-backed room should stay stable across repeated CurrentRoom reads.");
            Assert.AreSame(transportRoom, initialRoom);

            controller.SetMicMuted(true);
            controller.SetMicMuted(false);

            Assert.AreSame(initialRoom, controller.CurrentRoom,
                "Mic mute changes should not swap out the selected transport-backed local-media owner.");
            Assert.IsFalse(controller.IsMicMuted);
            Assert.IsFalse(playerSession.IsMicMuted);
            Assert.AreEqual(2, localParticipant.SetAudioMutedCallCount);
        }

        [Test]
        public void NativeRoomController_UnsolicitedDisconnectCleanup_UsesTransportEntryPointAndSharedControllerReset()
        {
            TestRealtimeTransport transport = new() { Room = new TestRoomFacade() };

            TestCharacterRegistry characterRegistry = new(
                new CharacterDescriptor("instance-1", "char-1", "Character", "participant-1", false));

            var factory = CreateInternal<IConvaiRoomControllerFactory>(
                "Convai.Infrastructure.Networking.Native.NativeRoomControllerFactory",
                new Func<IRealtimeTransport>(() => transport));

            var playerSession = new TestPlayerSession();
            IConvaiRoomController controller = factory.Create(
                characterRegistry,
                playerSession,
                new TestConfigurationProvider(),
                new ImmediateDispatcher(),
                new TestLogger(),
                new EventHub(new ImmediateScheduler()));

            SetNonPublicPropertyValue(controller, nameof(IConvaiRoomController.Token), "token");
            SetNonPublicPropertyValue(controller, nameof(IConvaiRoomController.RoomName), "room-name");
            SetNonPublicPropertyValue(controller, nameof(IConvaiRoomController.SessionID), "session-id");
            SetNonPublicPropertyValue(controller, nameof(IConvaiRoomController.CharacterSessionID),
                "character-session-id");
            SetNonPublicPropertyValue(controller, nameof(IConvaiRoomController.RoomURL), "https://room.example");
            SetNonPublicPropertyValue(controller, nameof(IConvaiRoomController.ResolvedSpeakerId), "speaker-1");
            SetNonPublicPropertyValue(controller, nameof(IConvaiRoomController.HasRoomDetails), true);
            SetNonPublicPropertyValue(controller, nameof(IConvaiRoomController.IsConnectedToRoom), true);
            SetNonPublicPropertyValue(controller, nameof(IConvaiRoomController.IsMicMuted), true);
            playerSession.SetMicMuted(true);

            int disconnectCallbackCount = 0;
            controller.OnUnexpectedRoomDisconnected += () => disconnectCallbackCount++;

            MethodInfo legacyDisconnectMethod = controller.GetType().GetMethod(
                "OnRoomDisconnected",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.IsNull(legacyDisconnectMethod,
                "Legacy direct-mode unsolicited disconnect entrypoint should be deleted once the native runtime becomes transport-only.");

            InvokeNonPublicInstanceMethod(controller, "HandleTransportDisconnected", DisconnectReason.RemoteHangUp);

            Assert.IsNull(controller.Token);
            Assert.IsNull(controller.RoomName);
            Assert.IsNull(controller.SessionID);
            Assert.IsNull(controller.CharacterSessionID);
            Assert.IsNull(controller.RoomURL);
            Assert.IsNull(controller.ResolvedSpeakerId);
            Assert.IsFalse(controller.HasRoomDetails);
            Assert.IsFalse(controller.IsConnectedToRoom);
            Assert.IsFalse(controller.IsMicMuted);
            Assert.IsFalse(playerSession.IsMicMuted);
            Assert.AreEqual(1, disconnectCallbackCount,
                "The transport-backed controller should surface the shared unsolicited-disconnect callback after cleanup/reset completes.");
            Assert.AreEqual(0, characterRegistry.GetAllCharacters().Count,
                "The transport-backed controller should reach the shared controller-owned cleanup reset after an unsolicited disconnect.");
        }

        private static T CreateInternal<T>(string typeName, params object[] args)
        {
            var type = Type.GetType($"{typeName}, Convai.Infrastructure.Networking.Native");
            Assert.IsNotNull(type, $"Expected type '{typeName}' to be available.");

            object instance = Activator.CreateInstance(
                type,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                args,
                null);

            Assert.IsNotNull(instance, $"Expected instance of '{typeName}' to be created.");
            return (T)instance;
        }

        private static Type ResolveNativeType(string typeName)
        {
            return Type.GetType($"{typeName}, Convai.Infrastructure.Networking.Native");
        }

        private static bool HasAttribute(ICustomAttributeProvider provider, string attributeFullName)
        {
            object[] attributes = provider.GetCustomAttributes(false);
            for (int i = 0; i < attributes.Length; i++)
            {
                if (string.Equals(attributes[i].GetType().FullName, attributeFullName, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static ConvaiSettings GetConvaiSettingsInstance()
        {
            FieldInfo instanceField =
                typeof(ConvaiSettings).GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(instanceField);
            return (ConvaiSettings)instanceField.GetValue(null);
        }

        private static void InvokeNonPublicInstanceMethod(object instance, string methodName, params object[] args)
        {
            MethodInfo method =
                instance.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(method, $"Expected non-public method '{methodName}' on '{instance.GetType().FullName}'.");
            method.Invoke(instance, args);
        }

        private static void SetNonPublicPropertyValue(object instance, string propertyName, object value)
        {
            PropertyInfo property = instance.GetType().GetProperty(
                propertyName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            Assert.IsNotNull(property, $"Expected property '{propertyName}' on '{instance.GetType().FullName}'.");
            property.SetValue(instance, value);
        }

        private static void SetConvaiSettingsInstance(ConvaiSettings settings)
        {
            FieldInfo instanceField =
                typeof(ConvaiSettings).GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(instanceField);
            instanceField.SetValue(null, settings);
        }

        private static void SetSerializedNativeRuntimeModeValue(ConvaiSettings settings, int rawValue)
        {
            FieldInfo nativeRuntimeModeField =
                typeof(ConvaiSettings).GetField("_nativeRuntimeMode", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(nativeRuntimeModeField);
            nativeRuntimeModeField.SetValue(settings, Enum.ToObject(typeof(NativeRuntimeMode), rawValue));
        }

        private static void AssertConstructorContainsOnlyTransportFactory(string typeName, string message)
        {
            var type = Type.GetType($"{typeName}, Convai.Infrastructure.Networking.Native");
            Assert.IsNotNull(type, $"Expected type '{typeName}' to be available.");

            ConstructorInfo[] constructors =
                type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.AreEqual(1, constructors.Length, message);

            ParameterInfo[] parameters = constructors[0].GetParameters();
            Assert.AreEqual(1, parameters.Length, message);
            Assert.AreEqual(typeof(Func<IRealtimeTransport>), parameters[0].ParameterType, message);
        }

        private sealed class ImmediateScheduler : IUnityScheduler
        {
            public void ScheduleOnMainThread(Action action) => action?.Invoke();
            public void ScheduleOnBackground(Action action) => action?.Invoke();
            public bool IsMainThread() => true;
        }

        private sealed class ImmediateDispatcher : IMainThreadDispatcher
        {
            public bool TryDispatch(Action action)
            {
                action?.Invoke();
                return true;
            }
        }

        private sealed class TestCharacterRegistry : ICharacterRegistry
        {
            private readonly Dictionary<string, CharacterDescriptor> _characters = new(StringComparer.Ordinal);

            public TestCharacterRegistry(params CharacterDescriptor[] descriptors)
            {
                foreach (CharacterDescriptor descriptor in descriptors)
                    _characters[descriptor.CharacterId] = descriptor;
            }

            public void RegisterCharacter(CharacterDescriptor descriptor) =>
                _characters[descriptor.CharacterId] = descriptor;

            public void UnregisterCharacter(string characterId) => _characters.Remove(characterId);

            public bool TryGetCharacter(string characterId, out CharacterDescriptor descriptor) =>
                _characters.TryGetValue(characterId, out descriptor);

            public bool TryGetCharacterByParticipantId(string participantId, out CharacterDescriptor descriptor)
            {
                foreach (CharacterDescriptor value in _characters.Values)
                {
                    if (string.Equals(value.ParticipantId, participantId, StringComparison.Ordinal))
                    {
                        descriptor = value;
                        return true;
                    }
                }

                descriptor = default;
                return false;
            }

            public IReadOnlyList<CharacterDescriptor> GetAllCharacters() =>
                new List<CharacterDescriptor>(_characters.Values);

            public void SetCharacterMuted(string characterId, bool muted)
            {
                if (_characters.TryGetValue(characterId, out CharacterDescriptor descriptor))
                    _characters[characterId] = descriptor.WithMuteState(muted);
            }

            public void Clear() => _characters.Clear();
        }

        private sealed class TestPlayerSession : IPlayerSession
        {
            public string PlayerId => "player-1";
            public string PlayerName => "Player";
            public bool IsMicMuted { get; private set; }
            public void StartListening(int microphoneIndex = 0) { }
            public void StopListening() { }
            public void SetMicMuted(bool mute) => IsMicMuted = mute;
            public void SetMicrophoneIndex(int index) { }
            public void OnPlayerTranscriptionReceived(string transcript, TranscriptionPhase transcriptionPhase) { }

            public void OnPlayerTranscriptionReceived(string transcript, TranscriptionPhase transcriptionPhase,
                SpeakerInfo speakerInfo)
            {
            }

            public void OnPlayerStartedSpeaking(string sessionId) { }
            public void OnPlayerStoppedSpeaking(string sessionId, bool didProduceFinalTranscript) { }
#pragma warning disable CS0067
            public event Action<string> MicrophoneStreamStarted;
            public event Action<string> MicrophoneStreamStopped;
#pragma warning restore CS0067
        }

        private sealed class TestConfigurationProvider : IConfigurationProvider
        {
            public string ApiKey => "api-key";
            public string CoreServerUrl => "https://live.convai.com";
            public ConvaiConnectionType ConnectionType => ConvaiConnectionType.Audio;
            public string VideoTrackName => "video";
            public ConvaiLLMProvider LlmProvider => ConvaiLLMProvider.Dynamic;
            public ConvaiServerEndpoint ServerEndpoint => ConvaiServerEndpoint.Connect;
            public LipSyncTransportOptions LipSyncTransportOptions => LipSyncTransportOptions.Disabled;
            public string EndUserId => "user";
            public void StoreCharacterSessionId(string characterId, string sessionId) { }
            public string GetCharacterSessionId(string characterId) => null;
            public void ClearCharacterSessionId(string characterId) { }
            public void ClearAllCharacterSessionIds() { }
        }

        private sealed class TestLogger : ILogger
        {
            public List<CapturedLogEntry> Entries { get; } = new();

            public void Log(LogLevel level, string message, LogCategory category = LogCategory.SDK) =>
                Entries.Add(new CapturedLogEntry(level, message, category));

            public void Log(LogLevel level, string message, IReadOnlyDictionary<string, object> context,
                LogCategory category = LogCategory.SDK) =>
                Entries.Add(new CapturedLogEntry(level, message, category));

            public void Debug(string message, LogCategory category = LogCategory.SDK) =>
                Entries.Add(new CapturedLogEntry(LogLevel.Debug, message, category));

            public void Debug(string message, IReadOnlyDictionary<string, object> context,
                LogCategory category = LogCategory.SDK) =>
                Entries.Add(new CapturedLogEntry(LogLevel.Debug, message, category));

            public void Info(string message, LogCategory category = LogCategory.SDK) =>
                Entries.Add(new CapturedLogEntry(LogLevel.Info, message, category));

            public void Info(string message, IReadOnlyDictionary<string, object> context,
                LogCategory category = LogCategory.SDK) =>
                Entries.Add(new CapturedLogEntry(LogLevel.Info, message, category));

            public void Warning(string message, LogCategory category = LogCategory.SDK) =>
                Entries.Add(new CapturedLogEntry(LogLevel.Warning, message, category));

            public void Warning(string message, IReadOnlyDictionary<string, object> context,
                LogCategory category = LogCategory.SDK) =>
                Entries.Add(new CapturedLogEntry(LogLevel.Warning, message, category));

            public void Error(string message, LogCategory category = LogCategory.SDK) =>
                Entries.Add(new CapturedLogEntry(LogLevel.Error, message, category));

            public void Error(string message, IReadOnlyDictionary<string, object> context,
                LogCategory category = LogCategory.SDK) =>
                Entries.Add(new CapturedLogEntry(LogLevel.Error, message, category));

            public void Error(Exception exception, string message = null, LogCategory category = LogCategory.SDK) =>
                Entries.Add(new CapturedLogEntry(LogLevel.Error, message ?? exception?.Message, category));

            public void Error(Exception exception, string message, IReadOnlyDictionary<string, object> context,
                LogCategory category = LogCategory.SDK) =>
                Entries.Add(new CapturedLogEntry(LogLevel.Error, message ?? exception?.Message, category));

            public bool IsEnabled(LogLevel level, LogCategory category) => true;
        }

        private readonly struct CapturedLogEntry
        {
            public CapturedLogEntry(LogLevel level, string message, LogCategory category)
            {
                Level = level;
                Message = message;
                Category = category;
            }

            public LogLevel Level { get; }
            public string Message { get; }
            public LogCategory Category { get; }
        }

        private sealed class TestRoomFacade : IRoomFacade
        {
            private event Action<IRemoteAudioTrack, IRemoteParticipant> AudioTrackSubscribedInternal;

            public string Sid => "room";
            public string Name => "transport-room";
            public RoomState State => RoomState.Connected;
            public bool IsConnected => true;
            public ILocalParticipant LocalParticipant { get; set; }

            public IReadOnlyList<IRemoteParticipant> RemoteParticipants { get; set; } =
                Array.Empty<IRemoteParticipant>();

            public IEnumerable<IParticipant> AllParticipants
            {
                get
                {
                    if (LocalParticipant != null) yield return LocalParticipant;

                    foreach (IRemoteParticipant participant in RemoteParticipants) yield return participant;
                }
            }

            public int RemoteParticipantCount => RemoteParticipants.Count;

            public event Action<IRemoteParticipant> ParticipantJoined
            {
                add { }
                remove { }
            }

            public event Action<IRemoteParticipant> ParticipantLeft
            {
                add { }
                remove { }
            }

            public event Action<IParticipant, ParticipantMetadata> ParticipantMetadataUpdated
            {
                add { }
                remove { }
            }

            public event Action<IRemoteAudioTrack, IRemoteParticipant> AudioTrackSubscribed
            {
                add => AudioTrackSubscribedInternal += value;
                remove => AudioTrackSubscribedInternal -= value;
            }

            public event Action<IRemoteAudioTrack, IRemoteParticipant> AudioTrackUnsubscribed
            {
                add { }
                remove { }
            }

            public event Action<IRemoteVideoTrack, IRemoteParticipant> VideoTrackSubscribed
            {
                add { }
                remove { }
            }

            public event Action<IRemoteVideoTrack, IRemoteParticipant> VideoTrackUnsubscribed
            {
                add { }
                remove { }
            }

            public event Action<TrackSubscriptionEventArgs> TrackSubscribed
            {
                add { }
                remove { }
            }

            public event Action<TrackSubscriptionEventArgs> TrackUnsubscribed
            {
                add { }
                remove { }
            }

            public event Action<RoomState> StateChanged
            {
                add { }
                remove { }
            }

            public event Action Reconnecting
            {
                add { }
                remove { }
            }

            public event Action Reconnected
            {
                add { }
                remove { }
            }

            public event Action<DisconnectReason> Disconnected
            {
                add { }
                remove { }
            }

            public IRemoteParticipant GetParticipantBySid(string sid)
            {
                TryGetParticipantBySid(sid, out IRemoteParticipant participant);
                return participant;
            }

            public IRemoteParticipant GetParticipantByIdentity(string identity)
            {
                TryGetParticipantByIdentity(identity, out IRemoteParticipant participant);
                return participant;
            }

            public bool TryGetParticipantBySid(string sid, out IRemoteParticipant participant)
            {
                foreach (IRemoteParticipant remoteParticipant in RemoteParticipants)
                {
                    if (string.Equals(remoteParticipant.Sid, sid, StringComparison.Ordinal))
                    {
                        participant = remoteParticipant;
                        return true;
                    }
                }

                participant = null;
                return false;
            }

            public bool TryGetParticipantByIdentity(string identity, out IRemoteParticipant participant)
            {
                foreach (IRemoteParticipant remoteParticipant in RemoteParticipants)
                {
                    if (string.Equals(remoteParticipant.Identity, identity, StringComparison.Ordinal))
                    {
                        participant = remoteParticipant;
                        return true;
                    }
                }

                participant = null;
                return false;
            }

            public void RaiseAudioTrackSubscribed(IRemoteAudioTrack audioTrack, IRemoteParticipant participant) =>
                AudioTrackSubscribedInternal?.Invoke(audioTrack, participant);
        }

        private sealed class TestRemoteParticipant : IRemoteParticipant
        {
            private readonly IReadOnlyList<IRemoteAudioTrack> _audioTracks;

            public TestRemoteParticipant(string sid, string identity, params IRemoteAudioTrack[] audioTracks)
            {
                Sid = sid;
                Identity = identity;
                _audioTracks = audioTracks ?? Array.Empty<IRemoteAudioTrack>();
                SubscribedTracks = _audioTracks;
            }

            public string Sid { get; }
            public string Identity { get; }
            public string Name => Identity;
            public ParticipantMetadata Metadata => default;
            public bool IsAgent => false;
            public IReadOnlyList<TrackPublicationInfo> TrackPublications => Array.Empty<TrackPublicationInfo>();
            public IReadOnlyList<IRemoteTrack> SubscribedTracks { get; }

            public IEnumerable<IRemoteAudioTrack> AudioTracks => _audioTracks;
            public IEnumerable<IRemoteVideoTrack> VideoTracks => Array.Empty<IRemoteVideoTrack>();

            public event Action<ParticipantMetadata> MetadataUpdated
            {
                add { }
                remove { }
            }

            public event Action<IRemoteTrack, TrackPublicationInfo> TrackSubscribed
            {
                add { }
                remove { }
            }

            public event Action<IRemoteTrack, TrackPublicationInfo> TrackUnsubscribed
            {
                add { }
                remove { }
            }

            public event Action<TrackPublicationInfo, bool> TrackMuteChanged
            {
                add { }
                remove { }
            }
        }

        private sealed class TestRemoteAudioTrack : IRemoteAudioTrack, IRemoteAudioControlTrack
        {
            public List<bool> EnabledValues { get; } = new();
            public void SetRemoteAudioEnabled(bool enabled) => EnabledValues.Add(enabled);
            public string Sid => "audio-track-1";
            public string Name => "audio-track-1";
            public TrackKind Kind => TrackKind.Audio;
            public bool IsMuted => false;
            public IRemoteParticipant Participant => null;
            public bool IsSubscribed => true;
            public bool IsAttached => false;

            public event Action<bool> MuteChanged
            {
                add { }
                remove { }
            }

            public IAudioStream CreateAudioStream() => null;
            public void AttachToAudioSource(AudioSource audioSource) { }
            public void Detach() { }
        }

        private sealed class TestLocalParticipant : ILocalParticipant
        {
            private readonly List<ILocalTrack> _localTracks = new();
            public int PublishAudioCallCount { get; private set; }
            public int SetAudioMutedCallCount { get; private set; }
            public bool LastAudioMutedValue { get; private set; }
            public IAudioSource LastPublishedSource { get; private set; }

            public string Sid => "local-participant";
            public string Identity => "local";
            public string Name => "Local";
            public ParticipantMetadata Metadata => default;
            public bool IsAgent => false;
            public IReadOnlyList<ILocalTrack> LocalTracks => _localTracks;

            public event Action<ParticipantMetadata> MetadataUpdated
            {
                add { }
                remove { }
            }

            public event Action<ILocalTrack> TrackPublished;
            public event Action<ILocalTrack> TrackUnpublished;

            public Task<ILocalAudioTrack> PublishAudioTrackAsync(IAudioSource source,
                AudioPublishOptions options = default, CancellationToken ct = default)
            {
                PublishAudioCallCount++;
                LastPublishedSource = source;
                source?.StartCapture();

                TestLocalAudioTrack track = new(source, $"audio-track-{PublishAudioCallCount}");
                _localTracks.Add(track);
                TrackPublished?.Invoke(track);
                return Task.FromResult<ILocalAudioTrack>(track);
            }

            public Task<ILocalVideoTrack> PublishVideoTrackAsync(IVideoSource source,
                VideoPublishOptions options = default, CancellationToken ct = default) =>
                Task.FromResult<ILocalVideoTrack>(null);

            public Task UnpublishTrackAsync(ILocalTrack track, CancellationToken ct = default)
            {
                if (track is TestLocalAudioTrack audioTrack) audioTrack.MarkUnpublished();

                _localTracks.Remove(track);
                TrackUnpublished?.Invoke(track);
                return Task.CompletedTask;
            }

            public void SetAudioMuted(bool muted)
            {
                SetAudioMutedCallCount++;
                LastAudioMutedValue = muted;

                foreach (ILocalTrack track in _localTracks)
                    if (track is ILocalAudioTrack audioTrack)
                        audioTrack.SetMuted(muted);
            }

            public void SetVideoMuted(bool muted)
            {
            }
        }

        private sealed class TestLocalAudioTrack : ILocalAudioTrack
        {
            public TestLocalAudioTrack(IAudioSource source, string sid)
            {
                Source = source;
                Sid = sid;
                Name = sid;
                IsPublished = true;
            }

            public string Sid { get; }
            public string Name { get; }
            public TrackKind Kind => TrackKind.Audio;
            public bool IsMuted { get; private set; }
            public bool IsPublished { get; private set; }
            public IAudioSource Source { get; }

            public event Action<bool> MuteChanged;

            public void SetMuted(bool muted)
            {
                IsMuted = muted;
                MuteChanged?.Invoke(muted);
            }

            public void MarkUnpublished() => IsPublished = false;
        }

        private sealed class TestMicrophoneSource : IMicrophoneSource
        {
            public TestMicrophoneSource(string name)
            {
                Name = name;
                DeviceName = name;
            }

            public string Name { get; }
            public bool IsCapturing { get; private set; }
            public string DeviceName { get; }
            public int DeviceIndex => 0;
            public bool IsMuted { get; set; }

            public void StartCapture() => IsCapturing = true;

            public void StopCapture() => IsCapturing = false;

            public void Dispose()
            {
            }
        }

        private sealed class TestRealtimeTransport : IRealtimeTransport
        {
            public TransportState State => TransportState.Disconnected;
            public TransportSessionInfo? CurrentSession => null;
            public TransportCapabilities Capabilities => default;
            public AudioRuntimeState AudioState => default;
            public bool IsConnected => false;
            public IRoomFacade Room { get; set; }
            public bool IsMicrophoneEnabled => false;
            public bool IsMicrophoneMuted => false;

            public Task<bool> ConnectAsync(string url, string token, TransportConnectOptions options = null,
                CancellationToken ct = default) => Task.FromResult(false);

            public Task DisconnectAsync(DisconnectReason reason = DisconnectReason.ClientInitiated,
                CancellationToken ct = default) => Task.CompletedTask;

            public void EnableAudio() { }

            public Task<bool> EnableMicrophoneAsync(int microphoneDeviceIndex = 0, CancellationToken ct = default) =>
                Task.FromResult(false);

            public Task DisableMicrophoneAsync(CancellationToken ct = default) => Task.CompletedTask;
            public void SetMicrophoneMuted(bool muted) { }
            public bool CanEnableMicrophone() => false;
            public bool CanEnableAudio() => false;

            public Task SendDataAsync(ReadOnlyMemory<byte> payload, bool reliable = true, string topic = null,
                string[] destinationIdentities = null, CancellationToken ct = default) => Task.CompletedTask;

            public void Dispose() { }
#pragma warning disable CS0067
            public event Action<TransportSessionInfo> Connected;
            public event Action<DisconnectReason> Disconnected;
            public event Action<TransportError> ConnectionFailed;
            public event Action Reconnecting;
            public event Action Reconnected;
            public event Action<TransportState> StateChanged;
            public event Action<DataPacket> DataReceived;
            public event Action<TransportParticipantInfo> ParticipantConnected;
            public event Action<TransportParticipantInfo> ParticipantDisconnected;
            public event Action<TrackInfo> TrackSubscribed;
            public event Action<TrackInfo> TrackUnsubscribed;
            public event Action<bool> MicrophoneEnabledChanged;
            public event Action<bool> MicrophoneMuteChanged;
            public event Action<bool> AudioPlaybackStateChanged;
#pragma warning restore CS0067
        }
    }
}
