using System.Collections.Generic;
using Convai.Domain.EventSystem;
using Convai.Domain.Models.LipSync;
using Convai.Modules.LipSync.Profiles;
using Convai.Runtime.Room;
using Convai.Shared;
using Convai.Shared.DependencyInjection;
using Convai.Shared.Interfaces;
using Convai.Shared.Types;
using UnityEngine;
using ILogger = Convai.Domain.Logging.ILogger;

namespace Convai.Modules.LipSync
{
    /// <summary>
    ///     Thin MonoBehaviour shell that delegates runtime lip sync behavior to dedicated services.
    /// </summary>
    [AddComponentMenu("Convai/Lip Sync/Convai Lip Sync")]
    public sealed class ConvaiLipSyncComponent : MonoBehaviour, IInjectableLipSyncComponent, ILipSyncCapabilitySource,
        IInjectable
    {
        [Header("Core Setup")] [SerializeField]
        private string _lockedProfileId = LipSyncProfileId.ARKitValue;

        [SerializeField] private ConvaiLipSyncMapAsset _mapping;
        [SerializeField] private List<SkinnedMeshRenderer> _targetMeshes = new();

        [Header("Playback & Behavior")] [SerializeField] [Range(0f, 0.9f)]
        private float _smoothingFactor = 0.5f;

        [SerializeField] [Range(0.05f, 2f)] private float _fadeOutDuration = 0.2f;
        [SerializeField] [Range(-0.5f, 0.5f)] private float _timeOffset;

        [Header("Streaming & Latency")] [SerializeField]
        private LipSyncLatencyMode _latencyMode = LipSyncLatencyMode.Balanced;

        [SerializeField] [Range(1f, 10f)] private float _maxBufferedSeconds = 3f;
        [SerializeField] [Range(0.05f, 0.3f)] private float _minResumeHeadroomSeconds = 0.12f;
        private ILipSyncLifecycleOrchestrator _lifecycleOrchestrator;
        private IConvaiRoomAudioService _roomAudioService;

        private ILipSyncRuntimeController _runtimeController;
        private ILipSyncComponentServiceFactory _serviceFactory;

        /// <summary>Currently active profile id used by this component.</summary>
        public LipSyncProfileId ActiveProfile
        {
            get
            {
                if (_lifecycleOrchestrator != null && _lifecycleOrchestrator.ActiveProfile.IsValid)
                    return _lifecycleOrchestrator.ActiveProfile;

                return new LipSyncProfileId(LipSyncProfileId.Normalize(_lockedProfileId));
            }
        }

        /// <summary>Inspector-configured profile id normalized into a value object.</summary>
        public LipSyncProfileId LockedProfile => new(_lockedProfileId);

        /// <summary>Whether the playback engine is currently playing or starving.</summary>
        public bool IsPlaying => _lifecycleOrchestrator?.IsPlaying ?? false;

        /// <summary>Whether smooth fade-out is currently in progress.</summary>
        public bool IsFadingOut => _lifecycleOrchestrator?.IsFadingOut ?? false;

        /// <summary>Whether this component currently has remaining talking time.</summary>
        public bool IsTalking => GetTalkingTimeRemaining() > 0f;

        /// <summary>Current playback engine state.</summary>
        public PlaybackState EngineState => _lifecycleOrchestrator?.EngineState ?? PlaybackState.Idle;

        /// <summary>Configured target meshes receiving blendshape output.</summary>
        public IReadOnlyList<SkinnedMeshRenderer> TargetMeshes => _targetMeshes;

        /// <summary>Inspector-assigned mapping asset, if any.</summary>
        public ConvaiLipSyncMapAsset Mapping => _mapping;

        /// <summary>Effective runtime mapping after profile-default fallback resolution.</summary>
        public ConvaiLipSyncMapAsset EffectiveMapping
        {
            get
            {
                TryAssignRuntimeDefaultMappingIfMissing();
                EnsureServices();
                LipSyncComponentConfiguration configuration = BuildComponentConfiguration();
                ConvaiLipSyncMapAsset effectiveMapping =
                    _lifecycleOrchestrator.ResolveEffectiveMapping(ref configuration);
                ApplyComponentConfiguration(configuration);
                return effectiveMapping;
            }
        }

        private void Awake()
        {
            EnsureServices();
            LipSyncComponentConfiguration configuration = BuildComponentConfiguration();
            _lifecycleOrchestrator.HandleAwake(this, ref configuration);
            ApplyComponentConfiguration(configuration);
        }

        private void LateUpdate() => _lifecycleOrchestrator?.Tick(Time.deltaTime);

        private void OnEnable()
        {
            TryAssignRuntimeDefaultMappingIfMissing();
            EnsureServices();
            LipSyncComponentConfiguration configuration = BuildComponentConfiguration();
            _lifecycleOrchestrator.HandleEnable(this, Application.isPlaying, ref configuration);
            ApplyComponentConfiguration(configuration);
        }

        private void OnDisable() => _lifecycleOrchestrator?.HandleDisable();

        private void OnDestroy() => _lifecycleOrchestrator?.HandleDestroy();

        private void OnValidate()
        {
            EnsureServices();
            LipSyncComponentConfiguration configuration = BuildComponentConfiguration();
            _lifecycleOrchestrator.ApplyLatencyPreset(_latencyMode, ref configuration);
            _lifecycleOrchestrator.HandleValidate(this, Application.isPlaying, ref configuration);
            ApplyComponentConfiguration(configuration);
        }

        /// <inheritdoc />
        public void InjectServices(IServiceContainer container)
        {
            EnsureServices();
            if (container != null) container.TryGet(out _roomAudioService);

            _runtimeController.SetRoomAudioService(_roomAudioService);
        }

        /// <summary>
        ///     Injects required runtime services for event-driven lip sync playback.
        /// </summary>
        public void Inject(IEventHub eventHub, ILogger logger = null)
        {
            EnsureServices();
            LipSyncComponentConfiguration configuration = BuildComponentConfiguration();
            _lifecycleOrchestrator.HandleInject(
                this,
                ref configuration,
                eventHub,
                logger,
                enabled && isActiveAndEnabled);
            ApplyComponentConfiguration(configuration);
        }

        /// <summary>
        ///     Builds transport options for room negotiation from the active profile and effective source schema.
        /// </summary>
        public bool TryGetLipSyncTransportOptions(out LipSyncTransportOptions options)
        {
            TryAssignRuntimeDefaultMappingIfMissing();
            EnsureServices();
            LipSyncComponentConfiguration configuration = BuildComponentConfiguration();
            bool success = _lifecycleOrchestrator.TryGetTransportOptions(ref configuration, out options);
            ApplyComponentConfiguration(configuration);
            return success;
        }

        /// <summary>Returns estimated remaining talking time in seconds.</summary>
        public float GetTalkingTimeRemaining() => _lifecycleOrchestrator?.GetTalkingTimeRemaining() ?? 0f;

        /// <summary>Returns elapsed talking time in seconds from stream playback start.</summary>
        public float GetTalkingTimeElapsed() => _lifecycleOrchestrator?.GetTalkingTimeElapsed() ?? 0f;

        /// <summary>Total duration currently buffered in the runtime ring buffer.</summary>
        public float GetTotalBufferedDuration() => _lifecycleOrchestrator?.GetTotalBufferedDuration() ?? 0f;

        /// <summary>Total duration of all frames received since stream start.</summary>
        public float GetTotalStreamDuration() => _lifecycleOrchestrator?.GetTotalStreamDuration() ?? 0f;

        /// <summary>Current headroom between buffer end and playback position. Negative means starving.</summary>
        public float GetHeadroom() => _lifecycleOrchestrator?.GetHeadroom() ?? 0f;

        /// <summary>
        ///     Returns a zero-allocation snapshot of the current blendshape output values.
        /// </summary>
        public BlendshapeSnapshot GetBlendshapeSnapshot() => _lifecycleOrchestrator?.GetBlendshapeSnapshot() ?? default;

        private void EnsureServices()
        {
            if (_lifecycleOrchestrator != null) return;

            _serviceFactory ??= new LipSyncComponentServiceFactory();
            LipSyncComponentServices services = _serviceFactory.Create();
            _runtimeController = services.RuntimeController;
            _lifecycleOrchestrator = services.LifecycleOrchestrator;
        }

        private void TryAssignRuntimeDefaultMappingIfMissing()
        {
            if (!Application.isPlaying || _mapping != null) return;

            _lockedProfileId = LipSyncProfileId.Normalize(_lockedProfileId);
            LipSyncProfileId profileId = new(_lockedProfileId);
            if (!profileId.IsValid || !LipSyncProfileCatalog.TryGetProfile(profileId, out _)) return;

            ConvaiLipSyncMapAsset profileDefault = LipSyncDefaultMappingResolver.ResolveProfileDefault(profileId);
            if (profileDefault != null) _mapping = profileDefault;
        }

        private LipSyncComponentConfiguration BuildComponentConfiguration()
        {
            return new LipSyncComponentConfiguration
            {
                LockedProfileId = _lockedProfileId,
                Mapping = _mapping,
                TargetMeshes = _targetMeshes,
                SmoothingFactor = _smoothingFactor,
                FadeOutDuration = _fadeOutDuration,
                TimeOffsetSeconds = _timeOffset,
                MaxBufferedSeconds = _maxBufferedSeconds,
                MinResumeHeadroomSeconds = _minResumeHeadroomSeconds
            };
        }

        private void ApplyComponentConfiguration(LipSyncComponentConfiguration configuration)
        {
            _lockedProfileId = configuration.LockedProfileId;
            _maxBufferedSeconds = configuration.MaxBufferedSeconds;
            _minResumeHeadroomSeconds = configuration.MinResumeHeadroomSeconds;
        }
    }
}
