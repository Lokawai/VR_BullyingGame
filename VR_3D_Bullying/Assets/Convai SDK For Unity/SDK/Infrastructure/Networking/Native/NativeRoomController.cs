using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Convai.Domain.Abstractions;
using Convai.Domain.DomainEvents.Participant;
using Convai.Domain.EventSystem;
using Convai.Domain.Logging;
using Convai.Infrastructure.Networking.Models;
using Convai.Infrastructure.Networking.Transport;
using Convai.Infrastructure.Protocol;
using Convai.RestAPI;
using Convai.RestAPI.Internal;
using Convai.RestAPI.Services;
using Newtonsoft.Json;
// Type aliases to disambiguate between LiveKit.Proto and Transport types
using TransportParticipantInfo = Convai.Infrastructure.Networking.Transport.TransportParticipantInfo;
using TransportTrackInfo = Convai.Infrastructure.Networking.Transport.TrackInfo;
using TransportTrackKind = Convai.Infrastructure.Networking.Transport.TrackKind;

namespace Convai.Infrastructure.Networking.Native
{
    /// <summary>
    ///     Native (non-WebGL) room controller that implements <see cref="IConvaiRoomController" /> directly.
    ///     Manages native room connections through the transport abstraction layer.
    /// </summary>
    internal sealed class NativeRoomController : IConvaiRoomController
    {
        private readonly ICharacterRegistry _characterRegistry;
        private readonly IConfigurationProvider _config;
        private readonly IMainThreadDispatcher _dispatcher;
        private readonly IEventHub _eventHub;
        private readonly ILogger _logger;

        private readonly Dictionary<string, TransportTrackInfo> _pendingTransportAudioSubscriptions =
            new(StringComparer.Ordinal);

        private readonly IPlayerSession _playerSession;
        private readonly ProtocolGateway _protocolGateway;
        private readonly INarrativeSectionNameResolver _sectionNameResolver;

        /// <summary>
        ///     Lock object for thread-safe access to public state properties.
        ///     Used to synchronize access from Unity main thread, EventHub background threads, and async tasks.
        /// </summary>
        private readonly object _stateLock = new();

        private readonly IRealtimeTransport _transport;

        private Func<string, bool> _audioSubscriptionPolicy;
        private string _characterSessionId;
        private bool _disposed;

        private bool _hasRoomDetails;
        private bool _isConnectedToRoom;
        private bool _isMicMuted;
        private IRemoteAudioControl _remoteAudioControl;
        private string _resolvedSpeakerId;
        private string _roomName;
        private string _roomUrl;
        private string _sessionId;
        private IRoomFacade _subscribedRoomFacade;
        private string _token;

        /// <summary>
        ///     Initializes a new instance of the <see cref="NativeRoomController" /> class.
        /// </summary>
        /// <param name="characterRegistry">Character registry for resolving participants to characters.</param>
        /// <param name="playerSession">Player session abstraction.</param>
        /// <param name="config">Configuration provider for settings and stored session IDs.</param>
        /// <param name="dispatcher">Dispatcher used to marshal work to the main thread.</param>
        /// <param name="logger">Logger.</param>
        /// <param name="eventHub">Optional event hub used for domain events.</param>
        /// <param name="sectionNameResolver">Optional resolver for human-readable narrative section names.</param>
        /// <param name="transport">Realtime transport abstraction backing the native room controller.</param>
        public NativeRoomController(
            ICharacterRegistry characterRegistry,
            IPlayerSession playerSession,
            IConfigurationProvider config,
            IMainThreadDispatcher dispatcher,
            ILogger logger,
            IEventHub eventHub = null,
            INarrativeSectionNameResolver sectionNameResolver = null,
            IRealtimeTransport transport = null)
        {
            _characterRegistry = characterRegistry ?? throw new ArgumentNullException(nameof(characterRegistry));
            _playerSession = playerSession ?? throw new ArgumentNullException(nameof(playerSession));
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _eventHub = eventHub;
            _sectionNameResolver = sectionNameResolver;
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));

            _transport.StateChanged += HandleConnectionStateChanged;
            _transport.ConnectionFailed += HandleConnectionFailed;
            _transport.DataReceived += HandleDataPacketReceived;
            _transport.ParticipantConnected += OnTransportParticipantConnected;
            _transport.ParticipantDisconnected += OnTransportParticipantDisconnected;
            _transport.TrackSubscribed += OnTransportTrackSubscribed;
            _transport.TrackUnsubscribed += OnTransportTrackUnsubscribed;
            _transport.Disconnected += HandleTransportDisconnected;
            _transport.Reconnecting += HandleTransportReconnecting;
            _transport.Reconnected += HandleTransportReconnected;

            _protocolGateway = new ProtocolGateway(
                logDebug: message => _logger.Debug(message, LogCategory.Transport),
                logError: message => _logger.Error(message, LogCategory.Transport));

