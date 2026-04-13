using System;
using Convai.Domain.Logging;
using Convai.Runtime.Behaviors;
using Convai.Runtime.Logging;
using Convai.Runtime.Presentation.Services;
using Convai.Shared;
using Convai.Shared.Abstractions;
using Convai.Shared.DependencyInjection;
using UnityEngine;

namespace Convai.Runtime.Components
{
    /// <summary>
    ///     Main player component that implements IConvaiPlayerAgent.
    ///     Provides player identity for the Convai conversation system.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This component is the player-side equivalent of <see cref="ConvaiCharacter" />.
    ///         It provides player identity (name, speakerId, color) and text messaging capability.
    ///         Microphone input is managed separately by <see cref="Convai.Runtime.Adapters.Networking.ConvaiRoomManager" />.
    ///         Player behavior can be extended using <see cref="ConvaiPlayerBehaviorBase" />.
    ///     </para>
    ///     <para>
    ///         <b>Important - SpeakerId vs Server Speaker ID:</b>
    ///     </para>
    ///     <para>
    ///         The <c>SpeakerId</c> property on this component is a <b>local display identifier</b> used for
    ///         transcript UI attribution. It is NOT the same as the server-generated <c>speaker_id</c> used
    ///         for Long-Term Memory (LTM) and interaction tracking.
    ///     </para>
    ///     <para>
    ///         The server-generated speaker ID is available via
    ///         <see cref="Convai.Infrastructure.Networking.IConvaiRoomController.ResolvedSpeakerId" />
    ///         after connection is established.
    ///     </para>
    /// </remarks>
    [AddComponentMenu("Convai/Convai Player")]
    public class ConvaiPlayer : MonoBehaviour, IConvaiPlayerAgent, IInjectable
    {
        #region Events

        /// <summary>Raised when a text message is sent.</summary>
        public event Action<string> OnTextMessageSent;

        #endregion

        #region IInjectable

        /// <inheritdoc />
        public void InjectServices(IServiceContainer container)
        {
            if (container.TryGet(out IPlayerInputService playerInputService)) playerInputService.SetPlayer(this);

            if (container.TryGet(out IConvaiRuntimeSettingsService runtimeSettings))
                SetRuntimeDisplayName(runtimeSettings.Current.PlayerDisplayName);
        }

        #endregion

        #region Serialized Fields

        [Header("Player Configuration")]
        [SerializeField]
        [Tooltip("Display name for the player, used in transcripts and debug logs.")]
        private string _playerName = "Player";

        [SerializeField]
        [Tooltip("Optional local speaker identifier for transcript UI attribution. " +
                 "If empty, PlayerName is used. This is NOT the server-generated speaker ID used for Long-Term Memory.")]
        private string _speakerId = "";

        [SerializeField] private Color _nameTagColor = Color.green;

        private string _runtimeDisplayName = string.Empty;
        private bool _hasRuntimeDisplayName;

        #endregion

        #region IConvaiPlayerAgent Implementation

        /// <summary>Display name for the player.</summary>
        public string PlayerName => _hasRuntimeDisplayName ? _runtimeDisplayName : _playerName;

        /// <summary>
        ///     Local speaker identifier for transcript UI attribution.
        /// </summary>
        /// <remarks>
        ///     This is a local display identifier, NOT the server-generated speaker_id used for LTM.
        ///     For the server-assigned speaker ID, see
        ///     <see cref="Convai.Infrastructure.Networking.ConvaiRoomController.ResolvedSpeakerId" />.
        /// </remarks>
        public string SpeakerId => string.IsNullOrEmpty(_speakerId) ? PlayerName : _speakerId;

        /// <summary>Name tag color for transcript display.</summary>
        public Color NameTagColor => _nameTagColor;

        /// <summary>
        ///     Sends a text message to the active Character.
        /// </summary>
        /// <param name="message">The text message to send.</param>
        public void SendTextMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                ConvaiLogger.Warning("[ConvaiPlayer] SendTextMessage called with null or empty message; ignoring.",
                    LogCategory.SDK);
                return;
            }

            if (OnTextMessageSent == null)
            {
                ConvaiLogger.Warning(
                    "[ConvaiPlayer] SendTextMessage has no subscribers (is ConvaiRoomManager active?); message dropped.",
                    LogCategory.SDK);
                return;
            }

            OnTextMessageSent.Invoke(message);
        }

        #endregion

        #region Public Methods

        /// <summary>
        ///     Configures the player identity.
        /// </summary>
        /// <param name="playerName">Display name for the player.</param>
        /// <param name="speakerId">Optional speaker ID (defaults to playerName).</param>
        public void Configure(string playerName, string speakerId = null)
        {
            _playerName = string.IsNullOrWhiteSpace(playerName) ? "Player" : playerName.Trim();
            _speakerId = string.IsNullOrWhiteSpace(speakerId) ? _playerName : speakerId;
            _runtimeDisplayName = string.Empty;
            _hasRuntimeDisplayName = false;
        }

        /// <summary>
        ///     Updates the effective runtime display name used by transcripts and player identity.
        /// </summary>
        /// <param name="displayName">Display name override. Null/empty clears the runtime override.</param>
        public void SetRuntimeDisplayName(string displayName)
        {
            if (string.IsNullOrWhiteSpace(displayName))
            {
                _runtimeDisplayName = string.Empty;
                _hasRuntimeDisplayName = false;
                return;
            }

            _runtimeDisplayName = displayName.Trim();
            _hasRuntimeDisplayName = true;
        }

        #endregion
    }
}
