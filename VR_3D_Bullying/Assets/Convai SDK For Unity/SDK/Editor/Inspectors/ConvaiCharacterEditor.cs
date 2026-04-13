using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Convai.Domain.Logging;
using Convai.Editor.ConfigurationWindow;
using Convai.Editor.Utilities;
using Convai.RestAPI;
using Convai.Runtime;
using Convai.Runtime.Components;
using Convai.Runtime.Logging;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using SessionState = Convai.Domain.DomainEvents.Session.SessionState;

namespace Convai.Editor.Inspectors
{
    /// <summary>
    ///     Custom inspector for ConvaiCharacter using UI Toolkit.
    ///     Provides status indicators, quick actions, styled UI, validation, and runtime debug info.
    /// </summary>
    [CustomEditor(typeof(ConvaiCharacter))]
    public class ConvaiCharacterEditor : UnityEditor.Editor
    {
        private const int MaxEventHistoryCount = 50;

        private static readonly Regex GuidRegex = new(
            @"^[a-fA-F0-9]{8}-[a-fA-F0-9]{4}-[a-fA-F0-9]{4}-[a-fA-F0-9]{4}-[a-fA-F0-9]{12}$",
            RegexOptions.Compiled);

        private readonly List<EventHistoryItem> _eventHistory = new();
        private Button _addAudioOutputButton;
        private VisualElement _apiKeyIndicator;

        private VisualElement _apiKeySection;
        private Label _apiKeyStatusText;
        private VisualElement _audioSuggestionSection;
        private Label _audioTrackStatusLabel;
        private SerializedProperty _autoConnectProp;
        private Toggle _autoConnectToggle;
        private ConvaiCharacter _character;

        private TextField _characterIdField;

        private SerializedProperty _characterIdProp;
        private Label _characterIdValidationMessage;
        private TextField _characterNameField;
        private SerializedProperty _characterNameProp;
        private SerializedProperty _characterReadyTimeoutProp;
        private Button _clearEventsButton;
        private Label _connectionQualityLabel;
        private string _currentEventFilter = "All";
        private Toggle _enableAudioToggle;
        private SerializedProperty _enableRemoteAudioProp;
        private SerializedProperty _enableSessionResumeProp;
        private Toggle _enableSessionResumeToggle;
        private Label _eventEmptyLabel;
        private DropdownField _eventFilterDropdown;

        private VisualElement _eventHistorySection;
        private ScrollView _eventListScroll;
        private VisualElement _fetchLoadingIndicator;
        private Button _fetchNameButton;
        private Button _fixSetupButton;

        private VisualElement _headerLogo;
        private Button _helpButton;
        private Label _injectionStatusLabel;
        private bool _isFetchingName;
        private Label _latencyLabel;
        private Label _micStatusLabel;
        private VisualElement _missingComponentsList;
        private ColorField _nameTagColorField;
        private SerializedProperty _nameTagColorProp;
        private Button _openSettingsButton;
        private Button _openWizardButton;
        private Label _packetLossLabel;
        private Label _participantIdLabel;
        private FloatField _readyTimeoutField;
        private Label _roomNameLabel;

        private VisualElement _root;
        private VisualElement _runtimeInfoSection;

        private VisualElement _sceneSetupSection;

        private Label _sessionIdLabel;
        private Label _speechStateLabel;
        private VisualElement _statusIndicator;
        private Label _statusText;
        private VisualElement _warningSection;

        /// <inheritdoc />
        public override VisualElement CreateInspectorGUI()
        {
            _character = (ConvaiCharacter)target;
            _root = new VisualElement();

            VisualTreeAsset visualTree = ConvaiEditorSettings.Instance.ConvaiCharacterInspectorUxml;
            StyleSheet styleSheet = ConvaiEditorSettings.Instance.ConvaiEditorStylesSheet;

            if (visualTree == null)
            {
                ConvaiLogger.Warning("[ConvaiCharacterEditor] UXML not found. Using default inspector.",
                    LogCategory.Editor);
                var defaultInspector = new IMGUIContainer(OnInspectorGUI);
                _root.Add(defaultInspector);
                return _root;
            }

            visualTree.CloneTree(_root);

            if (styleSheet != null) _root.styleSheets.Add(styleSheet);

            _characterIdProp = serializedObject.FindProperty("_characterId");
            _characterNameProp = serializedObject.FindProperty("_characterName");
            _nameTagColorProp = serializedObject.FindProperty("_nameTagColor");
            _autoConnectProp = serializedObject.FindProperty("_autoConnect");
            _enableRemoteAudioProp = serializedObject.FindProperty("_enableRemoteAudio");
            _enableSessionResumeProp = serializedObject.FindProperty("_enableSessionResume");
            _characterReadyTimeoutProp = serializedObject.FindProperty("_characterReadyTimeoutSeconds");

            BindUIElements();
            SetupEventHandlers();
            UpdateUI();

            _root.schedule.Execute(UpdateRuntimeStatus).Every(500);

            return _root;
        }

