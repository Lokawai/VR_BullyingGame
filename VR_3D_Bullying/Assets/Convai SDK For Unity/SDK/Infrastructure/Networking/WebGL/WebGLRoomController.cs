using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Convai.Domain.Abstractions;
using Convai.Domain.DomainEvents.Participant;
using Convai.Domain.DomainEvents.Session;
using Convai.Domain.Errors;
using Convai.Domain.EventSystem;
using Convai.Infrastructure.Networking.Models;
using Convai.Infrastructure.Networking.Transport;
using Convai.Infrastructure.Protocol;
using Convai.RestAPI;
using Convai.RestAPI.Internal;
using Convai.RestAPI.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;
using ILogger = Convai.Domain.Logging.ILogger;
using LogCategory = Convai.Domain.Logging.LogCategory;

namespace Convai.Infrastructure.Networking.WebGL
{
    /// <summary>
    ///     WebGL implementation of IConvaiRoomController using IRealtimeTransport.
    ///     Provides room connection and management for WebGL platforms.
    /// </summary>
    internal sealed class WebGLRoomController : IConvaiRoomController
    {
        private readonly ICharacterRegistry _characterRegistry;
        private readonly IConfigurationProvider _config;
        private readonly MonoBehaviour _coroutineRunner;
        private readonly IMainThreadDispatcher _dispatcher;
        private readonly IEventHub _eventHub;
        private readonly ILogger _logger;
        private readonly IPlayerSession _playerSession;
        private readonly ProtocolGateway _protocolGateway;
        private readonly INarrativeSectionNameResolver _sectionNameResolver;

        private readonly object _stateLock = new();
        private readonly IRealtimeTransport _transport;
        private string _characterSessionId;
        private bool _disposed;

        private bool _hasRoomDetails;
        private bool _isConnectedToRoom;
        private bool _isMicMuted;
        private string _resolvedSpeakerId;
        private string _roomName;
        private string _roomUrl;

        private string _sessionId;
        private string _targetCharacterId;
        private string _token;

        /// <summary>
        ///     Creates a new WebGLRoomController.
        /// </summary>
        /// <param name="characterRegistry">Character registry for looking up characters.</param>
        /// <param name="playerSession">Player session information.</param>
        /// <param name="config">Configuration provider.</param>
        /// <param name="dispatcher">Main thread dispatcher.</param>
        /// <param name="logger">Logger for diagnostics.</param>
        /// <param name="eventHub">Event hub for domain events.</param>
        /// <param name="transport">The realtime transport implementation.</param>
        /// <param name="coroutineRunner">MonoBehaviour for running coroutines (required for WebGL HTTP calls).</param>
        /// <param name="sectionNameResolver">Optional narrative section resolver.</param>
        public WebGLRoomController(
            ICharacterRegistry characterRegistry,
            IPlayerSession playerSession,
            IConfigurationProvider config,
            IMainThreadDispatcher dispatcher,
            ILogger logger,
            IEventHub eventHub,
            IRealtimeTransport transport,
            MonoBehaviour coroutineRunner,
            INarrativeSectionNameResolver sectionNameResolver = null)
        {
            _characterRegistry = characterRegistry ?? throw new ArgumentNullException(nameof(characterRegistry));
            _playerSession = playerSession ?? throw new ArgumentNullException(nameof(playerSession));
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _eventHub = eventHub;
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _coroutineRunner = coroutineRunner ?? throw new ArgumentNullException(nameof(coroutineRunner));
            _sectionNameResolver = sectionNameResolver;

            _protocolGateway = new ProtocolGateway(
                null,
                msg => _logger.Debug(msg, LogCategory.Transport),
                msg => _logger.Error(msg, LogCategory.Transport));

            // Subscribe to transport events
            SubscribeToTransportEvents();
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed) return;

            UnsubscribeFromTransportEvents();
            _disposed = true;
        }

