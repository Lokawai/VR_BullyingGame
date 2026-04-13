using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Convai.Domain.DomainEvents.LipSync;
using Convai.Domain.DomainEvents.Runtime;
using Convai.Domain.EventSystem;
using Convai.Domain.Logging;
using Convai.Domain.Models;
using Convai.Domain.Models.LipSync;
using Convai.Infrastructure.Networking;
using Convai.Infrastructure.Networking.Models;
using Convai.Infrastructure.Networking.Transport;
using Convai.Infrastructure.Protocol;
using Convai.Shared.Types;
using NUnit.Framework;

namespace Convai.Tests.EditMode.Infrastructure
{
    [TestFixture]
    public class RTVIHandlerLipSyncServerMessageTests
    {
        [Test]
        public void TryHandleLipSyncServerMessage_ValidChunkedPacket_PublishesPackedEvent()
        {
            TestLogger logger = new();
            EventHub eventHub = new(new ImmediateScheduler(), logger);
            TestCharacterRegistry registry = new();
            registry.RegisterCharacter(new CharacterDescriptor("inst-1", "char-1", "Test Character", "participant-1",
                false));

            LipSyncPackedDataReceived packedEvent = default;
            bool packedEventReceived = false;
            eventHub.Subscribe<LipSyncPackedDataReceived>(
                evt =>
                {
                    packedEvent = evt;
                    packedEventReceived = true;
                },
                EventDeliveryPolicy.Immediate);

            RTVIHandler handler = CreateHandler(logger, eventHub, registry, CreateOptions("A", "B"));
            ProtocolPacket packet = CreateServerMessagePacket(
                "participant-1",
                @"{
                    ""type"": ""chunked-neurosync-blendshapes"",
                    ""format"": ""arkit"",
                    ""sequence"": 99,
                    ""timestamp"": 45.5,
                    ""blendshapes"": [
                        [0.1, 0.2],
                        [0.3, 0.4]
                    ]
                }");

            bool handled = handler.TryHandleLipSyncServerMessage(packet);

            Assert.IsTrue(handled);
            Assert.IsTrue(packedEventReceived);

            Assert.AreEqual("char-1", packedEvent.CharacterId);
            Assert.AreEqual("participant-1", packedEvent.ParticipantId);
            Assert.AreEqual(LipSyncProfileId.ARKit, packedEvent.ProfileId);
            Assert.AreEqual(2, packedEvent.FrameCount);
        }

        [Test]
        public void TryHandleLipSyncServerMessage_UnresolvedParticipant_DropsWithoutPublish()
        {
            TestLogger logger = new();
            EventHub eventHub = new(new ImmediateScheduler(), logger);

            int packedEventCount = 0;
            eventHub.Subscribe<LipSyncPackedDataReceived>(_ => packedEventCount++, EventDeliveryPolicy.Immediate);

            RTVIHandler handler = CreateHandler(logger, eventHub, new TestCharacterRegistry(), CreateOptions("A", "B"));
            ProtocolPacket packet = CreateServerMessagePacket(
                "participant-unmapped",
                @"{
                    ""type"": ""chunked-neurosync-blendshapes"",
                    ""format"": ""arkit"",
                    ""sequence"": 11,
                    ""timestamp"": 10.25,
                    ""blendshapes"": [
                        [0.1, 0.2],
                        [0.3, 0.4]
                    ]
                }");

            bool handled = handler.TryHandleLipSyncServerMessage(packet);

            Assert.IsTrue(handled);
            Assert.AreEqual(0, packedEventCount);
            Assert.IsTrue(
                logger.WarningMessages.Exists(message =>
                    message.Contains("lipsync.route.character_unresolved", StringComparison.Ordinal)),
                "Expected unresolved participant drop reason in warning log.");
        }