        private void BindUIElements()
        {
            _headerLogo = _root.Q<VisualElement>("header-logo");
            _statusIndicator = _root.Q<VisualElement>("status-indicator");
            _statusText = _root.Q<Label>("status-text");
            _helpButton = _root.Q<Button>("help-button");

            if (_headerLogo != null)
            {
                Texture2D iconTexture = ConvaiEditorSettings.Instance.ConvaiIconTexture;
                if (iconTexture != null) _headerLogo.style.backgroundImage = new StyleBackground(iconTexture);
            }

            _apiKeySection = _root.Q<VisualElement>("api-key-section");
            _apiKeyIndicator = _root.Q<VisualElement>("api-key-indicator");
            _apiKeyStatusText = _root.Q<Label>("api-key-status-text");
            _openSettingsButton = _root.Q<Button>("open-settings-button");
            _openWizardButton = _root.Q<Button>("open-wizard-button");

            _sceneSetupSection = _root.Q<VisualElement>("scene-setup-section");
            _missingComponentsList = _root.Q<VisualElement>("missing-components-list");
            _fixSetupButton = _root.Q<Button>("fix-setup-button");

            _characterIdField = _root.Q<TextField>("character-id-field");
            _characterNameField = _root.Q<TextField>("character-name-field");
            _nameTagColorField = _root.Q<ColorField>("name-tag-color-field");
            _autoConnectToggle = _root.Q<Toggle>("auto-connect-toggle");
            _enableAudioToggle = _root.Q<Toggle>("enable-audio-toggle");
            _enableSessionResumeToggle = _root.Q<Toggle>("enable-session-resume-toggle");
            _readyTimeoutField = _root.Q<FloatField>("ready-timeout-field");
            _fetchNameButton = _root.Q<Button>("fetch-name-button");
            _fetchLoadingIndicator = _root.Q<VisualElement>("fetch-loading-indicator");

            _warningSection = _root.Q<VisualElement>("warning-section");
            _characterIdValidationMessage = _root.Q<Label>("character-id-validation-message");

            _audioSuggestionSection = _root.Q<VisualElement>("audio-suggestion-section");
            _addAudioOutputButton = _root.Q<Button>("add-audio-output-button");

            _runtimeInfoSection = _root.Q<VisualElement>("runtime-info-section");
            _injectionStatusLabel = _root.Q<Label>("injection-status");
            _sessionIdLabel = _root.Q<Label>("session-id-label");
            _participantIdLabel = _root.Q<Label>("participant-id-label");
            _roomNameLabel = _root.Q<Label>("room-name-label");
            _audioTrackStatusLabel = _root.Q<Label>("audio-track-status");
            _micStatusLabel = _root.Q<Label>("mic-status");
            _speechStateLabel = _root.Q<Label>("speech-state");
            _connectionQualityLabel = _root.Q<Label>("connection-quality");
            _latencyLabel = _root.Q<Label>("latency-label");
            _packetLossLabel = _root.Q<Label>("packet-loss-label");

            _eventHistorySection = _root.Q<VisualElement>("event-history-section");
            _eventListScroll = _root.Q<ScrollView>("event-list-scroll");
            _eventFilterDropdown = _root.Q<DropdownField>("event-filter-dropdown");
            _clearEventsButton = _root.Q<Button>("clear-events-button");
            _eventEmptyLabel = _root.Q<Label>("event-empty-label");

            if (_characterIdField != null)
            {
                _characterIdField.BindProperty(_characterIdProp);
                _characterIdField.RegisterValueChangedCallback(_ => UpdateCharacterIdValidation());
                _characterIdField.RegisterCallback<BlurEvent>(_ => TrimCharacterIdWhitespace());
            }

            if (_characterNameField != null)
                _characterNameField.BindProperty(_characterNameProp);

            if (_nameTagColorField != null)
                _nameTagColorField.BindProperty(_nameTagColorProp);

            if (_autoConnectToggle != null)
                _autoConnectToggle.BindProperty(_autoConnectProp);

            if (_enableAudioToggle != null && _enableRemoteAudioProp != null)
            {
                _enableAudioToggle.BindProperty(_enableRemoteAudioProp);
                _enableAudioToggle.RegisterValueChangedCallback(_ => UpdateAudioSuggestion());
            }

            if (_enableSessionResumeToggle != null && _enableSessionResumeProp != null)
                _enableSessionResumeToggle.BindProperty(_enableSessionResumeProp);

            if (_readyTimeoutField != null && _characterReadyTimeoutProp != null)
                _readyTimeoutField.BindProperty(_characterReadyTimeoutProp);
        }