        private void SubscribeToTransportEvents()
        {
            _transport.Connected += OnTransportConnected;
            _transport.Disconnected += OnTransportDisconnected;
            _transport.Reconnecting += OnTransportReconnecting;
            _transport.Reconnected += OnTransportReconnected;
            _transport.ParticipantConnected += OnParticipantConnected;
            _transport.ParticipantDisconnected += OnParticipantDisconnected;
            _transport.TrackSubscribed += OnTrackSubscribed;
            _transport.TrackUnsubscribed += OnTrackUnsubscribed;
            _transport.DataReceived += OnDataReceived;
        }

        private void UnsubscribeFromTransportEvents()
        {
            _transport.Connected -= OnTransportConnected;
            _transport.Disconnected -= OnTransportDisconnected;
            _transport.Reconnecting -= OnTransportReconnecting;
            _transport.Reconnected -= OnTransportReconnected;
            _transport.ParticipantConnected -= OnParticipantConnected;
            _transport.ParticipantDisconnected -= OnParticipantDisconnected;
            _transport.TrackSubscribed -= OnTrackSubscribed;
            _transport.TrackUnsubscribed -= OnTrackUnsubscribed;
            _transport.DataReceived -= OnDataReceived;
        }

        #region IConvaiRoomController State Properties

        /// <inheritdoc />
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

        /// <inheritdoc />
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

        /// <inheritdoc />
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

        /// <inheritdoc />
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

        /// <inheritdoc />
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

        /// <inheritdoc />
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

        /// <inheritdoc />
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

        /// <inheritdoc />
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

        /// <inheritdoc />
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
        public RTVIHandler RTVIHandler { get; private set; }

        /// <inheritdoc />
        public IRoomFacade CurrentRoom { get; private set; }

        #endregion

        #region Events

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

        #endregion

        #region Transport Event Handlers

        private void OnTransportConnected(TransportSessionInfo sessionInfo)
        {
            CurrentRoom = _transport.Room;
            _roomName = sessionInfo.RoomName;
            _sessionId = sessionInfo.SessionId;
            _characterSessionId = sessionInfo.CharacterSessionId;
            IsConnectedToRoom = true;
            _logger.Info("[WebGLRoomController] Connected to room", LogCategory.Transport);

            _dispatcher.TryDispatch(() => OnRoomConnectionSuccessful?.Invoke());
        }

        private void OnTransportDisconnected(DisconnectReason reason)
        {
            IsConnectedToRoom = false;
            CurrentRoom = null;
            _logger.Info($"[WebGLRoomController] Disconnected from room: {reason}", LogCategory.Transport);

            if (reason != DisconnectReason.ClientInitiated)
                _dispatcher.TryDispatch(() => OnUnexpectedRoomDisconnected?.Invoke());
        }

        private void OnTransportReconnecting()
        {
            _logger.Info("[WebGLRoomController] Reconnecting to room...", LogCategory.Transport);
            _dispatcher.TryDispatch(() => OnRoomReconnecting?.Invoke());
        }

        private void OnTransportReconnected()
        {
            _logger.Info("[WebGLRoomController] Reconnected to room", LogCategory.Transport);
            _dispatcher.TryDispatch(() => OnRoomReconnected?.Invoke());
        }

        private void OnParticipantConnected(TransportParticipantInfo info)
        {
            if (string.IsNullOrEmpty(info.ParticipantId) || _characterRegistry == null) return;

            string characterId = TryResolveCharacterId(info);
            if (string.IsNullOrEmpty(characterId)) return;

            if (_characterRegistry.TryGetCharacter(characterId, out CharacterDescriptor descriptor))
            {
                if (!string.Equals(descriptor.ParticipantId, info.ParticipantId, StringComparison.OrdinalIgnoreCase))
                {
                    _characterRegistry.RegisterCharacter(descriptor.WithParticipantId(info.ParticipantId));

                    try
                    {
                        _eventHub?.Publish(ParticipantConnected.ForCharacter(
                            info.ParticipantId,
                            descriptor.CharacterId,
                            descriptor.CharacterName));
                    }
                    catch (Exception ex)
                    {
                        _logger?.Debug($"[WebGLRoomController] Failed to publish ParticipantConnected: {ex.Message}",
                            LogCategory.Transport);
                    }
                }
            }
        }