            RefreshRoomFacadeAudioBridge();
        }

        /// <inheritdoc />
        public IRoomFacade CurrentRoom => _transport.Room;

        /// <inheritdoc />
        public RTVIHandler RTVIHandler { get; private set; }

        /// <summary>
        ///     Indicates whether room connection details have been successfully retrieved.
        ///     Thread-safe property.
        /// </summary>
        public bool HasRoomDetails
        {
            get
            {
                lock (_stateLock) return _hasRoomDetails;
            }
            private set
            {
                lock (_stateLock) _hasRoomDetails = value;
            }
        }

        /// <summary>
        ///     Indicates whether currently connected to the LiveKit room.
        ///     Thread-safe property.
        /// </summary>
        public bool IsConnectedToRoom
        {
            get
            {
                lock (_stateLock) return _isConnectedToRoom;
            }
            private set
            {
                lock (_stateLock) _isConnectedToRoom = value;
            }
        }

        /// <summary>
        ///     Indicates whether the microphone is currently muted.
        ///     Thread-safe property.
        /// </summary>
        public bool IsMicMuted
        {
            get
            {
                lock (_stateLock) return _isMicMuted;
            }
            private set
            {
                lock (_stateLock) _isMicMuted = value;
            }
        }

        /// <summary>
        ///     The authentication token for the current room connection.
        ///     Thread-safe property.
        /// </summary>
        public string Token
        {
            get
            {
                lock (_stateLock) return _token;
            }
            private set
            {
                lock (_stateLock) _token = value;
            }
        }

        /// <summary>
        ///     The name of the current room.
        ///     Thread-safe property.
        /// </summary>
        public string RoomName
        {
            get
            {
                lock (_stateLock) return _roomName;
            }
            private set
            {
                lock (_stateLock) _roomName = value;
            }
        }

        /// <summary>
        ///     The session ID for the current connection.
        ///     Thread-safe property.
        /// </summary>
        public string SessionID
        {
            get
            {
                lock (_stateLock) return _sessionId;
            }
            private set
            {
                lock (_stateLock) _sessionId = value;
            }
        }

        /// <summary>
        ///     The URL of the current room.
        ///     Thread-safe property.
        /// </summary>
        public string RoomURL
        {
            get
            {
                lock (_stateLock) return _roomUrl;
            }
            private set
            {
                lock (_stateLock) _roomUrl = value;
            }
        }

        /// <summary>
        ///     The character-specific session ID for conversation continuity.
        ///     Thread-safe property.
        /// </summary>
        public string CharacterSessionID
        {
            get
            {
                lock (_stateLock) return _characterSessionId;
            }
            private set
            {
                lock (_stateLock) _characterSessionId = value;
            }
        }

        /// <summary>
        ///     The server-generated speaker ID (UUID) returned after speaker resolution.
        ///     This ID is used for Long-Term Memory (LTM) and interaction tracking.
        ///     Thread-safe property.
        /// </summary>
        /// <remarks>
        ///     This is NOT the same as the end_user_id sent during connection.
        ///     The server creates/resolves a Speaker record from the end_user_id and returns this speaker_id.
        ///     LTM uses the key format: speaker_id:character_id
        /// </remarks>
        public string ResolvedSpeakerId
        {
            get
            {
                lock (_stateLock) return _resolvedSpeakerId;
            }
            private set
            {
                lock (_stateLock) _resolvedSpeakerId = value;
            }
        }

        /// <inheritdoc />
        public event Action OnRoomConnectionSuccessful;

        /// <inheritdoc />
        public event Action OnRoomConnectionFailed;

        /// <inheritdoc />
        public event Action<bool> OnMicMuteChanged;

        /// <inheritdoc />
        public event Action OnRoomReconnecting;

        /// <inheritdoc />
        public event Action OnRoomReconnected;

        /// <inheritdoc />
        public event Action OnUnexpectedRoomDisconnected;

        /// <inheritdoc />
        public event Action<IRemoteAudioTrack, string, string> OnRemoteAudioTrackSubscribed;

        /// <inheritdoc />
        public event Action<string, string> OnRemoteAudioTrackUnsubscribed;

        /// <inheritdoc />
        public Task<bool> InitializeAsync(string connectionType, string llmProvider, string coreServerUrl,
            string characterId, string storedSessionId, bool enableSessionResume) => InitializeAsync(connectionType,
            llmProvider, coreServerUrl, characterId, storedSessionId, enableSessionResume, null,
            CancellationToken.None);

        /// <inheritdoc />
        public async Task<bool> InitializeAsync(
            string connectionType,
            string llmProvider,
            string coreServerUrl,
            string characterId,
            string storedSessionId,
            bool enableSessionResume,
            RoomJoinOptions joinOptions,
            CancellationToken cancellationToken = default)
        {
            HasRoomDetails = false;

            string endUserId = string.IsNullOrWhiteSpace(_config.EndUserId) ? null : _config.EndUserId;

            string sessionId = joinOptions?.CharacterSessionId ?? storedSessionId;
            RoomEmotionConfig emotionConfig = await ResolveEmotionConfigAsync(
                characterId,
                cancellationToken);

            RoomConnectionRequest roomRequest = RoomConnectionRequestFactory.Create(
                characterId,
                connectionType,
                llmProvider,
                coreServerUrl,
                sessionId,
                endUserId,
                _config.VideoTrackName,
                emotionConfig,
                joinOptions,
                _config.LipSyncTransportOptions);

            if (joinOptions != null && joinOptions.IsJoinRequest)
            {
                _logger.Debug($"Room join mode: room={joinOptions.RoomName}, spawnAgent={joinOptions.SpawnAgent}",
                    LogCategory.Transport);
            }
            else
                _logger.Debug("Room create mode (new room)", LogCategory.Transport);

            bool connected = await ConnectToConvai(roomRequest, characterId, enableSessionResume);
            HasRoomDetails = connected;
            IsConnectedToRoom = connected;

            if (!connected)
            {
                _logger.Error("Failed to connect to Convai and LiveKit room", LogCategory.Transport);
                OnRoomConnectionFailed?.Invoke();
                return false;
            }

            _logger.Info("Connected to Convai and LiveKit room successfully", LogCategory.Transport);
            OnRoomConnectionSuccessful?.Invoke();
            return true;
        }


        /// <summary>
        ///     Disconnects from the room synchronously (fire-and-forget).
        ///     For proper async disconnect that waits for completion, use <see cref="DisconnectFromRoomAsync" />.
        /// </summary>
        public void DisconnectFromRoom() => _ = DisconnectFromRoomAsync();

        /// <summary>
        ///     Disconnects from the room asynchronously, waiting for the underlying transport to complete cleanup.
        /// </summary>
        /// <param name="cancellationToken">Optional cancellation token.</param>
        /// <returns>A task that completes when the disconnect is finished.</returns>
        public async Task DisconnectFromRoomAsync(CancellationToken cancellationToken = default)
        {
            _logger.Debug("Disconnecting from room...", LogCategory.Transport);

            await DisconnectFromRoomViaTransport(cancellationToken);

            ResetControllerOwnedDisconnectState();

            _logger.Info("Disconnected from room", LogCategory.Transport);
        }

        /// <summary>Sets whether the local microphone is muted.</summary>
        /// <param name="mute">True to mute; false to unmute.</param>
        public void SetMicMuted(bool mute)
        {
            IsMicMuted = mute;

            _playerSession.SetMicMuted(mute);

            ILocalParticipant localParticipant = CurrentRoom?.LocalParticipant;
            if (localParticipant != null)
            {
                try
                {
                    localParticipant.SetAudioMuted(mute);
                }
                catch (Exception ex)
                {
                    _logger.Warning(
                        $"[NativeRoomController] Failed to apply local participant mic mute state: {ex.Message}",
                        LogCategory.Audio);
                }
            }

            OnMicMuteChanged?.Invoke(IsMicMuted);
        }

        /// <summary>Toggles the local microphone mute state.</summary>
        public void ToggleMicMute() => SetMicMuted(!IsMicMuted);

        /// <summary>Sets whether the given character's audio is muted.</summary>
        /// <param name="characterId">Character identifier.</param>
        /// <param name="mute">True to mute; false to unmute.</param>
        /// <returns>True when the state is applied; otherwise false.</returns>
        public bool SetCharacterAudioMuted(string characterId, bool mute)
        {
            if (string.IsNullOrEmpty(characterId))
            {
                _logger.Debug("Attempted to set mute on a null Character ID", LogCategory.Character);
                return false;
            }

            if (!_characterRegistry.TryGetCharacter(characterId, out CharacterDescriptor _))
            {
                _logger.Debug($"Character '{characterId}' is not registered; cannot update mute state.",
                    LogCategory.Character);
                return false;
            }

            _characterRegistry.SetCharacterMuted(characterId, mute);
            return true;
        }

        /// <summary>Mutes the given character's audio.</summary>
        /// <param name="characterId">Character identifier.</param>
        /// <returns>True when the state is applied; otherwise false.</returns>
        public bool MuteCharacter(string characterId) => SetCharacterAudioMuted(characterId, true);

        /// <summary>Unmutes the given character's audio.</summary>
        /// <param name="characterId">Character identifier.</param>
        /// <returns>True when the state is applied; otherwise false.</returns>
        public bool UnmuteCharacter(string characterId) => SetCharacterAudioMuted(characterId, false);

        /// <summary>Gets whether the given character's audio is muted.</summary>
        /// <param name="characterId">Character identifier.</param>
        /// <returns>True if muted; otherwise false.</returns>
        public bool IsCharacterAudioMuted(string characterId)
        {
            if (string.IsNullOrEmpty(characterId)) return false;

            return _characterRegistry.TryGetCharacter(characterId, out CharacterDescriptor descriptor) &&
                   descriptor.IsMuted;
        }

        /// <summary>
        ///     Sets the per-character audio subscription policy callback.
        ///     The callback is invoked when an audio track is published to determine if it should be subscribed.
        /// </summary>
        /// <param name="policy">A function that returns true if audio should be subscribed for the given participant identity.</param>
        public void SetAudioSubscriptionPolicy(Func<string, bool> policy)
        {
            _audioSubscriptionPolicy = policy;

            _logger.Debug("[NativeRoomController] Audio subscription policy configured", LogCategory.Audio);
        }

        /// <summary>
        ///     Applies the remote audio preference for a character at runtime.
        ///     Call this when the preference changes after the track has already been subscribed/unsubscribed.
        /// </summary>
        /// <param name="characterId">The character identifier.</param>
        /// <param name="enabled">True to enable (subscribe) audio; false to disable (unsubscribe).</param>
        public void ApplyRemoteAudioPreference(string characterId, bool enabled)
        {
            if (string.IsNullOrEmpty(characterId)) return;

            CharacterDescriptor descriptor = default;
            string participantIdentity = ResolveParticipantIdentity(characterId, ref descriptor);
            string participantSid = descriptor.ParticipantId ?? string.Empty;

            bool applied = ResolveRemoteAudioControl()
                .Apply(characterId, participantIdentity, participantSid, enabled, descriptor);
            if (!applied)
            {
                _logger.Debug(
                    $"[NativeRoomController] No cached remote-audio control target for character: {characterId} (identity: {participantIdentity})",
                    LogCategory.Audio);
            }
        }

        #region IDisposable

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed) return;

            _transport.StateChanged -= HandleConnectionStateChanged;
            _transport.ConnectionFailed -= HandleConnectionFailed;
            _transport.DataReceived -= HandleDataPacketReceived;
            _transport.ParticipantConnected -= OnTransportParticipantConnected;
            _transport.ParticipantDisconnected -= OnTransportParticipantDisconnected;
            _transport.TrackSubscribed -= OnTransportTrackSubscribed;
            _transport.TrackUnsubscribed -= OnTransportTrackUnsubscribed;
            _transport.Disconnected -= HandleTransportDisconnected;
            _transport.Reconnecting -= HandleTransportReconnecting;
            _transport.Reconnected -= HandleTransportReconnected;
            DetachRoomFacadeAudioBridge();
            _disposed = true;
        }

        #endregion

        /// <summary>
        ///     Connects to a Convai room using the specified room request and character ID, with optional session resume.
        /// </summary>
        public async Task<bool> ConnectToConvai(RoomConnectionRequest roomRequest, string characterId,
            bool enableSessionResume)
        {
            Task<(bool success, RoomDetails details, string error)> roomDetailsTask =
                TryGetRoomDetailsAsync(roomRequest);
            bool hasSessionId = enableSessionResume && !string.IsNullOrEmpty(roomRequest.CharacterSessionId);

            (bool success, RoomDetails details, string error) attemptWithSessionId = await roomDetailsTask;
            if (attemptWithSessionId.success)
            {
                ApplyRoomDetails(attemptWithSessionId.details);
                if (enableSessionResume && !string.IsNullOrEmpty(CharacterSessionID))
                {
                    _config.StoreCharacterSessionId(characterId, CharacterSessionID);
                    _logger.Debug($"Stored character session ID for character {characterId}: {CharacterSessionID}",
                        LogCategory.Transport);
                }

                return await ConnectToRoomAndInitialize();
            }

            bool invalidSession = hasSessionId &&
                                  !string.IsNullOrEmpty(attemptWithSessionId.error) &&
                                  attemptWithSessionId.error.Contains("Invalid character_session_id");
            if (invalidSession)
            {
                _logger.Warning("Invalid stored character_session_id. Clearing and retrying without session.",
                    LogCategory.Transport);
                _config.ClearCharacterSessionId(characterId);
                roomRequest.CharacterSessionId = null;

                (bool success, RoomDetails details, string error) attemptWithoutSessionId =
                    await TryGetRoomDetailsAsync(roomRequest);
                if (attemptWithoutSessionId.success)
                {
                    ApplyRoomDetails(attemptWithoutSessionId.details);
                    return await ConnectToRoomAndInitialize();
                }

                _logger.Error($"Error: {attemptWithoutSessionId.error}", LogCategory.Transport);
                return false;
            }

            _logger.Error($"Error: {attemptWithSessionId.error}", LogCategory.Transport);
            return false;
        }

        private async Task<(bool success, RoomDetails details, string error)> TryGetRoomDetailsAsync(
            RoomConnectionRequest roomRequest)
        {
            try
            {
                var options = new ConvaiRestClientOptions(_config.ApiKey);
                using var client = new ConvaiRestClient(options);
                RoomDetails details = await client.Rooms.ConnectAsync(roomRequest).ConfigureAwait(false);
                return (true, details, null);
            }
            catch (Exception ex)
            {
                return (false, null, ex.Message);
            }
        }

        private async Task<RoomEmotionConfig> ResolveEmotionConfigAsync(
            string characterId,
            CancellationToken cancellationToken)
        {
            try
            {
                var options = new ConvaiRestClientOptions(_config.ApiKey);
                using var client = new ConvaiRestClient(options);
                CharacterDetails details = await client.Characters
                    .GetDetailsAsync(characterId, cancellationToken)
                    .ConfigureAwait(false);

                if (details != null && details.TryGetConnectEmotionConfig(out RoomEmotionConfig emotionConfig))
                {
                    _logger.Debug(
                        $"Emotion config enabled for character {characterId} with provider: {emotionConfig.Provider}",
                        LogCategory.Transport);
                    return emotionConfig;
                }

                _logger.Debug($"Emotion config disabled for character {characterId}", LogCategory.Transport);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.Warning(
                    $"Failed to resolve emotion config for character {characterId}: {ex.Message}. Continuing without emotion_config.",
                    LogCategory.Transport);
            }

            return null;
        }

        private void ApplyRoomDetails(RoomDetails roomDetails)
        {
            string result = JsonConvert.SerializeObject(roomDetails);
            _logger.Debug($"Room details received: {result}", LogCategory.Transport);
            Token = roomDetails.Token;
            RoomName = roomDetails.RoomName;
            SessionID = roomDetails.SessionId;
            RoomURL = roomDetails.RoomURL;
            CharacterSessionID = roomDetails.CharacterSessionId;
            ResolvedSpeakerId = roomDetails.SpeakerId;
            _logger.Debug(
                $"Token: {Token}; Room Name: {RoomName}; Room URL: {RoomURL}; Session ID: {SessionID}; Character Session ID: {CharacterSessionID}; Resolved Speaker ID: {ResolvedSpeakerId}",
                LogCategory.Transport);
        }

        /// <summary>
        ///     Connects to the LiveKit room and initializes the RTVI handler.
        /// </summary>
        private async Task<bool> ConnectToRoomAndInitialize()
        {
            bool roomConnected = await ConnectToRoom();
            if (roomConnected)
            {
                _logger.Debug("Connected to Convai and LiveKit room successfully", LogCategory.Transport);

                RTVIHandler = new RTVIHandler(_protocolGateway, _transport, _characterRegistry, _playerSession,
                    _dispatcher, _logger, _eventHub, _sectionNameResolver, _config.LipSyncTransportOptions);
                return true;
            }

            return false;
        }

        private async Task<bool> ConnectToRoom()
        {
            _logger.Debug("Connecting to Room...", LogCategory.Transport);

            return await ConnectToRoomViaTransport();
        }

        /// <summary>
        ///     Connects to room using the IRealtimeTransport abstraction layer.
        /// </summary>
        private async Task<bool> ConnectToRoomViaTransport()
        {
            _logger.Debug("Connecting to Room via Transport abstraction...", LogCategory.Transport);

            try
            {
                var transportOptions = new TransportConnectOptions { AutoSubscribe = true };

                bool connected = await _transport.ConnectAsync(RoomURL, Token, transportOptions);

                if (connected)
                {
                    RefreshRoomFacadeAudioBridge();
                    _logger.Debug("Connected to room (via transport): " + RoomName, LogCategory.Transport);
                    _logger.Debug("Session ID: " + SessionID, LogCategory.Transport);
                    _logger.Debug($"Transport state: {_transport.State}", LogCategory.Transport);
                    return true;
                }

                _logger.Error("Failed to connect via transport: Connection returned false", LogCategory.Transport);
                return false;
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to connect via transport: {ex.Message}", LogCategory.Transport);
                return false;
            }
        }

        /// <summary>
        ///     Disconnects from room using the IRealtimeTransport abstraction layer.
        /// </summary>
        private async Task DisconnectFromRoomViaTransport(CancellationToken cancellationToken)
        {
            _logger.Debug("Disconnecting via Transport...", LogCategory.Transport);

            DetachRoomFacadeAudioBridge();

            await _transport.DisconnectAsync(DisconnectReason.ClientInitiated, cancellationToken);
        }


        private void ResetControllerOwnedDisconnectState()
        {
            RTVIHandler = null;
            Token = null;
            RoomName = null;
            RoomURL = null;
            SessionID = null;
            CharacterSessionID = null;
            ResolvedSpeakerId = null;
            HasRoomDetails = false;
            IsConnectedToRoom = false;
            IsMicMuted = false;
            _playerSession.SetMicMuted(false);
            _characterRegistry.Clear();
        }

        private void HandleUnsolicitedDisconnectCleanup(string pathLabel)
        {
            _logger.Debug($"[NativeRoomController] Running unsolicited disconnect cleanup via {pathLabel}",
                LogCategory.Transport);
            ResetControllerOwnedDisconnectState();
            OnUnexpectedRoomDisconnected?.Invoke();
        }

        private string ResolveParticipantIdentity(string characterId, ref CharacterDescriptor descriptor)
        {
            string participantIdentity = characterId;
            if (!_characterRegistry.TryGetCharacter(characterId, out descriptor) ||
                string.IsNullOrEmpty(descriptor.ParticipantId)) return participantIdentity;

            IRoomFacade currentRoom = CurrentRoom;
            if (currentRoom != null &&
                currentRoom.TryGetParticipantBySid(descriptor.ParticipantId, out IRemoteParticipant participant) &&
                !string.IsNullOrEmpty(participant.Identity))
                participantIdentity = participant.Identity;

            return participantIdentity;
        }

        private IRemoteAudioControl ResolveRemoteAudioControl()
        {
            _remoteAudioControl ??= CreateRemoteAudioControl();
            return _remoteAudioControl;
        }

        private string ResolveCharacterIdFromParticipant(string participantSid, string participantIdentity = null)
        {
            if (!string.IsNullOrEmpty(participantSid) &&
                _characterRegistry.TryGetCharacterByParticipantId(participantSid,
                    out CharacterDescriptor byParticipant))
                return byParticipant.CharacterId;

            if (!string.IsNullOrEmpty(participantIdentity) &&
                _characterRegistry.TryGetCharacter(participantIdentity, out CharacterDescriptor byIdentity))
                return byIdentity.CharacterId;

            IReadOnlyList<CharacterDescriptor> allCharacters = _characterRegistry.GetAllCharacters();
            return allCharacters != null && allCharacters.Count == 1 ? allCharacters[0].CharacterId : null;
        }

        private void RefreshRoomFacadeAudioBridge()
        {
            IRoomFacade currentRoom = CurrentRoom;
            if (ReferenceEquals(_subscribedRoomFacade, currentRoom)) return;

            DetachRoomFacadeAudioBridge();
            if (currentRoom == null) return;

            currentRoom.AudioTrackSubscribed += OnRoomFacadeAudioTrackSubscribed;
            _subscribedRoomFacade = currentRoom;
        }

        private void DetachRoomFacadeAudioBridge()
        {
            if (_subscribedRoomFacade != null)
                _subscribedRoomFacade.AudioTrackSubscribed -= OnRoomFacadeAudioTrackSubscribed;

            _subscribedRoomFacade = null;
            _pendingTransportAudioSubscriptions.Clear();
        }

        private bool QueuePendingTransportAudioSubscription(TransportTrackInfo track)
        {
            if (string.IsNullOrEmpty(track.TrackSid)) return false;

            RefreshRoomFacadeAudioBridge();
            if (_subscribedRoomFacade == null) return false;

            _pendingTransportAudioSubscriptions[track.TrackSid] = track;
            return true;
        }

        private void OnRoomFacadeAudioTrackSubscribed(IRemoteAudioTrack audioTrack, IRemoteParticipant participant)
        {
            if (audioTrack == null || participant == null || string.IsNullOrEmpty(audioTrack.Sid)) return;
            if (!_pendingTransportAudioSubscriptions.TryGetValue(audioTrack.Sid, out TransportTrackInfo track)) return;

            _pendingTransportAudioSubscriptions.Remove(audioTrack.Sid);

            bool shouldSubscribe =
                _audioSubscriptionPolicy?.Invoke(track.ParticipantIdentity ?? participant.Identity) ?? true;
            HandleResolvedTransportAudioSubscription(track, participant, audioTrack, shouldSubscribe, "room-facade");
        }

        private void HandleResolvedTransportAudioSubscription(
            TransportTrackInfo track,
            IRemoteParticipant participant,
            IRemoteAudioTrack audioTrack,
            bool shouldSubscribe,
            string resolutionPath)
        {
            if (participant == null || audioTrack == null) return;

            string participantSid = !string.IsNullOrEmpty(participant.Sid) ? participant.Sid : track.ParticipantId;
            string participantIdentity = !string.IsNullOrEmpty(participant.Identity)
                ? participant.Identity
                : track.ParticipantIdentity;
            string characterId = ResolveCharacterIdFromParticipant(participantSid, participantIdentity);

            if (!shouldSubscribe)
            {
                _logger.Debug(
                    $"[NativeRoomController] Remote audio disabled for participant: {participantIdentity}; disabling audio track via {resolutionPath} resolution seam.",
                    LogCategory.Audio);

                if (audioTrack is IRemoteAudioControlTrack controllableTrack)
                    controllableTrack.SetRemoteAudioEnabled(false);

                return;
            }

            _logger.Debug(
                $"[NativeRoomController] Audio track detected via {resolutionPath}: Name={track.Name}, Sid={track.TrackSid}",
                LogCategory.Audio);
            OnRemoteAudioTrackSubscribed?.Invoke(audioTrack, participantSid, characterId);
        }

        private IRemoteAudioControl CreateRemoteAudioControl() => new RoomFacadeRemoteAudioControl(this);

        private void HandleConnectionStateChanged(TransportState state) =>
            IsConnectedToRoom = state == TransportState.Connected;

        private void HandleConnectionFailed(TransportError error) =>
            _logger.Error($"Transport connection failed: {error.Message}", LogCategory.Transport);

        private void HandleDataPacketReceived(DataPacket packet)
        {
            ProtocolPacket protocolPacket = new(packet.Payload, packet.ParticipantId, packet.Topic,
                packet.Kind == DataPacketKind.Reliable);
            // Fast-path: intercept LipSync packets before JSON deserialization in the gateway.
            if (RTVIHandler != null && RTVIHandler.TryHandleLipSyncServerMessage(in protocolPacket)) return;

            _protocolGateway.ProcessIncoming(protocolPacket);
        }

        #region Transport Event Handlers

        private void HandleTransportDisconnected(DisconnectReason reason)
        {
            _logger.Debug($"[NativeRoomController] Transport disconnected with reason: {reason}",
                LogCategory.Transport);

            if (reason == DisconnectReason.ClientInitiated) return;

            HandleUnsolicitedDisconnectCleanup("transport-disconnected-event");
        }

        private void OnTransportParticipantConnected(TransportParticipantInfo participant)
        {
            _logger.Debug($"Participant connected (via transport): {participant.Identity}", LogCategory.Transport);
            _logger.Debug($"Participant SID: {participant.ParticipantId}", LogCategory.Transport);

            bool matchedRegistry =
                _characterRegistry.TryGetCharacter(participant.Identity, out CharacterDescriptor descriptor);
            if (!matchedRegistry)
            {
                IReadOnlyList<CharacterDescriptor> allCharacters = _characterRegistry.GetAllCharacters();
                if (allCharacters.Count == 0)
                {
                    _logger.Debug("Cannot map participant: No Characters in registry", LogCategory.Character);
                    return;
                }

                descriptor = allCharacters[0];
                _logger.Debug(
                    $"No Character matched identity '{participant.Identity}'. Using default Character: {descriptor.CharacterId}",
                    LogCategory.Character);
            }

            CharacterDescriptor updated = descriptor.WithParticipantId(participant.ParticipantId);
            _characterRegistry.RegisterCharacter(updated);
            _logger.Debug(
                $"Mapped participant {participant.Identity} (SID: {participant.ParticipantId}) to Character: {updated.CharacterId}",
                LogCategory.Character);

            PublishParticipantConnectedEvent(participant.ParticipantId, participant.Identity, participant.Identity);
        }

        private void OnTransportParticipantDisconnected(TransportParticipantInfo participant)
        {
            _logger.Debug($"Participant disconnected (via transport): {participant.Identity}", LogCategory.Transport);

            if (_characterRegistry.TryGetCharacterByParticipantId(participant.ParticipantId,
                    out CharacterDescriptor descriptor))
            {
                _characterRegistry.RegisterCharacter(descriptor.WithParticipantId(string.Empty));
                _logger.Debug($"Cleared participant mapping for Character: {descriptor.CharacterId}",
                    LogCategory.Character);
            }

            PublishParticipantDisconnectedEvent(participant.ParticipantId, participant.Identity, participant.Identity);
        }

        private void OnTransportTrackSubscribed(TransportTrackInfo track)
        {
            _logger.Debug(
                $"[NativeRoomController] Track subscribed (via transport): {track.Name} from participant: {track.ParticipantIdentity}",
                LogCategory.Transport);

            if (track.Kind == TransportTrackKind.Audio)
            {
                bool shouldSubscribe = _audioSubscriptionPolicy?.Invoke(track.ParticipantIdentity) ?? true;

                if (!TryResolveTransportRemoteAudioTrack(track.ParticipantIdentity, track.ParticipantId, track.TrackSid,
                        out IRemoteParticipant participant, out IRemoteAudioTrack audioTrack))
                {
                    if (QueuePendingTransportAudioSubscription(track))
                    {
                        _logger.Debug(
                            $"[NativeRoomController] Transport audio track not yet available in room facade; awaiting track wrapper for participant: {track.ParticipantIdentity} ({track.ParticipantId})",
                            LogCategory.Audio);
                        return;
                    }

                    _logger.Debug(
                        $"[NativeRoomController] Unable to resolve transport audio track for participant: {track.ParticipantIdentity} ({track.ParticipantId})",
                        LogCategory.Audio);
                    return;
                }

                HandleResolvedTransportAudioSubscription(track, participant, audioTrack, shouldSubscribe, "transport");
            }
        }

        private void OnTransportTrackUnsubscribed(TransportTrackInfo track)
        {
            _logger.Debug(
                $"Track unsubscribed (via transport): {track.Name} from participant: {track.ParticipantIdentity}",
                LogCategory.Transport);

            if (track.Kind == TransportTrackKind.Audio)
            {
                if (!string.IsNullOrEmpty(track.TrackSid))
                    _pendingTransportAudioSubscriptions.Remove(track.TrackSid);

                _logger.Debug($"Audio track unsubscribed for participant: {track.ParticipantIdentity}",
                    LogCategory.Audio);
                OnRemoteAudioTrackUnsubscribed?.Invoke(track.ParticipantId, null);
            }
        }

        private bool TryResolveTransportRemoteAudioTrack(
            string participantIdentity,
            string participantSid,
            string trackSid,
            out IRemoteParticipant participant,
            out IRemoteAudioTrack audioTrack)
        {
            participant = null;
            audioTrack = null;

            IRoomFacade room = CurrentRoom;
            if (room == null) return false;

            bool foundParticipant =
                (!string.IsNullOrEmpty(participantSid) &&
                 room.TryGetParticipantBySid(participantSid, out participant)) ||
                (!string.IsNullOrEmpty(participantIdentity) &&
                 room.TryGetParticipantByIdentity(participantIdentity, out participant));

            if (!foundParticipant || participant == null) return false;

            foreach (IRemoteAudioTrack candidate in participant.AudioTracks)
            {
                if (string.IsNullOrEmpty(trackSid) || string.Equals(candidate.Sid, trackSid, StringComparison.Ordinal))
                {
                    audioTrack = candidate;
                    break;
                }
            }

            return audioTrack != null;
        }

        private void HandleTransportReconnecting()
        {
            _logger.Debug("[NativeRoomController] Room reconnecting (via transport)", LogCategory.Transport);
            OnRoomReconnecting?.Invoke();
        }

        private void HandleTransportReconnected()
        {
            RefreshRoomFacadeAudioBridge();
            _logger.Debug("[NativeRoomController] Room reconnected (via transport)", LogCategory.Transport);
            OnRoomReconnected?.Invoke();
        }

        #endregion

        #region Session Management

        /// <summary>Gets the stored session identifier for the given character, if available.</summary>
        /// <param name="characterId">Character identifier.</param>
        /// <returns>Stored session identifier, or null if not set.</returns>
        public string GetStoredSessionId(string characterId) => _config?.GetCharacterSessionId(characterId);

        /// <summary>Clears the stored session identifier for the given character.</summary>
        /// <param name="characterId">Character identifier.</param>
        public void ClearStoredSessionId(string characterId)
        {
            _config?.ClearCharacterSessionId(characterId);
            _logger.Debug($"Cleared stored session ID for character: {characterId}", LogCategory.Transport);
        }

        /// <summary>Clears all stored character session identifiers.</summary>
        public void ClearAllStoredSessionIds()
        {
            _config?.ClearAllCharacterSessionIds();
            _logger.Debug("Cleared all stored session IDs", LogCategory.Transport);
        }

        /// <summary>Gets the current character session identifier for the active room connection.</summary>
        /// <returns>Character session identifier.</returns>
        public string GetCurrentCharacterSessionId() => CharacterSessionID;

        #endregion

        #region SDK Event Bridge (Domain Events)

        /// <summary>
        ///     Publishes a <see cref="Domain.DomainEvents.Participant.ParticipantConnected" /> event via EventHub.
        /// </summary>
        private void PublishParticipantConnectedEvent(string participantId, string identity, string displayName)
        {
            if (_eventHub == null)
            {
                _logger?.Debug("[NativeRoomController] EventHub unavailable; skipping ParticipantConnected event.");
                return;
            }

            try
            {
                ParticipantConnected evt = ParticipantConnected.ForCharacter(participantId, identity, displayName);
                _eventHub.Publish(evt);
                _logger?.Debug(
                    $"[NativeRoomController] Published ParticipantConnected via EventHub: {displayName} ({participantId})");
            }
            catch (Exception ex)
            {
                _logger?.Debug($"[NativeRoomController] Failed to publish ParticipantConnected event: {ex.Message}");
            }
        }

        /// <summary>
        ///     Publishes a <see cref="Domain.DomainEvents.Participant.ParticipantDisconnected" /> event via EventHub.
        /// </summary>
        private void PublishParticipantDisconnectedEvent(string participantId, string identity, string displayName)
        {
            if (_eventHub == null)
            {
                _logger?.Debug("[NativeRoomController] EventHub unavailable; skipping ParticipantDisconnected event.");
                return;
            }

            try
            {
                ParticipantDisconnected
                    evt = ParticipantDisconnected.ForCharacter(participantId, identity, displayName);
                _eventHub.Publish(evt);
                _logger?.Debug(
                    $"[NativeRoomController] Published ParticipantDisconnected via EventHub: {displayName} ({participantId})");
            }
            catch (Exception ex)
            {
                _logger?.Debug($"[NativeRoomController] Failed to publish ParticipantDisconnected event: {ex.Message}");
            }
        }

        #endregion

        #region Event Helpers

        private interface IRemoteAudioControl
        {
            public string PathName { get; }

            public bool Apply(string characterId, string participantIdentity, string participantSid, bool enabled,
                CharacterDescriptor descriptor);
        }

        private sealed class RoomFacadeRemoteAudioControl : IRemoteAudioControl
        {
            private readonly NativeRoomController _owner;

            public RoomFacadeRemoteAudioControl(NativeRoomController owner)
            {
                _owner = owner;
            }

            public string PathName => "room-facade";

            public bool Apply(string characterId, string participantIdentity, string participantSid, bool enabled,
                CharacterDescriptor descriptor)
            {
                if (!_owner.TryResolveTransportRemoteAudioTrack(participantIdentity, participantSid, null,
                        out IRemoteParticipant participant, out IRemoteAudioTrack audioTrack) ||
                    !(audioTrack is IRemoteAudioControlTrack controllableTrack))
                    return false;

                _owner._logger.Debug(
                    $"[NativeRoomController] {(enabled ? "Enabling" : "Disabling")} remote audio for character: {characterId} via room facade control seam.",
                    LogCategory.Audio);

                controllableTrack.SetRemoteAudioEnabled(enabled);

                string resolvedParticipantSid = !string.IsNullOrEmpty(participant?.Sid)
                    ? participant.Sid
                    : participantSid;
                string resolvedParticipantIdentity = !string.IsNullOrEmpty(participant?.Identity)
                    ? participant.Identity
                    : participantIdentity;

                if (enabled)
                    _owner.OnRemoteAudioTrackSubscribed?.Invoke(audioTrack, resolvedParticipantSid, characterId);
                else if (!string.IsNullOrEmpty(resolvedParticipantSid))
                    _owner.OnRemoteAudioTrackUnsubscribed?.Invoke(resolvedParticipantSid, characterId);

                return true;
            }
        }

        #endregion
    }
}