        private void SetupEventHandlers()
        {
            if (_helpButton != null)
                _helpButton.clicked += () => UnityEngine.Application.OpenURL(ConvaiEditorLinks.DocsUnityQuickstartUrl);

            if (_openSettingsButton != null)
                _openSettingsButton.clicked += () => SettingsService.OpenProjectSettings("Project/Convai SDK");

            if (_openWizardButton != null)
                _openWizardButton.clicked += () => ConvaiConfigurationWindowEditor.OpenAccountWindow();

            if (_fixSetupButton != null)
            {
                _fixSetupButton.clicked += () =>
                {
                    ConvaiSetupWizard.SetupRequiredComponents();
                    UpdateSceneSetupValidation();
                };
            }

            if (_addAudioOutputButton != null)
            {
                _addAudioOutputButton.clicked += () =>
                {
                    if (_character != null && _character.GetComponent<ConvaiAudioOutput>() == null)
                    {
                        Undo.AddComponent<ConvaiAudioOutput>(_character.gameObject);
                        UpdateAudioSuggestion();
                    }
                };
            }

            if (_fetchNameButton != null) _fetchNameButton.clicked += FetchCharacterNameFromAPI;

            var copyButton = _root.Q<Button>("copy-id-button");
            if (copyButton != null)
            {
                copyButton.clicked += () =>
                {
                    GUIUtility.systemCopyBuffer = _character.CharacterId ?? "";
                    ConvaiLogger.Debug($"[Convai] Character ID copied: {_character.CharacterId}", LogCategory.Editor);
                };
            }

            var dashboardButton = _root.Q<Button>("open-dashboard-button");
            if (dashboardButton != null)
            {
                dashboardButton.clicked += () =>
                {
                    string url = string.IsNullOrEmpty(_character.CharacterId)
                        ? ConvaiEditorLinks.DashboardHomeUrl
                        : $"{ConvaiEditorLinks.CharacterDashboardBaseUrl}?id={_character.CharacterId}";
                    UnityEngine.Application.OpenURL(url);
                };
            }

            var dashboardHomeButton = _root.Q<Button>("open-dashboard-home-button");
            if (dashboardHomeButton != null)
            {
                dashboardHomeButton.clicked +=
                    () => UnityEngine.Application.OpenURL(ConvaiEditorLinks.DashboardHomeUrl);
            }

            var testButton = _root.Q<Button>("test-connection-button");
            if (testButton != null)
            {
                testButton.clicked += () =>
                {
                    if (UnityEngine.Application.isPlaying)
                    {
                        ConvaiLogger.Debug(
                            $"[Convai] Character '{_character.CharacterName}' - Injected: {_character.IsInjected}, SessionState: {_character.SessionState}, IsCharacterReady: {_character.IsCharacterReady}",
                            LogCategory.Editor);
                    }
                    else
                        ConvaiLogger.Debug("[Convai] Enter Play Mode to test connection.", LogCategory.Editor);
                };
            }

            if (_eventFilterDropdown != null)
            {
                _eventFilterDropdown.RegisterValueChangedCallback(evt =>
                {
                    _currentEventFilter = evt.newValue;
                    RefreshEventHistoryDisplay();
                });
            }

            if (_clearEventsButton != null)
            {
                _clearEventsButton.clicked += () =>
                {
                    _eventHistory.Clear();
                    RefreshEventHistoryDisplay();
                };
            }
        }