        private void OnParticipantDisconnected(TransportParticipantInfo info)
        {
            if (string.IsNullOrEmpty(info.ParticipantId) || _characterRegistry == null) return;

            if (_characterRegistry.TryGetCharacterByParticipantId(info.ParticipantId,
                    out CharacterDescriptor descriptor))
            {
                try
                {
                    _eventHub?.Publish(ParticipantDisconnected.ForCharacter(
                        info.ParticipantId,
                        descriptor.CharacterId,
                        descriptor.CharacterName));
                }
                catch (Exception ex)
                {
                    _logger?.Debug($"[WebGLRoomController] Failed to publish ParticipantDisconnected: {ex.Message}",
                        LogCategory.Transport);
                }

                _characterRegistry.RegisterCharacter(descriptor.WithParticipantId(string.Empty));
            }
        }

        private void OnTrackSubscribed(TrackInfo trackInfo)
        {
            if (trackInfo.Kind != TrackKind.Audio) return;

            string participantSid = trackInfo.ParticipantId;

            string characterId = ResolveCharacterIdFromParticipant(participantSid);

            _logger.Debug(
                $"[WebGLRoomController] Remote audio track subscribed: participant={participantSid}, character={characterId}",
                LogCategory.Transport);

            IRemoteParticipant participant = CurrentRoom?.GetParticipantBySid(participantSid);
            IRemoteAudioTrack audioTrack = null;

            if (participant != null)
            {
                foreach (IRemoteAudioTrack track in participant.AudioTracks)
                {
                    if (track.Sid == trackInfo.TrackSid)
                    {
                        audioTrack = track;
                        break;
                    }
                }
            }

            if (audioTrack != null)
            {
                _dispatcher.TryDispatch(() =>
                    OnRemoteAudioTrackSubscribed?.Invoke(audioTrack, participantSid, characterId));
            }
        }

        private void OnTrackUnsubscribed(TrackInfo trackInfo)
        {
            if (trackInfo.Kind != TrackKind.Audio) return;

            string participantSid = trackInfo.ParticipantId;
            string characterId = ResolveCharacterIdFromParticipant(participantSid);

            _logger.Debug($"[WebGLRoomController] Remote audio track unsubscribed: participant={participantSid}",
                LogCategory.Transport);

            _dispatcher.TryDispatch(() => OnRemoteAudioTrackUnsubscribed?.Invoke(participantSid, characterId));
        }

        private void OnDataReceived(DataPacket packet)
        {
            if (packet.Payload.Length == 0) return;

            try
            {
                var protocolPacket = new ProtocolPacket(
                    packet.Payload,
                    packet.ParticipantId,
                    packet.Topic,
                    packet.Kind == DataPacketKind.Reliable);

                // Fast-path: intercept LipSync packets before JSON deserialization in the gateway.
                if (RTVIHandler != null && RTVIHandler.TryHandleLipSyncServerMessage(in protocolPacket)) return;

                _protocolGateway.ProcessIncoming(protocolPacket);
            }
            catch (Exception ex)
            {
                _logger.Error($"[WebGLRoomController] Error processing data: {ex.Message}", LogCategory.Transport);
            }
        }

        #endregion

        #region Connection Methods

        /// <inheritdoc />
        public Task<bool> InitializeAsync(
            string connectionType,
            string llmProvider,
            string coreServerUrl,
            string characterId,
            string storedSessionId,
            bool enableSessionResume) =>
            InitializeAsync(connectionType, llmProvider, coreServerUrl, characterId, storedSessionId,
                enableSessionResume, null);

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
            _targetCharacterId = characterId;