        [Test]
        public void TryHandleLipSyncServerMessage_FormatMismatch_IsHandledAndDroppedWithoutPublish()
        {
            TestLogger logger = new();
            EventHub eventHub = new(new ImmediateScheduler(), logger);
            TestCharacterRegistry registry = new();
            registry.RegisterCharacter(new CharacterDescriptor("inst-1", "char-1", "Test Character", "participant-1",
                false));

            int packedEventCount = 0;
            eventHub.Subscribe<LipSyncPackedDataReceived>(_ => packedEventCount++, EventDeliveryPolicy.Immediate);

            RTVIHandler handler = CreateHandler(logger, eventHub, registry, CreateOptions("A"));
            ProtocolPacket packet = CreateServerMessagePacket(
                "participant-1",
                @"{
                    ""type"": ""chunked-neurosync-blendshapes"",
                    ""format"": ""mha"",
                    ""blendshapes"": [[0.1]]
                }");

            bool handled = handler.TryHandleLipSyncServerMessage(packet);

            Assert.IsTrue(handled, "Lip-sync candidate with parse/drop should be marked handled (no fallback).");
            Assert.AreEqual(0, packedEventCount);
            Assert.IsTrue(
                logger.WarningMessages.Exists(message =>
                    message.Contains("lipsync.parse.format_mismatch", StringComparison.Ordinal)),
                "Expected format mismatch drop reason in warning log.");
        }

        [Test]
        public void TryHandleLipSyncServerMessage_NonLipSyncPacket_ReturnsFalse()
        {
            TestLogger logger = new();
            EventHub eventHub = new(new ImmediateScheduler(), logger);
            RTVIHandler handler = CreateHandler(logger, eventHub, new TestCharacterRegistry(), CreateOptions("A"));

            ProtocolPacket packet = CreateServerMessagePacket(
                "participant-1",
                @"{
                    ""type"": ""bot-emotion"",
                    ""emotion"": ""happy"",
                    ""scale"": 85
                }");

            bool handled = handler.TryHandleLipSyncServerMessage(packet);

            Assert.IsFalse(handled);
        }

        [Test]
        public void TryHandleLipSyncServerMessage_InvalidJsonCandidate_IsHandledAndDroppedWithoutPublish()
        {
            TestLogger logger = new();
            EventHub eventHub = new(new ImmediateScheduler(), logger);
            RTVIHandler handler = CreateHandler(logger, eventHub, new TestCharacterRegistry(), CreateOptions("A"));

            int packedEventCount = 0;
            eventHub.Subscribe<LipSyncPackedDataReceived>(_ => packedEventCount++, EventDeliveryPolicy.Immediate);

            string malformedJson =
                "{\"type\":\"server-message\",\"payload\":{\"type\":\"chunked-neurosync-blendshapes\",\"format\":\"arkit\",\"blendshapes\":[[0.1]}";
            ProtocolPacket packet = new(Encoding.UTF8.GetBytes(malformedJson), "participant-1", string.Empty, true);

            bool handled = handler.TryHandleLipSyncServerMessage(packet);

            Assert.IsTrue(handled, "Malformed lip-sync candidate should be handled and dropped.");
            Assert.AreEqual(0, packedEventCount);
            Assert.IsTrue(
                logger.WarningMessages.Exists(message =>
                    message.Contains("lipsync.parse.payload_invalid_json", StringComparison.Ordinal)),
                "Expected invalid-json drop reason in warning log.");
        }

        [Test]
        public void ProcessIncoming_BotStartedSpeaking_WithMappedParticipant_PublishesSpeechEvent()
        {
            TestLogger logger = new();
            EventHub eventHub = new(new ImmediateScheduler(), logger);
            TestCharacterRegistry registry = new();
            registry.RegisterCharacter(new CharacterDescriptor("inst-1", "char-1", "Test Character", "participant-1",
                false));

            CharacterSpeechStateChanged speechEvent = default;
            int speechEventCount = 0;
            eventHub.Subscribe<CharacterSpeechStateChanged>(
                evt =>
                {
                    speechEvent = evt;
                    speechEventCount++;
                },
                EventDeliveryPolicy.Immediate);

            ProtocolGateway gateway = new();
            _ = new RTVIHandler(
                gateway,
                new TestRealtimeTransport(),
                registry,
                new TestPlayerSession(),
                new ImmediateDispatcher(),
                logger,
                eventHub,
                null,
                CreateOptions("A"));

            gateway.ProcessIncoming(CreateGatewayPacket("bot-started-speaking", "participant-1"));

            Assert.AreEqual(1, speechEventCount);
            Assert.AreEqual("char-1", speechEvent.CharacterId);
            Assert.IsTrue(speechEvent.IsSpeaking);
        }