        private void UpdateUI()
        {
            UpdateApiKeyStatus();
            UpdateSceneSetupValidation();
            UpdateCharacterIdValidation();
            UpdateAudioSuggestion();
            UpdateRuntimeStatus();
        }

        /// <summary>
        ///     Validates that required scene components are present and updates the UI accordingly.
        /// </summary>
        private void UpdateSceneSetupValidation()
        {
            if (_sceneSetupSection == null || _missingComponentsList == null) return;

            bool hasManager = FindFirstObjectByType<ConvaiManager>() != null;

            bool allPresent = hasManager;

            if (allPresent)
            {
                _sceneSetupSection.style.display = DisplayStyle.None;
                return;
            }

            _sceneSetupSection.style.display = DisplayStyle.Flex;
            _missingComponentsList.Clear();

            _missingComponentsList.Add(new Label("• ConvaiManager (required)") { pickingMode = PickingMode.Ignore });

            foreach (Label label in _missingComponentsList.Children()) label.AddToClassList("convai-error-item");
        }

        /// <summary>
        ///     Validates the Character ID format and updates the warning section.
        /// </summary>
        private void UpdateCharacterIdValidation()
        {
            if (_warningSection == null) return;

            string characterId = _character.CharacterId ?? "";
            string validationMessage = ValidateCharacterId(characterId);

            bool hasError = !string.IsNullOrEmpty(validationMessage);
            _warningSection.style.display = hasError ? DisplayStyle.Flex : DisplayStyle.None;

            if (_characterIdValidationMessage != null)
            {
                _characterIdValidationMessage.text = validationMessage;
                _characterIdValidationMessage.style.display = hasError ? DisplayStyle.Flex : DisplayStyle.None;
            }

            if (_characterIdField != null)
            {
                if (hasError && !string.IsNullOrEmpty(characterId))
                    _characterIdField.AddToClassList("convai-field-error");
                else
                    _characterIdField.RemoveFromClassList("convai-field-error");
            }
        }

        /// <summary>
        ///     Validates a Character ID and returns an error message if invalid, or empty string if valid.
        /// </summary>
        private string ValidateCharacterId(string characterId)
        {
            if (string.IsNullOrEmpty(characterId)) return "Character ID is required for the character to function.";

            if (characterId != characterId.Trim()) return "Character ID has leading or trailing whitespace.";

            if (characterId.Length != 36)
                return $"Character ID should be 36 characters (current: {characterId.Length}).";

            if (!GuidRegex.IsMatch(characterId))
                return "Character ID format is invalid. Expected: xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx";

            return string.Empty;
        }

        /// <summary>
        ///     Trims whitespace from the Character ID field when it loses focus.
        /// </summary>
        private void TrimCharacterIdWhitespace()
        {
            if (_characterIdProp == null) return;

            string currentValue = _characterIdProp.stringValue;
            string trimmedValue = currentValue?.Trim() ?? "";

            if (currentValue != trimmedValue)
            {
                _characterIdProp.stringValue = trimmedValue;
                serializedObject.ApplyModifiedProperties();
                ConvaiLogger.Debug("[Convai] Character ID whitespace trimmed.", LogCategory.Editor);
                UpdateCharacterIdValidation();
            }
        }

