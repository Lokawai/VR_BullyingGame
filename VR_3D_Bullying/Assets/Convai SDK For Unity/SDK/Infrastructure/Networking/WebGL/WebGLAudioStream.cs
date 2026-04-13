using System;
using Convai.Domain.Logging;
using Convai.Runtime.Logging;
using LiveKit;
using UnityEngine;

namespace Convai.Infrastructure.Networking.WebGL
{
    /// <summary>
    ///     WebGL implementation of <see cref="IAudioStream" /> for browser-based audio playback.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         On WebGL, audio is played through browser HTML audio elements rather than Unity's audio system.
    ///         This implementation provides the interface but actual audio is handled by the browser.
    ///     </para>
    /// </remarks>
    internal sealed class WebGLAudioStream : IAudioStream
    {
        #region Constructor

        /// <summary>
        ///     Creates a new WebGL audio stream.
        /// </summary>
        /// <param name="track">The remote track to stream audio from.</param>
        public WebGLAudioStream(RemoteTrack track)
        {
            _track = track ?? throw new ArgumentNullException(nameof(track));
        }

        #endregion

        #region IAudioStream Events

        /// <inheritdoc />
        /// <remarks>
        ///     On WebGL, raw audio data is not accessible from JavaScript audio elements.
        ///     This event will not fire. Use the native platform for audio data access.
        /// </remarks>
#pragma warning disable CS0067 // Event is never used - required by interface but WebGL doesn't provide raw audio data
        public event Action<float[], int, int> AudioDataReceived;
#pragma warning restore CS0067

        #endregion

        #region IDisposable

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            Detach();
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Private Fields

        private readonly RemoteTrack _track;
        private bool _isActive;
        private bool _disposed;

        // Default audio parameters (browser handles actual values)
        private const int DefaultSampleRate = 48000;
        private const int DefaultChannels = 2;

        #endregion

        #region IAudioStream Properties

        /// <inheritdoc />
        public bool IsActive => _isActive && !_disposed;

        /// <inheritdoc />
        /// <remarks>
        ///     On WebGL, the actual sample rate is determined by the browser's audio context.
        ///     This returns a default value.
        /// </remarks>
        public int SampleRate => DefaultSampleRate;

        /// <inheritdoc />
        /// <remarks>
        ///     On WebGL, the actual channel count is determined by the browser.
        ///     This returns a default stereo value.
        /// </remarks>
        public int Channels => DefaultChannels;

        #endregion

        #region IAudioStream Methods

        /// <inheritdoc />
        /// <remarks>
        ///     On WebGL, audio cannot be attached to a Unity AudioSource. Instead, audio plays
        ///     through browser HTML audio elements. This method will activate browser audio playback.
        /// </remarks>
        public void AttachToAudioSource(AudioSource target)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(WebGLAudioStream));

            if (_isActive)
            {
                ConvaiLogger.Warning("[WebGLAudioStream] Stream is already active.", LogCategory.Audio);
                return;
            }

            // Attach to browser audio element
            _track.Attach();
            _isActive = true;

            ConvaiLogger.Info("[WebGLAudioStream] Audio stream attached to browser audio. " +
                              "Unity AudioSource parameter is ignored on WebGL.",
                LogCategory.Audio);
        }

        /// <inheritdoc />
        public void Detach()
        {
            if (!_isActive || _disposed) return;

            _track.Detach();
            _isActive = false;
        }

        #endregion
    }
}