        [Test]
        public void ProcessIncoming_BotStartedSpeaking_WithCharacterIdentityFallback_PublishesSpeechEvent()
        {
            TestLogger logger = new();
            EventHub eventHub = new(new ImmediateScheduler(), logger);
            TestCharacterRegistry registry = new();
            registry.RegisterCharacter(new CharacterDescriptor("inst-1", "char-1", "Test Character", string.Empty,
                false));

            CharacterSpeechStateChanged speechEvent = default;
            int speechEventCount = 0;
            eventHub.Subscribe<CharacterSpeechStateChanged>(
                evt =>
                {
                    speechEvent = evt;
                    speechEventCount++;
                },
                EventDeliveryPolicy.Immediate);

            ProtocolGateway gateway = new();
            _ = new RTVIHandler(
                gateway,
                new TestRealtimeTransport(),
                registry,
                new TestPlayerSession(),
                new ImmediateDispatcher(),
                logger,
                eventHub,
                null,
                CreateOptions("A"));

            gateway.ProcessIncoming(CreateGatewayPacket("bot-started-speaking", "char-1"));

            Assert.AreEqual(1, speechEventCount);
            Assert.AreEqual("char-1", speechEvent.CharacterId);
            Assert.IsTrue(speechEvent.IsSpeaking);
        }

        [Test]
        public void
            ProcessIncoming_BotStartedSpeaking_WithUnmappedParticipant_PublishesSpeechEventUsingParticipantFallback()
        {
            TestLogger logger = new();
            EventHub eventHub = new(new ImmediateScheduler(), logger);

            CharacterSpeechStateChanged speechEvent = default;
            int speechEventCount = 0;
            eventHub.Subscribe<CharacterSpeechStateChanged>(
                evt =>
                {
                    speechEvent = evt;
                    speechEventCount++;
                },
                EventDeliveryPolicy.Immediate);

            ProtocolGateway gateway = new();
            _ = new RTVIHandler(
                gateway,
                new TestRealtimeTransport(),
                new TestCharacterRegistry(),
                new TestPlayerSession(),
                new ImmediateDispatcher(),
                logger,
                eventHub,
                null,
                CreateOptions("A"));

            gateway.ProcessIncoming(CreateGatewayPacket("bot-started-speaking", "participant-unmapped"));

            Assert.AreEqual(1, speechEventCount);
            Assert.AreEqual("participant-unmapped", speechEvent.CharacterId);
            Assert.IsTrue(speechEvent.IsSpeaking);
        }

        [Test]
        public void TryHandleLipSyncServerMessage_WithCharacterIdentityFallback_PublishesPackedEvent()
        {
            TestLogger logger = new();
            EventHub eventHub = new(new ImmediateScheduler(), logger);
            TestCharacterRegistry registry = new();
            registry.RegisterCharacter(new CharacterDescriptor("inst-1", "char-1", "Test Character", string.Empty,
                false));

            LipSyncPackedDataReceived packedEvent = default;
            bool packedEventReceived = false;
            eventHub.Subscribe<LipSyncPackedDataReceived>(
                evt =>
                {
                    packedEvent = evt;
                    packedEventReceived = true;
                },
                EventDeliveryPolicy.Immediate);

            RTVIHandler handler = CreateHandler(logger, eventHub, registry, CreateOptions("A", "B"));
            ProtocolPacket packet = CreateServerMessagePacket(
                "char-1",
                @"{
                    ""type"": ""chunked-neurosync-blendshapes"",
                    ""format"": ""arkit"",
                    ""sequence"": 42,
                    ""timestamp"": 12.5,
                    ""blendshapes"": [
                        [0.1, 0.2],
                        [0.3, 0.4]
                    ]
                }");

            bool handled = handler.TryHandleLipSyncServerMessage(packet);

            Assert.IsTrue(handled);
            Assert.IsTrue(packedEventReceived);
            Assert.AreEqual("char-1", packedEvent.CharacterId);
            Assert.AreEqual("char-1", packedEvent.ParticipantId);
        }

