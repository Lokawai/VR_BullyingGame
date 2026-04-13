using Convai.Domain.Logging;
using Convai.Runtime.Logging;
using Convai.Runtime.Room;
using Convai.Shared;
using Convai.Shared.DependencyInjection;
using UnityEngine;

namespace Convai.Runtime.Components
{
    /// <summary>
    ///     Optional companion component for audio output. Auto-discovers ConvaiCharacter.
    ///     Handles AudioSource configuration and playback control for character speech.
    /// </summary>
    /// <remarks>
    ///     This component follows the composition pattern:
    ///     - Must be attached to the same GameObject as ConvaiCharacter
    ///     - Auto-discovers and subscribes to ConvaiCharacter events
    ///     - Manages AudioSource volume and mute state
    ///     - Registers with room audio service for track routing
    /// </remarks>
    [AddComponentMenu("Convai/Convai Audio Output")]
    [RequireComponent(typeof(ConvaiCharacter))]
    [RequireComponent(typeof(AudioSource))]
    public class ConvaiAudioOutput : MonoBehaviour, IInjectable
    {
        #region Serialized Fields

        [Header("Audio Settings")] [SerializeField] [Range(0f, 1f)]
        private float _volume = 1.0f;

        [SerializeField] private bool _isMuted;

        [Header("3D Audio")] [SerializeField] private bool _use3DAudio = true;

        [SerializeField] private float _minDistance = 1f;
        [SerializeField] private float _maxDistance = 50f;

        #endregion

        #region Components

        private ConvaiCharacter _character;
        private IConvaiRoomAudioService _roomAudioService;

        #endregion

        #region Public Properties

        /// <summary>Audio output volume (0-1).</summary>
        public float Volume
        {
            get => _volume;
            set
            {
                _volume = Mathf.Clamp01(value);
                if (AudioSource != null) AudioSource.volume = _isMuted ? 0f : _volume;
            }
        }

        /// <summary>Whether audio output is muted.</summary>
        public bool IsMuted
        {
            get => _isMuted;
            set
            {
                _isMuted = value;
                if (AudioSource != null) AudioSource.volume = _isMuted ? 0f : _volume;
            }
        }

        /// <summary>The AudioSource used for playback.</summary>
        public AudioSource AudioSource { get; private set; }

        #endregion

        #region Dependency Injection

        /// <inheritdoc />
        public void InjectServices(IServiceContainer container)
        {
            container.TryGet(out IConvaiRoomAudioService audioService);
            Inject(audioService);
        }

        /// <summary>
        ///     Injects the room audio service for track routing.
        ///     Called by the ConvaiManager pipeline.
        /// </summary>
        public void Inject(IConvaiRoomAudioService roomAudioService) => _roomAudioService = roomAudioService;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            _character = GetComponent<ConvaiCharacter>();
            AudioSource = GetComponent<AudioSource>();
            ConfigureAudioSource();
        }

        private void OnEnable()
        {
            if (_character == null)
            {
                ConvaiLogger.Error($"[ConvaiAudioOutput] ConvaiCharacter component not found on {gameObject.name}",
                    LogCategory.Audio);
                enabled = false;
                return;
            }

            SubscribeToEvents();
        }

        private void OnDisable() => UnsubscribeFromEvents();

        private void OnValidate()
        {
            if (AudioSource != null) ConfigureAudioSource();
        }

        #endregion

        #region Private Helpers

        private void ConfigureAudioSource()
        {
            if (AudioSource == null) return;

            AudioSource.playOnAwake = false;
            AudioSource.volume = _isMuted ? 0f : _volume;
            AudioSource.spatialBlend = _use3DAudio ? 1f : 0f;
            AudioSource.minDistance = _minDistance;
            AudioSource.maxDistance = _maxDistance;
            AudioSource.rolloffMode = AudioRolloffMode.Logarithmic;
        }

        private void SubscribeToEvents()
        {
            if (_character == null) return;

            _character.OnSpeechStarted += OnCharacterSpeechStarted;
            _character.OnSpeechStopped += OnCharacterSpeechStopped;
        }

        private void UnsubscribeFromEvents()
        {
            if (_character == null) return;

            _character.OnSpeechStarted -= OnCharacterSpeechStarted;
            _character.OnSpeechStopped -= OnCharacterSpeechStopped;
        }

        private void OnCharacterSpeechStarted()
        {
            // AudioSource is already playing via LiveKit's AudioStream
            // This hook is for future extensions (visual indicators, etc.)
        }

        private void OnCharacterSpeechStopped()
        {
            // AudioSource playback stops automatically when track ends
            // This hook is for future extensions (cleanup, etc.)
        }

        #endregion
    }
}