        /// <summary>
        ///     Updates the audio output suggestion box visibility.
        /// </summary>
        private void UpdateAudioSuggestion()
        {
            if (_audioSuggestionSection == null || _character == null) return;

            bool audioEnabled = _enableRemoteAudioProp != null && _enableRemoteAudioProp.boolValue;
            bool hasAudioOutput = _character.GetComponent<ConvaiAudioOutput>() != null;

            bool showSuggestion = audioEnabled && !hasAudioOutput;
            _audioSuggestionSection.style.display = showSuggestion ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void UpdateRuntimeStatus()
        {
            if (_statusIndicator == null || _statusText == null) return;

            if (UnityEngine.Application.isPlaying && _character != null)
            {
                if (_runtimeInfoSection != null)
                {
                    _runtimeInfoSection.style.display = DisplayStyle.Flex;
                    if (_injectionStatusLabel != null)
                    {
                        string injectionText = _character.IsInjected
                            ? "✓ Dependencies Injected"
                            : "✗ Dependencies Not Injected";
                        string readyText = _character.IsCharacterReady ? " | Ready" : "";
                        _injectionStatusLabel.text = injectionText + readyText;
                    }
                }

                if (_eventHistorySection != null) _eventHistorySection.style.display = DisplayStyle.Flex;

                UpdateExtendedRuntimeInfo();

                UpdateStatusIndicator(_character.SessionState, _character.IsCharacterReady);
            }
            else
            {
                if (_runtimeInfoSection != null)
                    _runtimeInfoSection.style.display = DisplayStyle.None;

                if (_eventHistorySection != null)
                    _eventHistorySection.style.display = DisplayStyle.None;

                UpdateStatusIndicator(SessionState.Disconnected, false);
            }
        }

        private void UpdateStatusIndicator(SessionState state, bool isCharacterReady)
        {
            if (_statusIndicator == null || _statusText == null) return;

            _statusIndicator.RemoveFromClassList("convai-status-indicator--idle");
            _statusIndicator.RemoveFromClassList("convai-status-indicator--connected");
            _statusIndicator.RemoveFromClassList("convai-status-indicator--active");
            _statusIndicator.RemoveFromClassList("convai-status-indicator--error");
            _statusIndicator.RemoveFromClassList("convai-status-indicator--ending");

            switch (state)
            {
                case SessionState.Disconnected:
                    _statusIndicator.AddToClassList("convai-status-indicator--idle");
                    _statusText.text = "Disconnected";
                    break;
                case SessionState.Connecting:
                    _statusIndicator.AddToClassList("convai-status-indicator--idle");
                    _statusText.text = "Connecting...";
                    break;
                case SessionState.Connected:
                    if (isCharacterReady)
                    {
                        _statusIndicator.AddToClassList("convai-status-indicator--active");
                        _statusText.text = "Active";
                    }
                    else
                    {
                        _statusIndicator.AddToClassList("convai-status-indicator--connected");
                        _statusText.text = "Connected";
                    }

                    break;
                case SessionState.Reconnecting:
                    _statusIndicator.AddToClassList("convai-status-indicator--idle");
                    _statusText.text = "Reconnecting...";
                    break;
                case SessionState.Disconnecting:
                    _statusIndicator.AddToClassList("convai-status-indicator--ending");
                    _statusText.text = "Disconnecting...";
                    break;
                case SessionState.Error:
                    _statusIndicator.AddToClassList("convai-status-indicator--error");
                    _statusText.text = "Error";
                    break;
                default:
                    _statusIndicator.AddToClassList("convai-status-indicator--idle");
                    _statusText.text = state.ToString();
                    break;
            }
        }

        /// <summary>
        ///     Updates the API key status section visibility and content.
        /// </summary>
        private void UpdateApiKeyStatus()
        {
            if (_apiKeySection == null) return;

            var settings = ConvaiSettings.Instance;
            bool hasApiKey = settings != null && settings.HasApiKey;

            if (hasApiKey)
                _apiKeySection.style.display = DisplayStyle.None;
            else
            {
                _apiKeySection.style.display = DisplayStyle.Flex;

                if (_apiKeyIndicator != null)
                {
                    _apiKeyIndicator.RemoveFromClassList("convai-api-key-indicator--configured");
                    _apiKeyIndicator.AddToClassList("convai-api-key-indicator--missing");
                }

                if (_apiKeyStatusText != null) _apiKeyStatusText.text = "API Key Not Configured";
            }
        }

        /// <summary>
        ///     Fetches the character name from the Convai API using the Character ID.
        /// </summary>
        private void FetchCharacterNameFromAPI() => _ = FetchCharacterNameFromAPIAsync();

        private async Task FetchCharacterNameFromAPIAsync()
        {
            if (_isFetchingName) return;

            string characterId = _character?.CharacterId;
            if (string.IsNullOrEmpty(characterId))
            {
                ConvaiLogger.Warning("[Convai] Cannot fetch character name: Character ID is empty.",
                    LogCategory.Editor);
                return;
            }

            string validationError = ValidateCharacterId(characterId);
            if (!string.IsNullOrEmpty(validationError))
            {
                ConvaiLogger.Warning($"[Convai] Cannot fetch character name: {validationError}", LogCategory.Editor);
                return;
            }

            var settings = ConvaiSettings.Instance;
            if (settings == null || !settings.HasApiKey)
            {
                ConvaiLogger.Warning("[Convai] Cannot fetch character name: API key not configured.",
                    LogCategory.Editor);
                return;
            }

            string apiKey = settings.ApiKey;

            _isFetchingName = true;
            UpdateFetchButtonState();

            try
            {
                var options = new ConvaiRestClientOptions(apiKey);
                using var client = new ConvaiRestClient(options);
                CharacterDetails details = await client.Characters.GetDetailsAsync(characterId);

                string fetchedName = details.CharacterName;
                if (!string.IsNullOrEmpty(fetchedName))
                {
                    _characterNameProp.stringValue = fetchedName;
                    serializedObject.ApplyModifiedProperties();
                    ConvaiLogger.Debug($"[Convai] Character name fetched: {fetchedName}", LogCategory.Editor);
                }
                else
                    ConvaiLogger.Warning("[Convai] Failed to fetch character name: Name is empty.", LogCategory.Editor);
            }
            catch (Exception ex)
            {
                ConvaiLogger.Error($"[Convai] Failed to fetch character name: {ex.Message}", LogCategory.Editor);
            }
            finally
            {
                _isFetchingName = false;
                UpdateFetchButtonState();
            }
        }

        /// <summary>
        ///     Updates the fetch button enabled state and loading indicator visibility.
        /// </summary>
        private void UpdateFetchButtonState()
        {
            if (_fetchNameButton != null)
            {
                _fetchNameButton.SetEnabled(!_isFetchingName);
                _fetchNameButton.text = _isFetchingName ? "..." : "Fetch";
            }

            if (_fetchLoadingIndicator != null)
                _fetchLoadingIndicator.style.display = _isFetchingName ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>
        ///     Refreshes the event history display based on current filter.
        /// </summary>
        private void RefreshEventHistoryDisplay()
        {
            if (_eventListScroll == null) return;

            _eventListScroll.Clear();

            List<EventHistoryItem> filteredEvents = _currentEventFilter == "All"
                ? _eventHistory
                : _eventHistory.FindAll(e => e.EventType == _currentEventFilter);

            if (filteredEvents.Count == 0)
            {
                if (_eventEmptyLabel != null) _eventListScroll.Add(_eventEmptyLabel);
                return;
            }

            foreach (EventHistoryItem evt in filteredEvents)
            {
                var eventItem = new VisualElement();
                eventItem.AddToClassList("convai-event-item");

                var timestampLabel = new Label(evt.Timestamp.ToString("HH:mm:ss"));
                timestampLabel.AddToClassList("convai-event-timestamp");

                var typeLabel = new Label(evt.EventType);
                typeLabel.AddToClassList("convai-event-type");
                typeLabel.AddToClassList($"convai-event-type--{evt.EventType.ToLowerInvariant()}");

                var dataLabel = new Label(evt.Data);
                dataLabel.AddToClassList("convai-event-data");

                eventItem.Add(timestampLabel);
                eventItem.Add(typeLabel);
                eventItem.Add(dataLabel);

                _eventListScroll.Add(eventItem);
            }
        }

        /// <summary>
        ///     Updates the extended runtime debug information.
        /// </summary>
        private void UpdateExtendedRuntimeInfo()
        {
            if (!UnityEngine.Application.isPlaying || _character == null) return;

            if (_sessionIdLabel != null)
            {
                string sessionState = _character.SessionState.ToString();
                _sessionIdLabel.text = $"Session State: {sessionState}";
            }

            if (_participantIdLabel != null)
            {
                bool isConnected = _character.IsSessionConnected;
                _participantIdLabel.text = isConnected
                    ? "Connection: ✓ Connected"
                    : "Connection: ✗ Disconnected";
            }

            if (_roomNameLabel != null)
            {
                bool isInConversation = _character.IsInConversation;
                _roomNameLabel.text = isInConversation
                    ? "Conversation: ✓ Active"
                    : "Conversation: ✗ Inactive";
            }

            if (_audioTrackStatusLabel != null)
            {
                bool remoteAudioEnabled = _character.IsRemoteAudioEnabled;
                _audioTrackStatusLabel.text = remoteAudioEnabled
                    ? "Remote Audio: ✓ Enabled"
                    : "Remote Audio: ✗ Disabled";
            }

            if (_micStatusLabel != null) _micStatusLabel.text = "Microphone: —";

            if (_speechStateLabel != null) _speechStateLabel.text = "Speech: —";
        }

        /// <summary>
        ///     Represents a single event in the history panel.
        /// </summary>
        private struct EventHistoryItem
        {
            public DateTime Timestamp;
            public string EventType;
            public string Data;
        }
    }
}