        private static RTVIHandler CreateHandler(
            TestLogger logger,
            IEventHub eventHub,
            ICharacterRegistry characterRegistry,
            in LipSyncTransportOptions options)
        {
            return new RTVIHandler(
                new ProtocolGateway(),
                new TestRealtimeTransport(),
                characterRegistry,
                new TestPlayerSession(),
                new ImmediateDispatcher(),
                logger,
                eventHub,
                null,
                options);
        }

        private static ProtocolPacket CreateServerMessagePacket(string participantId, string payloadObjectJson)
        {
            string json = "{\"type\":\"server-message\",\"payload\":" + (payloadObjectJson ?? "{}") + "}";
            return new ProtocolPacket(Encoding.UTF8.GetBytes(json), participantId, string.Empty, true);
        }

        private static ProtocolPacket CreateGatewayPacket(string messageType, string participantId,
            string payloadObjectJson = "{}")
        {
            string json = "{\"type\":\"" + (messageType ?? string.Empty) + "\",\"payload\":" +
                          (payloadObjectJson ?? "{}") + "}";
            return new ProtocolPacket(Encoding.UTF8.GetBytes(json), participantId ?? string.Empty, string.Empty, true);
        }

        private static LipSyncTransportOptions CreateOptions(params string[] channelNames)
        {
            return new LipSyncTransportOptions(
                true,
                "neurosync",
                LipSyncProfileId.ARKit,
                "arkit",
                channelNames,
                true,
                10,
                60,
                LipSyncTransportOptions.DefaultFramesBufferDuration);
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

        private sealed class TestRealtimeTransport : IRealtimeTransport
        {
            public TransportState State => TransportState.Connected;
            public TransportSessionInfo? CurrentSession => null;
            public TransportCapabilities Capabilities => default;
            public AudioRuntimeState AudioState => default;
            public bool IsConnected => true;
            public IRoomFacade Room => null;
            public bool IsMicrophoneEnabled => false;
            public bool IsMicrophoneMuted => false;

            public Task<bool> ConnectAsync(string url, string token, TransportConnectOptions options = null,
                CancellationToken ct = default) => Task.FromResult(true);

            public Task DisconnectAsync(DisconnectReason reason = DisconnectReason.ClientInitiated,
                CancellationToken ct = default) => Task.CompletedTask;

            public void EnableAudio()
            {
            }

            public Task<bool> EnableMicrophoneAsync(int microphoneDeviceIndex = 0, CancellationToken ct = default) =>
                Task.FromResult(false);

            public Task DisableMicrophoneAsync(CancellationToken ct = default) => Task.CompletedTask;

            public void SetMicrophoneMuted(bool muted)
            {
            }

            public bool CanEnableMicrophone() => false;
            public bool CanEnableAudio() => false;

            public Task SendDataAsync(ReadOnlyMemory<byte> payload, bool reliable = true, string topic = null,
                string[] destinationIdentities = null, CancellationToken ct = default) => Task.CompletedTask;

            public void Dispose()
            {
            }

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

        private sealed class TestCharacterRegistry : ICharacterRegistry
        {
            private readonly Dictionary<string, CharacterDescriptor> _characters = new(StringComparer.Ordinal);
            private readonly Dictionary<string, CharacterDescriptor> _participants = new(StringComparer.Ordinal);

            public void RegisterCharacter(CharacterDescriptor descriptor)
            {
                _characters[descriptor.CharacterId] = descriptor;
                if (!string.IsNullOrWhiteSpace(descriptor.ParticipantId))
                    _participants[descriptor.ParticipantId] = descriptor;
            }

            public void UnregisterCharacter(string characterId)
            {
                if (!_characters.TryGetValue(characterId ?? string.Empty, out CharacterDescriptor existing)) return;

                _characters.Remove(characterId ?? string.Empty);
                if (!string.IsNullOrWhiteSpace(existing.ParticipantId)) _participants.Remove(existing.ParticipantId);
            }

            public bool TryGetCharacter(string characterId, out CharacterDescriptor descriptor) =>
                _characters.TryGetValue(characterId ?? string.Empty, out descriptor);

            public bool TryGetCharacterByParticipantId(string participantId, out CharacterDescriptor descriptor) =>
                _participants.TryGetValue(participantId ?? string.Empty, out descriptor);

            public IReadOnlyList<CharacterDescriptor> GetAllCharacters() =>
                new List<CharacterDescriptor>(_characters.Values);

            public void SetCharacterMuted(string characterId, bool muted)
            {
                if (!_characters.TryGetValue(characterId ?? string.Empty, out CharacterDescriptor descriptor)) return;

                CharacterDescriptor updated = descriptor.WithMuteState(muted);
                _characters[updated.CharacterId] = updated;
                if (!string.IsNullOrWhiteSpace(updated.ParticipantId)) _participants[updated.ParticipantId] = updated;
            }

            public void Clear()
            {
                _characters.Clear();
                _participants.Clear();
            }
        }

        private sealed class TestPlayerSession : IPlayerSession
        {
            public string PlayerId => "player-1";

            public string PlayerName => "Player";

            public bool IsMicMuted { get; private set; }

            public void StartListening(int microphoneIndex = 0) => MicrophoneStreamStarted?.Invoke("session");

            public void StopListening() => MicrophoneStreamStopped?.Invoke("session");

            public void SetMicMuted(bool mute) => IsMicMuted = mute;

            public void SetMicrophoneIndex(int index)
            {
            }

            public void OnPlayerTranscriptionReceived(string transcript, TranscriptionPhase transcriptionPhase)
            {
            }

            public void OnPlayerTranscriptionReceived(string transcript, TranscriptionPhase transcriptionPhase,
                SpeakerInfo speakerInfo)
            {
            }

            public void OnPlayerStartedSpeaking(string sessionId)
            {
            }

            public void OnPlayerStoppedSpeaking(string sessionId, bool didProduceFinalTranscript)
            {
            }
#pragma warning disable CS0067
            public event Action<string> MicrophoneStreamStarted;
            public event Action<string> MicrophoneStreamStopped;
#pragma warning restore CS0067
        }

        private sealed class TestLogger : ILogger
        {
            public List<string> WarningMessages { get; } = new();
            public bool DebugEnabled { get; } = true;

            public void Log(LogLevel level, string message, LogCategory category = LogCategory.SDK)
            {
            }

            public void Log(LogLevel level, string message, IReadOnlyDictionary<string, object> context,
                LogCategory category = LogCategory.SDK)
            {
            }

            public void Debug(string message, LogCategory category = LogCategory.SDK)
            {
            }

            public void Debug(string message, IReadOnlyDictionary<string, object> context,
                LogCategory category = LogCategory.SDK)
            {
            }

            public void Info(string message, LogCategory category = LogCategory.SDK)
            {
            }

            public void Info(string message, IReadOnlyDictionary<string, object> context,
                LogCategory category = LogCategory.SDK)
            {
            }

            public void Warning(string message, LogCategory category = LogCategory.SDK) =>
                WarningMessages.Add(message ?? string.Empty);

            public void Warning(string message, IReadOnlyDictionary<string, object> context,
                LogCategory category = LogCategory.SDK) => Warning(message, category);

            public void Error(string message, LogCategory category = LogCategory.SDK)
            {
            }

            public void Error(string message, IReadOnlyDictionary<string, object> context,
                LogCategory category = LogCategory.SDK)
            {
            }

            public void Error(Exception exception, string message = null, LogCategory category = LogCategory.SDK)
            {
            }

            public void Error(Exception exception, string message, IReadOnlyDictionary<string, object> context,
                LogCategory category = LogCategory.SDK)
            {
            }

            public bool IsEnabled(LogLevel level, LogCategory category) => level != LogLevel.Debug || DebugEnabled;
        }
    }
}