            _logger.Info($"[WebGLRoomController] Initializing room connection for character: {characterId}",
                LogCategory.Transport);

            try
            {
                string apiKey = _config.ApiKey;
                _logger.Debug($"[WebGLRoomController] Resolving room details via '{coreServerUrl}'.",
                    LogCategory.Transport);
                RoomEmotionConfig emotionConfig = await ResolveEmotionConfigAsync(characterId, cancellationToken);

                string resolvedSessionId = !string.IsNullOrEmpty(joinOptions?.CharacterSessionId)
                    ? joinOptions.CharacterSessionId
                    : enableSessionResume
                        ? storedSessionId
                        : null;
                string endUserId = string.IsNullOrWhiteSpace(_config.EndUserId) ? null : _config.EndUserId;
                RoomConnectionRequest roomRequest = RoomConnectionRequestFactory.Create(
                    characterId,
                    connectionType,
                    llmProvider,
                    coreServerUrl,
                    resolvedSessionId,
                    endUserId,
                    _config.VideoTrackName,
                    emotionConfig,
                    joinOptions,
                    _config.LipSyncTransportOptions);
                string jsonBody = RoomConnectionRequestTransportSerializer.SerializeForTransport(
                    roomRequest,
                    new ConvaiRestClientOptions(apiKey));

                if (joinOptions != null && joinOptions.IsJoinRequest)
                {
                    _logger.Debug(
                        $"[WebGLRoomController] Room join mode: room={joinOptions.RoomName}, spawnAgent={joinOptions.SpawnAgent}",
                        LogCategory.Transport);
                }
                else
                    _logger.Debug("[WebGLRoomController] Room create mode (new room)", LogCategory.Transport);

                _logger.Debug("[WebGLRoomController] Requesting room details using coroutine-backed HTTP.",
                    LogCategory.Transport);

                RoomDetails roomDetails;
                try
                {
                    roomDetails = await RunCoroutineRequestAsync<RoomDetails>(
                        tcs => _coroutineRunner.StartCoroutine(FetchRoomDetailsCoroutine(coreServerUrl, apiKey,
                            jsonBody, tcs)),
                        cancellationToken);
                }
                catch (Exception restEx)
                {
                    _logger.Error(
                        $"[WebGLRoomController] REST API call failed with exception: {restEx.GetType().Name}: {restEx.Message}",
                        LogCategory.Transport);
                    throw;
                }

                if (roomDetails == null || string.IsNullOrEmpty(roomDetails.Token))
                {
                    _logger.Error("[WebGLRoomController] Failed to get room details", LogCategory.Transport);
                    OnRoomConnectionFailed?.Invoke();
                    return false;
                }

                Token = roomDetails.Token;
                RoomName = roomDetails.RoomName;
                RoomURL = roomDetails.RoomURL;
                SessionID = roomDetails.SessionId;
                CharacterSessionID = roomDetails.CharacterSessionId;
                HasRoomDetails = true;

                _logger.Debug($"[WebGLRoomController] Room details received - room={RoomName}, url={RoomURL}",
                    LogCategory.Transport);

                RTVIHandler = new RTVIHandler(
                    _protocolGateway,
                    _transport,
                    _characterRegistry,
                    _playerSession,
                    _dispatcher,
                    _logger,
                    _eventHub,
                    _sectionNameResolver,
                    _config.LipSyncTransportOptions);

                _logger.Debug($"[WebGLRoomController] Connecting realtime transport to {RoomURL}.",
                    LogCategory.Transport);
                bool connected = await _transport.ConnectAsync(RoomURL, Token, null, cancellationToken);

                if (!connected)
                {
                    _logger.Error("[WebGLRoomController] Transport connection failed", LogCategory.Transport);
                    _eventHub?.Publish(SessionError.Create(SessionErrorCodes.TransportLivekitError,
                        "Transport connection failed", null, true));
                    OnRoomConnectionFailed?.Invoke();
                    return false;
                }

                _logger.Info("[WebGLRoomController] Connection successful. Microphone will start after user gesture.",
                    LogCategory.Transport);
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error($"[WebGLRoomController] Connection error: {ex.Message}", LogCategory.Transport);
                _eventHub?.Publish(SessionError.Create(SessionErrorCodes.TransportLivekitError, ex.Message, null, true,
                    ex));
                OnRoomConnectionFailed?.Invoke();
                return false;
            }
        }

        /// <summary>
        ///     Coroutine-based HTTP call for WebGL compatibility.
        ///     Uses UnityWebRequest which properly yields on WebGL, unlike async/await with Task.Yield().
        /// </summary>
        private IEnumerator FetchRoomDetailsCoroutine(string url, string apiKey, string jsonBody,
            TaskCompletionSource<RoomDetails> tcs)
        {
            using (UnityWebRequest webRequest = CreateJsonPostRequest(url, "X-API-Key", apiKey, jsonBody))
            {
                _logger.Debug($"[WebGLRoomController] Sending room-details request to {url}.", LogCategory.Transport);
                yield return webRequest.SendWebRequest();

                if (webRequest.result != UnityWebRequest.Result.Success)
                {
                    string error = $"HTTP request failed: {webRequest.error} (Code: {webRequest.responseCode})";
                    _logger.Error($"[WebGLRoomController] {error}", LogCategory.Transport);

                    if (webRequest.downloadHandler != null && !string.IsNullOrEmpty(webRequest.downloadHandler.text))
                    {
                        _logger.Debug(
                            $"[WebGLRoomController] Room-details error body: {Truncate(webRequest.downloadHandler.text, 500)}",
                            LogCategory.Transport);
                    }

                    string errorCode = MapHttpResponseCodeToErrorCode(webRequest.responseCode);
                    _eventHub?.Publish(SessionError.Create(errorCode, error, null, webRequest.responseCode >= 500));

                    tcs.TrySetException(new Exception(error));
                    yield break;
                }

                string responseText = webRequest.downloadHandler.text;

                try
                {
                    var roomDetails = JsonConvert.DeserializeObject<RoomDetails>(responseText);
                    if (roomDetails == null)
                    {
                        tcs.TrySetException(new Exception("Failed to deserialize room details: result was null"));
                        yield break;
                    }

                    _logger.Debug($"[WebGLRoomController] Parsed room details for room '{roomDetails.RoomName}'.",
                        LogCategory.Transport);
                    tcs.TrySetResult(roomDetails);
                }
                catch (JsonException ex)
                {
                    _logger.Error($"[WebGLRoomController] Failed to parse room details: {ex.Message}",
                        LogCategory.Transport);
                    _logger.Debug($"[WebGLRoomController] Raw room-details response: {Truncate(responseText, 500)}",
                        LogCategory.Transport);
                    tcs.TrySetException(new Exception($"Failed to parse room details: {ex.Message}"));
                }
            }
        }

        private async Task<RoomEmotionConfig> ResolveEmotionConfigAsync(
            string characterId,
            CancellationToken cancellationToken)
        {
            // WebGL room connect already uses a coroutine bridge because async/await + Task.Yield
            // is unreliable for UnityWebRequest in browser builds. Keep character/get on the same path.
            try
            {
                var details = await RunCoroutineRequestAsync<CharacterDetails>(
                    tcs => _coroutineRunner.StartCoroutine(
                        FetchCharacterDetailsCoroutine(
                            CharacterService.ProductionCharacterGetUrl,
                            _config.ApiKey,
                            characterId,
                            tcs)),
                    cancellationToken);

                if (details != null && details.TryGetConnectEmotionConfig(out RoomEmotionConfig emotionConfig))
                {
                    _logger.Debug(
                        $"[WebGLRoomController] Emotion config enabled for character {characterId} with provider: {emotionConfig.Provider}",
                        LogCategory.Transport);
                    return emotionConfig;
                }

                _logger.Debug(
                    $"[WebGLRoomController] Emotion config disabled for character {characterId}",
                    LogCategory.Transport);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.Warning(
                    $"[WebGLRoomController] Failed to resolve emotion config for character {characterId}: {ex.Message}. Continuing without emotion_config.",
                    LogCategory.Transport);
            }

            return null;
        }

        private IEnumerator FetchCharacterDetailsCoroutine(
            string url,
            string apiKey,
            string characterId,
            TaskCompletionSource<CharacterDetails> tcs)
        {
            if (tcs.Task.IsCompleted) yield break;

            var requestBody = new { charID = characterId };

            string jsonBody = JsonConvert.SerializeObject(requestBody);

            using (UnityWebRequest webRequest = CreateJsonPostRequest(url, "CONVAI-API-KEY", apiKey, jsonBody))
            {
                yield return webRequest.SendWebRequest();

                if (tcs.Task.IsCompleted) yield break;

                if (webRequest.result != UnityWebRequest.Result.Success)
                {
                    string error = $"character/get failed: {webRequest.error} (Code: {webRequest.responseCode})";
                    tcs.TrySetException(new Exception(error));
                    yield break;
                }

                try
                {
                    JObject response = JObject.Parse(webRequest.downloadHandler.text);
                    JToken detailsToken = response["response"] ?? response;
                    var details = detailsToken.ToObject<CharacterDetails>();
                    if (details == null)
                    {
                        tcs.TrySetException(new Exception("character/get deserialized to null."));
                        yield break;
                    }

                    tcs.TrySetResult(details);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(new Exception($"Failed to parse character/get response: {ex.Message}", ex));
                }
            }
        }

        private async Task<T> RunCoroutineRequestAsync<T>(
            Action<TaskCompletionSource<T>> startRequest,
            CancellationToken cancellationToken)
        {
            var tcs =
                new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            startRequest(tcs);

            using (cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken))) return await tcs.Task;
        }

        private static UnityWebRequest CreateJsonPostRequest(string url, string apiKeyHeaderName, string apiKey,
            string jsonBody)
        {
            var webRequest = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
            webRequest.uploadHandler = new UploadHandlerRaw(bodyRaw);
            webRequest.downloadHandler = new DownloadHandlerBuffer();
            webRequest.SetRequestHeader("Content-Type", "application/json");
            webRequest.SetRequestHeader(apiKeyHeaderName, apiKey);
            webRequest.timeout = 30;
            return webRequest;
        }

        private static string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxLength) return value;

            return value.Substring(0, maxLength);
        }

        /// <inheritdoc />
        public void DisconnectFromRoom() => _ = DisconnectFromRoomAsync();

        /// <inheritdoc />
        public async Task DisconnectFromRoomAsync(CancellationToken cancellationToken = default)
        {
            _logger.Debug("[WebGLRoomController] Disconnecting from room...", LogCategory.Transport);

            try
            {
                await _transport.DisconnectAsync(DisconnectReason.ClientInitiated, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.Error($"[WebGLRoomController] Disconnect error: {ex.Message}", LogCategory.Transport);
            }

            IsConnectedToRoom = false;
            HasRoomDetails = false;
            CurrentRoom = null;
        }

        #endregion

        #region Audio Control

        /// <inheritdoc />
        public void SetMicMuted(bool mute)
        {
            IsMicMuted = mute;
            _transport.SetMicrophoneMuted(mute);
            _dispatcher.TryDispatch(() => OnMicMuteChanged?.Invoke(mute));
        }

        /// <inheritdoc />
        public void ToggleMicMute() => SetMicMuted(!IsMicMuted);

        /// <inheritdoc />
        public bool SetCharacterAudioMuted(string characterId, bool mute)
        {
            _logger.Warning("[WebGLRoomController] SetCharacterAudioMuted not fully implemented for WebGL",
                LogCategory.Transport);
            return false;
        }

        /// <inheritdoc />
        public bool MuteCharacter(string characterId) => SetCharacterAudioMuted(characterId, true);

        /// <inheritdoc />
        public bool UnmuteCharacter(string characterId) => SetCharacterAudioMuted(characterId, false);

        /// <inheritdoc />
        public bool IsCharacterAudioMuted(string characterId) => false;

        /// <inheritdoc />
        public void SetAudioSubscriptionPolicy(Func<string, bool> policy) =>
            _logger.Debug("[WebGLRoomController] Audio subscription policy set", LogCategory.Transport);

        /// <inheritdoc />
        public void ApplyRemoteAudioPreference(string characterId, bool enabled) => _logger.Debug(
            $"[WebGLRoomController] Remote audio preference: character={characterId}, enabled={enabled}",
            LogCategory.Transport);

        #endregion

        #region Session Management

        /// <inheritdoc />
        public string GetStoredSessionId(string characterId) => _config?.GetCharacterSessionId(characterId);

        /// <inheritdoc />
        public void ClearStoredSessionId(string characterId) => _config?.ClearCharacterSessionId(characterId);

        /// <inheritdoc />
        public void ClearAllStoredSessionIds() => _config?.ClearAllCharacterSessionIds();

        /// <inheritdoc />
        public string GetCurrentCharacterSessionId() => CharacterSessionID;

        #endregion

        #region Helper Methods

        private string ResolveCharacterIdFromParticipant(string participantSid)
        {
            if (string.IsNullOrEmpty(participantSid)) return null;

            if (_characterRegistry.TryGetCharacterByParticipantId(participantSid, out CharacterDescriptor descriptor))
                return descriptor.CharacterId;

            if (!string.IsNullOrEmpty(_targetCharacterId))
            {
                IReadOnlyList<CharacterDescriptor> all = _characterRegistry.GetAllCharacters();
                if (all != null && all.Count == 1) return _targetCharacterId;
            }

            return null;
        }

        private string TryResolveCharacterId(TransportParticipantInfo info)
        {
            if (!string.IsNullOrEmpty(info.Identity) && _characterRegistry.TryGetCharacter(info.Identity, out _))
                return info.Identity;

            string fromMetadata = TryExtractCharacterIdFromMetadata(info.Metadata);
            if (!string.IsNullOrEmpty(fromMetadata) && _characterRegistry.TryGetCharacter(fromMetadata, out _))
                return fromMetadata;

            if (!string.IsNullOrEmpty(_targetCharacterId))
            {
                IReadOnlyList<CharacterDescriptor> all = _characterRegistry.GetAllCharacters();
                if (all != null && all.Count == 1) return _targetCharacterId;
            }

            return null;
        }

        private static string TryExtractCharacterIdFromMetadata(string metadata)
        {
            if (string.IsNullOrWhiteSpace(metadata)) return null;

            try
            {
                JObject obj = JObject.Parse(metadata);
                JToken token = obj["characterId"] ??
                               obj["character_id"] ?? obj["convai_character_id"] ?? obj["convaiCharacterId"];
                return token?.Type == JTokenType.String ? token.Value<string>() : token?.ToString();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        ///     Maps HTTP response codes to hierarchical error codes.
        /// </summary>
        private static string MapHttpResponseCodeToErrorCode(long responseCode)
        {
            return responseCode switch
            {
                0 => SessionErrorCodes.ConnectionNetworkError, // No response (network failure)
                401 => SessionErrorCodes.ConnectionInvalidToken,
                403 => SessionErrorCodes.ConnectionAuthFailed,
                404 => SessionErrorCodes.ConnectionNotFound,
                429 => SessionErrorCodes.ConnectionRateLimited,
                503 => SessionErrorCodes.ConnectionServiceUnavailable,
                >= 500 and < 600 => SessionErrorCodes.ConnectionServerError,
                >= 400 and < 500 => SessionErrorCodes.ConnectionBadRequest,
                _ => SessionErrorCodes.ConnectionFailed
            };
        }

        #endregion
    }
}
