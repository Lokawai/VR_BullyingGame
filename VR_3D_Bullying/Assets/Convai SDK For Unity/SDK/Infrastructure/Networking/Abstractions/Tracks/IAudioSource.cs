using System;
using UnityEngine;

namespace Convai.Infrastructure.Networking
{
    /// <summary>
    ///     Base interface for audio sources that can be published.
    /// </summary>
    public interface IAudioSource : IDisposable
    {
        /// <summary>
        ///     Gets the name/identifier of this audio source.
        /// </summary>
        public string Name { get; }

        /// <summary>
        ///     Gets whether this source is currently capturing audio.
        /// </summary>
        public bool IsCapturing { get; }

        /// <summary>
        ///     Starts capturing audio from this source.
        /// </summary>
        public void StartCapture();

        /// <summary>
        ///     Stops capturing audio from this source.
        /// </summary>
        public void StopCapture();
    }

    /// <summary>
    ///     Interface for microphone audio sources.
    /// </summary>
    public interface IMicrophoneSource : IAudioSource
    {
        /// <summary>
        ///     Gets the microphone device name.
        /// </summary>
        public string DeviceName { get; }

        /// <summary>
        ///     Gets the device index.
        /// </summary>
        public int DeviceIndex { get; }

        /// <summary>
        ///     Gets or sets the mute state.
        /// </summary>
        public bool IsMuted { get; set; }
    }

    /// <summary>
    ///     Interface for audio streams that provide audio data for playback.
    /// </summary>
    public interface IAudioStream : IDisposable
    {
        /// <summary>
        ///     Gets whether this stream is currently active.
        /// </summary>
        public bool IsActive { get; }

        /// <summary>
        ///     Gets the sample rate of the audio stream.
        /// </summary>
        public int SampleRate { get; }

        /// <summary>
        ///     Gets the number of channels.
        /// </summary>
        public int Channels { get; }

        /// <summary>
        ///     Attaches this stream to a Unity AudioSource for playback.
        /// </summary>
        /// <param name="target">The target AudioSource.</param>
        public void AttachToAudioSource(AudioSource target);

        /// <summary>
        ///     Detaches from any attached AudioSource.
        /// </summary>
        public void Detach();

        /// <summary>
        ///     Raised when audio data is received.
        /// </summary>
        public event Action<float[], int, int> AudioDataReceived;
    }

    /// <summary>
    ///     Options for publishing audio tracks.
    /// </summary>
    public struct AudioPublishOptions
    {
        /// <summary>
        ///     The source type for the audio track.
        /// </summary>
        public AudioTrackSource Source { get; set; }

        /// <summary>
        ///     Whether to enable discontinuous transmission (silence suppression).
        /// </summary>
        public bool Dtx { get; set; }

        /// <summary>
        ///     The bitrate for the audio track in bps.
        /// </summary>
        public int Bitrate { get; set; }

        /// <summary>
        ///     Creates default audio publish options for microphone.
        /// </summary>
        public static AudioPublishOptions DefaultMicrophone => new()
        {
            Source = AudioTrackSource.Microphone, Dtx = true, Bitrate = 32000
        };
    }

    /// <summary>
    ///     Types of audio track sources.
    /// </summary>
    public enum AudioTrackSource
    {
        /// <summary>Microphone input.</summary>
        Microphone,

        /// <summary>Screen share audio.</summary>
        ScreenShareAudio,

        /// <summary>Unknown or other source.</summary>
        Unknown
    }
}
