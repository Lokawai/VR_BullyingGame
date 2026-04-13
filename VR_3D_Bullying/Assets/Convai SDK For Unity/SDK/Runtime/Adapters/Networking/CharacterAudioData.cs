using System;
using UnityEngine;

namespace Convai.Runtime.Adapters.Networking
{
    /// <summary>
    ///     Data structure for tracking Character participant audio information in a room.
    ///     Uses platform-agnostic types for cross-platform compatibility.
    /// </summary>
    public class CharacterAudioData
    {
        /// <summary>
        ///     The participant ID associated with this Character.
        /// </summary>
        public string ParticipantId { get; set; } = string.Empty;

        /// <summary>
        ///     The audio stream for receiving audio from this Character.
        ///     Disposable resource that should be cleaned up when no longer needed.
        /// </summary>
        public IDisposable AudioStream { get; set; }

        /// <summary>
        ///     The Unity AudioSource for playing audio from this Character.
        /// </summary>
        public AudioSource AudioSource { get; set; }

        /// <summary>
        ///     Whether this Character's audio is currently muted.
        /// </summary>
        public bool IsMuted { get; set; }
    }
}
