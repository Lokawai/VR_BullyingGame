using System.Collections.Generic;
using Convai.Domain.Logging;
using Convai.Runtime.Behaviors;
using Convai.Runtime.Logging;
using Convai.Runtime.Presentation.Presenters;
using Convai.Runtime.Presentation.Services;
using Convai.Runtime.Presentation.Services.Utilities;
using Convai.Runtime.Services.CharacterLocator;
using Convai.Shared;
using Convai.Shared.DependencyInjection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Convai.Runtime.Presentation.Views.Transcript
{
    /// <summary>
    ///     Sample chat-style transcript UI that displays a scrollable message history.
    ///     This is a reference implementation showing how to implement ITranscriptUI.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Architecture:</b> This is a Samples layer reference implementation.
    ///         Implements ITranscriptUI as a "dumb" view - all aggregation logic is handled
    ///         by <see cref="Convai.Runtime.Presentation.Strategies.ChatPresentationStrategy" />.
    ///     </para>
    ///     <para>
    ///         <b>DI Pattern:</b> Implements IInjectable for dependency injection.
    ///         Services are injected by the ConvaiManager pipeline during scene initialization.
    ///     </para>
    /// </remarks>
    public class ChatTranscriptUI : MonoBehaviour, ITranscriptUI, IInjectable
    {
        [Header("UI References")] [SerializeField]
        private ScrollRect scrollRect;

        [SerializeField] private RectTransform chatContainer;
        [SerializeField] private GameObject characterMessagePrefab;
        [SerializeField] private GameObject playerMessagePrefab;
        [SerializeField] private TMP_InputField chatInputField;

        [Header("Fade Settings")] [SerializeField]
        private CanvasFader canvasFader;

        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private float fadeDuration = 0.5f;

        private readonly Dictionary<string, GameObject> _activeMessages = new();
        private IConvaiCharacterLocatorService _characterLocator;
        private bool _isActive;
        private bool _isInjected;
        private IPlayerInputService _playerInput;

        private void Awake()
        {
            if (canvasFader == null)
                canvasFader = GetComponentInChildren<CanvasFader>();
            if (canvasGroup == null)
                canvasGroup = GetComponentInChildren<CanvasGroup>();

            if (chatContainer == null)
            {
                ConvaiLogger.Warning("[ChatTranscriptUI] chatContainer is not assigned - messages will not display",
                    LogCategory.UI);
            }

            if (scrollRect == null)
            {
                ConvaiLogger.Warning("[ChatTranscriptUI] scrollRect is not assigned - auto-scroll will not work",
                    LogCategory.UI);
            }
        }

        private void Start()
        {
            if (!_isInjected)
            {
                ConvaiLogger.Warning(
                    "[ChatTranscriptUI] Dependencies not injected - ensure ConvaiManager is present in scene",
                    LogCategory.UI);
            }
        }

        private void OnEnable()
        {
            if (chatInputField != null) chatInputField.onSubmit.AddListener(OnChatInputSubmit);
        }

        private void OnDisable()
        {
            if (chatInputField != null) chatInputField.onSubmit.RemoveListener(OnChatInputSubmit);
        }

        #region IInjectable Implementation

        /// <inheritdoc />
        public void InjectServices(IServiceContainer container)
        {
            container.TryGet(out IConvaiCharacterLocatorService characterLocator);
            container.TryGet(out IPlayerInputService playerInput);
            _characterLocator = characterLocator;
            _playerInput = playerInput;
            _isInjected = true;

            if (_characterLocator == null)
            {
                ConvaiLogger.Warning(
                    "[ChatTranscriptUI] IConvaiCharacterLocatorService not available - character lookups will fail",
                    LogCategory.UI);
            }

            if (_playerInput == null)
            {
                ConvaiLogger.Warning("[ChatTranscriptUI] IPlayerInputService not available - text input will not work",
                    LogCategory.UI);
            }

            ConvaiLogger.Info("[ChatTranscriptUI] Dependencies injected via IInjectable", LogCategory.UI);
        }

        #endregion

        /// <summary>
        ///     Gets the unique identifier for this Chat UI instance.
        ///     Must match TranscriptUIMode.Chat for mode-based activation.
        /// </summary>
        public string Identifier => "Chat";

        /// <summary>
        ///     Gets whether this UI is currently active and visible.
        /// </summary>
        public bool IsActive => _isActive && gameObject.activeInHierarchy;

        private void OnChatInputSubmit(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;

            if (!_isInjected)
            {
                ConvaiLogger.Warning("[ChatTranscriptUI] Cannot send message - dependencies not injected",
                    LogCategory.UI);
                return;
            }

            if (_playerInput == null || !_playerInput.HasPlayer)
            {
                ConvaiLogger.Info("[ChatTranscriptUI] No player found", LogCategory.UI);
                return;
            }

            chatInputField.SetTextWithoutNotify(string.Empty);
            _playerInput.Player.SendTextMessage(text);

            chatInputField.ActivateInputField();
        }

        private void UpdateMessageBubble(GameObject messageObj, TranscriptViewModel viewModel)
        {
            var bubble = messageObj.GetComponent<ChatMessageBubble>();
            if (bubble != null)
            {
                bubble.SetSender(viewModel.DisplayName);
                bubble.SetMessage(viewModel.Text);

                if (viewModel.Speaker == TranscriptSpeaker.Character &&
                    _characterLocator != null &&
                    _characterLocator.TryGetCharacter(viewModel.SpeakerId, out IConvaiCharacterAgent character))
                    bubble.SetSenderColor(character.NameTagColor);
            }
            else
            {
                var textComponent = messageObj.GetComponentInChildren<TMP_Text>();
                if (textComponent != null) textComponent.text = $"{viewModel.DisplayName}: {viewModel.Text}";
            }
        }

        private void ScrollToBottom()
        {
            if (scrollRect == null) return;

            Canvas.ForceUpdateCanvases();

            if (chatContainer != null) LayoutRebuilder.ForceRebuildLayoutImmediate(chatContainer);

            scrollRect.verticalNormalizedPosition = 0;
        }

        #region ITranscriptUI Implementation

        /// <summary>
        ///     Displays or updates a transcript message.
        ///     Receives pre-aggregated view models from ChatPresentationStrategy.
        /// </summary>
        public void DisplayMessage(TranscriptViewModel viewModel)
        {
            ConvaiLogger.Info(
                $"[ChatTranscriptUI] DisplayMessage: Speaker={viewModel.Speaker}, SpeakerId={viewModel.SpeakerId}, IsFinal={viewModel.IsFinal}",
                LogCategory.UI);

            if (!_activeMessages.TryGetValue(viewModel.SpeakerId, out GameObject messageObj))
            {
                if (string.IsNullOrEmpty(viewModel.Text)) return;

                GameObject prefab = viewModel.Speaker == TranscriptSpeaker.Character
                    ? characterMessagePrefab
                    : playerMessagePrefab;
                messageObj = Instantiate(prefab, chatContainer);
                messageObj.SetActive(true);
                _activeMessages.Add(viewModel.SpeakerId, messageObj);

                var bubble = messageObj.GetComponent<ChatMessageBubble>();
                if (bubble != null)
                {
                    bubble.Identifier = viewModel.SpeakerId;
                    bubble.SetLocatorService(_characterLocator);
                }
            }

            UpdateMessageBubble(messageObj, viewModel);
            ScrollToBottom();
        }

        /// <summary>
        ///     Marks a message as completed.
        ///     Called by TranscriptUIController when ChatPresentationStrategy signals completion.
        /// </summary>
        public void CompleteMessage(string messageId)
        {
            ConvaiLogger.Info(
                $"[ChatTranscriptUI] CompleteMessage: Identifier={messageId}",
                LogCategory.UI);

            if (_activeMessages.TryGetValue(messageId, out GameObject messageObj))
            {
                var bubble = messageObj.GetComponent<ChatMessageBubble>();
                if (bubble != null) bubble.IsCompleted = true;

                _activeMessages.Remove(messageId);
            }
        }

        /// <summary>
        ///     Clears all displayed messages.
        /// </summary>
        public void ClearAll()
        {
            if (chatContainer != null)
            {
                foreach (Transform child in chatContainer.transform)
                {
                    if (child.gameObject != characterMessagePrefab &&
                        child.gameObject != playerMessagePrefab)
                        Destroy(child.gameObject);
                }
            }

            _activeMessages.Clear();
            ConvaiLogger.Info("[ChatTranscriptUI] Chat reset - all messages cleared", LogCategory.UI);
        }

        /// <summary>
        ///     Sets the active/visible state of this UI.
        /// </summary>
        public void SetActive(bool active)
        {
            _isActive = active;
            gameObject.SetActive(active);

            if (active && canvasFader != null && canvasGroup != null)
                canvasFader.StartFadeIn(canvasGroup, fadeDuration);
        }

        /// <summary>
        ///     Completes all active player messages.
        ///     Called when player turn ends - strategy handles aggregation, this just logs.
        /// </summary>
        public void CompletePlayerTurn() =>
            ConvaiLogger.Debug("[ChatTranscriptUI] Player turn completed", LogCategory.UI);

        #endregion
    }
}
