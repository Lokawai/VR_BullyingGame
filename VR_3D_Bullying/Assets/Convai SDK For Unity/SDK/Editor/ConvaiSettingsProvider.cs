using System;
using System.Collections.Generic;
using Convai.Editor.ConfigurationWindow;
using Convai.Editor.Utilities;
using Convai.Runtime;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityApplication = UnityEngine.Application;

namespace Convai.Editor
{
    /// <summary>
    ///     Provides Convai SDK settings in the Project Settings window.
    ///     Accessible via Edit > Project Settings > Convai SDK
    /// </summary>
    public static class ConvaiSettingsProvider
    {
        private const string SettingsPath = "Project/Convai SDK";
        private const string DefaultServerUrl = "https://live.convai.com";
        private static readonly string[] TranscriptModeDisplayNames = { "Chat", "Subtitle", "Q&A" };

        private static SerializedObject _serializedSettings;
        private static bool _showApiKey;
        private static bool _showDiagnostics = true;
        private static bool _showCategoryOverrides;
        private static bool _showAdvanced;

        private static GUIStyle _cardStyle;
        private static GUIStyle _sectionTitleStyle;
        private static GUIStyle _titleStyle;
        private static GUIStyle _subtitleStyle;
        private static GUIStyle _statusBadgeStyle;

        /// <summary>Creates the Project Settings provider for Convai SDK settings.</summary>
        [SettingsProvider]
        public static SettingsProvider CreateSettingsProvider()
        {
            var provider = new SettingsProvider(SettingsPath, SettingsScope.Project)
            {
                label = "Convai SDK",
                guiHandler = DrawSettingsGUI,
                keywords = new HashSet<string>(new[]
                {
                    "Convai", "AI", "Character", "API", "Key", "Microphone", "Logging", "Vision", "Camera",
                    "Transcript", "Notification", "Settings", "Diagnostics", "Emotion", "State Of Mind"
                }),
                activateHandler = OnActivate
            };

            return provider;
        }

        private static void OnActivate(string searchContext, VisualElement rootElement)
        {
            _serializedSettings = new SerializedObject(GetOrCreateSettings());
            InitializeStyles();
        }

        private static void DrawSettingsGUI(string searchContext)
        {
            if (_serializedSettings == null || _serializedSettings.targetObject == null)
                _serializedSettings = new SerializedObject(GetOrCreateSettings());

            InitializeStyles();
            _serializedSettings.Update();

            SerializedProperty apiKeyProp = _serializedSettings.FindProperty("_apiKey");
            SerializedProperty serverUrlProp = _serializedSettings.FindProperty("_serverUrl");
            SerializedProperty playerNameProp = _serializedSettings.FindProperty("_playerName");
            SerializedProperty transcriptEnabledProp = _serializedSettings.FindProperty("_transcriptSystemEnabled");
            SerializedProperty notificationEnabledProp = _serializedSettings.FindProperty("_notificationSystemEnabled");
            SerializedProperty transcriptStyleProp = _serializedSettings.FindProperty("_activeTranscriptStyleIndex");
            SerializedProperty defaultMicIndexProp = _serializedSettings.FindProperty("_defaultMicrophoneIndex");
            SerializedProperty visionEnabledProp = _serializedSettings.FindProperty("_visionEnabled");
            SerializedProperty visionCaptureWidthProp = _serializedSettings.FindProperty("_visionCaptureWidth");
            SerializedProperty visionCaptureHeightProp = _serializedSettings.FindProperty("_visionCaptureHeight");
            SerializedProperty visionFrameRateProp = _serializedSettings.FindProperty("_visionFrameRate");
            SerializedProperty visionJpegQualityProp = _serializedSettings.FindProperty("_visionJpegQuality");
            SerializedProperty globalLogLevelProp = _serializedSettings.FindProperty("_globalLogLevel");
            SerializedProperty includeStackTracesProp = _serializedSettings.FindProperty("_includeStackTraces");
            SerializedProperty coloredOutputProp = _serializedSettings.FindProperty("_coloredOutput");
            SerializedProperty categoryOverridesProp = _serializedSettings.FindProperty("_categoryOverrides");
            SerializedProperty connectionTimeoutProp = _serializedSettings.FindProperty("_connectionTimeout");

            if (apiKeyProp == null || serverUrlProp == null || playerNameProp == null)
            {
                EditorGUILayout.HelpBox("Convai settings asset schema is incomplete. Reimport the SDK package.",
                    MessageType.Error);
                return;
            }

            EditorGUILayout.Space(8);
            DrawHeaderCard(apiKeyProp, serverUrlProp);
            EditorGUILayout.Space(4);
            DrawConnectionAndIdentityCard(apiKeyProp, playerNameProp);
            EditorGUILayout.Space(4);
            DrawExperienceCard(transcriptEnabledProp, notificationEnabledProp, transcriptStyleProp,
                defaultMicIndexProp);
            EditorGUILayout.Space(4);
            DrawVisionCard(visionEnabledProp, visionCaptureWidthProp, visionCaptureHeightProp, visionFrameRateProp,
                visionJpegQualityProp);
            EditorGUILayout.Space(4);
            DrawDiagnosticsCard(globalLogLevelProp, includeStackTracesProp, coloredOutputProp, categoryOverridesProp);
            EditorGUILayout.Space(4);
            DrawAdvancedCard(serverUrlProp, connectionTimeoutProp);
            EditorGUILayout.Space(4);
            DrawQuickActionsCard();

            if (_serializedSettings.ApplyModifiedProperties()) AssetDatabase.SaveAssets();
        }

        private static void DrawHeaderCard(
            SerializedProperty apiKeyProp,
            SerializedProperty serverUrlProp)
        {
            bool hasApiKey = !string.IsNullOrWhiteSpace(apiKeyProp.stringValue);
            string serverUrl = serverUrlProp.stringValue ?? string.Empty;
            bool isProduction = string.Equals(NormalizeUrl(serverUrl), NormalizeUrl(DefaultServerUrl),
                StringComparison.OrdinalIgnoreCase);

            EditorGUILayout.BeginVertical(_cardStyle);
            EditorGUILayout.LabelField("Convai SDK Settings", _titleStyle);
            EditorGUILayout.LabelField(
                "Project-wide defaults for connection, runtime UX, and diagnostics. Keep this page minimal; tune advanced options only when needed.",
                _subtitleStyle);
            EditorGUILayout.Space(8);

            EditorGUILayout.BeginHorizontal();
            DrawStatusBadge("API Key", hasApiKey ? "Configured" : "Missing",
                hasApiKey ? GetSuccessColor() : GetWarnColor(),
                GUILayout.Width(180f), GUILayout.Height(22f));
            DrawStatusBadge("Environment", isProduction ? "Production" : "Custom",
                isProduction ? GetInfoColor() : GetWarnColor(),
                GUILayout.Width(180f), GUILayout.Height(22f));
            EditorGUILayout.EndHorizontal();

            if (!hasApiKey)
            {
                EditorGUILayout.Space(6);
                EditorGUILayout.HelpBox(
                    "Set your API key to enable account features, character sessions, and backend tools.",
                    MessageType.Warning);
            }
            else if (!isProduction)
            {
                EditorGUILayout.Space(6);
                EditorGUILayout.HelpBox(
                    "Custom server URL is active. Keep this only for staging, enterprise, or support-directed setups.",
                    MessageType.Info);
            }

            EditorGUILayout.EndVertical();
        }

        private static void DrawConnectionAndIdentityCard(SerializedProperty apiKeyProp,
            SerializedProperty playerNameProp)
        {
            EditorGUILayout.BeginVertical(_cardStyle);
            EditorGUILayout.LabelField("Connection & Identity", _sectionTitleStyle);
            EditorGUILayout.Space(4);

            EditorGUILayout.BeginHorizontal();
            string currentApiKey = apiKeyProp.stringValue ?? string.Empty;
            string nextApiKey = _showApiKey
                ? EditorGUILayout.TextField(
                    new GUIContent("API Key", "Your Convai dashboard API key."),
                    currentApiKey)
                : EditorGUILayout.PasswordField(
                    new GUIContent("API Key", "Your Convai dashboard API key."),
                    currentApiKey);
            if (!string.Equals(nextApiKey, currentApiKey, StringComparison.Ordinal))
                apiKeyProp.stringValue = nextApiKey?.Trim() ?? string.Empty;

            if (GUILayout.Button(_showApiKey ? "Hide" : "Show", GUILayout.Width(56f))) _showApiKey = !_showApiKey;
            EditorGUILayout.EndHorizontal();

            string currentPlayerName = playerNameProp.stringValue ?? string.Empty;
            string nextPlayerName = EditorGUILayout.TextField(
                new GUIContent("Default Player Name", "Used when runtime player display name is not set."),
                currentPlayerName);
            if (!string.Equals(nextPlayerName, currentPlayerName, StringComparison.Ordinal))
                playerNameProp.stringValue = nextPlayerName;

            EditorGUILayout.EndVertical();
        }

        private static void DrawExperienceCard(
            SerializedProperty transcriptEnabledProp,
            SerializedProperty notificationEnabledProp,
            SerializedProperty transcriptStyleProp,
            SerializedProperty defaultMicIndexProp)
        {
            EditorGUILayout.BeginVertical(_cardStyle);
            EditorGUILayout.LabelField("Runtime Defaults", _sectionTitleStyle);
            EditorGUILayout.Space(4);

            transcriptEnabledProp.boolValue = EditorGUILayout.Toggle(
                new GUIContent("Transcript System", "Enable transcript UI/event flow by default."),
                transcriptEnabledProp.boolValue);
            notificationEnabledProp.boolValue = EditorGUILayout.Toggle(
                new GUIContent("Notification System", "Enable notification bridge for session state and errors."),
                notificationEnabledProp.boolValue);

            int normalizedModeIndex = NormalizeTranscriptModeIndex(transcriptStyleProp.intValue);
            int selectedModeIndex = EditorGUILayout.Popup(
                new GUIContent("Transcript Mode", "Preferred default transcript presentation style."),
                normalizedModeIndex,
                TranscriptModeDisplayNames);
            if (selectedModeIndex != normalizedModeIndex) transcriptStyleProp.intValue = selectedModeIndex;

            int micIndex = defaultMicIndexProp.intValue;
            int clampedMicIndex = Mathf.Clamp(
                EditorGUILayout.IntField(
                    new GUIContent("Default Microphone Index", "0 uses system default microphone."),
                    micIndex),
                0, 10);
            if (clampedMicIndex != micIndex) defaultMicIndexProp.intValue = clampedMicIndex;

            EditorGUILayout.HelpBox("Use microphone index 0 for most projects unless you need a fixed device order.",
                MessageType.None);
            EditorGUILayout.EndVertical();
        }

        private static void DrawVisionCard(
            SerializedProperty visionEnabledProp,
            SerializedProperty visionCaptureWidthProp,
            SerializedProperty visionCaptureHeightProp,
            SerializedProperty visionFrameRateProp,
            SerializedProperty visionJpegQualityProp)
        {
            EditorGUILayout.BeginVertical(_cardStyle);
            EditorGUILayout.LabelField("Vision (Optional)", _sectionTitleStyle);
            EditorGUILayout.Space(4);

            visionEnabledProp.boolValue = EditorGUILayout.Toggle(
                new GUIContent("Enable Vision", "Enable camera capture for visual context in conversations."),
                visionEnabledProp.boolValue);

            using (new EditorGUI.DisabledGroupScope(!visionEnabledProp.boolValue))
            {
                DrawVisionPresetButtons(visionCaptureWidthProp, visionCaptureHeightProp, visionFrameRateProp,
                    visionJpegQualityProp);

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PrefixLabel(new GUIContent("Capture Resolution", "Width x Height in pixels"));
                visionCaptureWidthProp.intValue = Mathf.Clamp(EditorGUILayout.IntField(visionCaptureWidthProp.intValue),
                    320, 1920);
                GUILayout.Label("x", GUILayout.Width(10f));
                visionCaptureHeightProp.intValue =
                    Mathf.Clamp(EditorGUILayout.IntField(visionCaptureHeightProp.intValue), 240, 1080);
                GUILayout.Label("px", GUILayout.Width(22f));
                EditorGUILayout.EndHorizontal();

                visionFrameRateProp.intValue = EditorGUILayout.IntSlider(
                    new GUIContent("Frame Rate (FPS)", "Lower values reduce bandwidth and CPU usage."),
                    Mathf.Clamp(visionFrameRateProp.intValue, 1, 30), 1, 30);
                visionJpegQualityProp.intValue = EditorGUILayout.IntSlider(
                    new GUIContent("JPEG Quality (%)", "Higher quality improves clarity but uses more bandwidth."),
                    Mathf.Clamp(visionJpegQualityProp.intValue, 1, 100), 1, 100);
            }

            if (!visionEnabledProp.boolValue)
            {
                EditorGUILayout.HelpBox(
                    "Keep Vision disabled unless your character interactions require camera context.",
                    MessageType.Info);
            }

            EditorGUILayout.EndVertical();
        }

        private static void DrawDiagnosticsCard(
            SerializedProperty globalLogLevelProp,
            SerializedProperty includeStackTracesProp,
            SerializedProperty coloredOutputProp,
            SerializedProperty categoryOverridesProp)
        {
            EditorGUILayout.BeginVertical(_cardStyle);
            _showDiagnostics = EditorGUILayout.Foldout(_showDiagnostics, "Diagnostics & Logging", true,
                EditorStyles.foldoutHeader);

            if (_showDiagnostics)
            {
                EditorGUILayout.Space(4);

                globalLogLevelProp.enumValueIndex = EditorGUILayout.Popup(
                    new GUIContent("Global Log Level", "Minimum log level. Lower-severity logs are filtered."),
                    globalLogLevelProp.enumValueIndex,
                    globalLogLevelProp.enumDisplayNames);
                includeStackTracesProp.boolValue = EditorGUILayout.Toggle(
                    new GUIContent("Include Stack Traces", "Include stack traces for warning and error logs."),
                    includeStackTracesProp.boolValue);
                coloredOutputProp.boolValue = EditorGUILayout.Toggle(
                    new GUIContent("Colored Console Output", "Use colorized console messages when supported."),
                    coloredOutputProp.boolValue);

                if (categoryOverridesProp != null)
                {
                    _showCategoryOverrides = EditorGUILayout.Foldout(
                        _showCategoryOverrides,
                        $"Category Overrides ({categoryOverridesProp.arraySize})",
                        true);

                    if (_showCategoryOverrides)
                    {
                        EditorGUI.indentLevel++;
                        EditorGUILayout.PropertyField(categoryOverridesProp, GUIContent.none, true);
                        EditorGUI.indentLevel--;
                    }
                }
            }

            EditorGUILayout.EndVertical();
        }

        private static void DrawAdvancedCard(
            SerializedProperty serverUrlProp,
            SerializedProperty connectionTimeoutProp)
        {
            EditorGUILayout.BeginVertical(_cardStyle);
            _showAdvanced = EditorGUILayout.Foldout(_showAdvanced, "Advanced", true, EditorStyles.foldoutHeader);

            if (_showAdvanced)
            {
                EditorGUILayout.Space(4);
                serverUrlProp.stringValue = EditorGUILayout.TextField(
                    new GUIContent("Realtime Server URL", "Room connect host. Default is https://live.convai.com."),
                    serverUrlProp.stringValue);
                connectionTimeoutProp.floatValue = EditorGUILayout.Slider(
                    new GUIContent("Connection Timeout (s)",
                        "Adjust only if directed by support or for custom transports."),
                    Mathf.Clamp(connectionTimeoutProp.floatValue, 5f, 120f),
                    5f,
                    120f);
                EditorGUILayout.HelpBox(
                    "Native realtime uses the transport runtime path. Advanced values should stay at defaults for most projects unless support directs otherwise.",
                    MessageType.Info);
            }

            EditorGUILayout.EndVertical();
        }

        private static void DrawQuickActionsCard()
        {
            EditorGUILayout.BeginVertical(_cardStyle);
            EditorGUILayout.LabelField("Quick Actions", _sectionTitleStyle);
            EditorGUILayout.Space(4);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Open Convai Editor")) ConvaiConfigurationWindowEditor.OpenWelcomeWindow();
            if (GUILayout.Button("Open Dashboard")) UnityApplication.OpenURL(ConvaiEditorLinks.DashboardHomeUrl);
            if (GUILayout.Button("Open Docs")) UnityApplication.OpenURL(ConvaiEditorLinks.DocsUnityQuickstartUrl);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Validate Scene Setup")) ConvaiSetupWizard.ValidateSceneSetup();
            if (GUILayout.Button("Select Settings Asset"))
            {
                Selection.activeObject = _serializedSettings.targetObject;
                EditorGUIUtility.PingObject(_serializedSettings.targetObject);
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private static int NormalizeTranscriptModeIndex(int index)
        {
            return index switch
            {
                1 => 1,
                2 => 2,
                _ => 0
            };
        }

        private static string NormalizeUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return string.Empty;

            return url.Trim().TrimEnd('/');
        }

        private static void DrawVisionPresetButtons(
            SerializedProperty widthProp,
            SerializedProperty heightProp,
            SerializedProperty fpsProp,
            SerializedProperty qualityProp)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Presets", GUILayout.Width(EditorGUIUtility.labelWidth - 4f));

            if (GUILayout.Button("Low BW"))
                ApplyVisionPreset(widthProp, heightProp, fpsProp, qualityProp, 640, 360, 10, 55);

            if (GUILayout.Button("Balanced"))
                ApplyVisionPreset(widthProp, heightProp, fpsProp, qualityProp, 1280, 720, 15, 75);

            if (GUILayout.Button("High Quality"))
                ApplyVisionPreset(widthProp, heightProp, fpsProp, qualityProp, 1920, 1080, 24, 85);

            EditorGUILayout.EndHorizontal();
        }

        private static void ApplyVisionPreset(
            SerializedProperty widthProp,
            SerializedProperty heightProp,
            SerializedProperty fpsProp,
            SerializedProperty qualityProp,
            int width,
            int height,
            int fps,
            int quality)
        {
            widthProp.intValue = width;
            heightProp.intValue = height;
            fpsProp.intValue = fps;
            qualityProp.intValue = quality;
        }

        private static void DrawStatusBadge(string label, string value, Color backgroundColor,
            params GUILayoutOption[] options)
        {
            string text = $"{label}: {value}";
            Rect rect = GUILayoutUtility.GetRect(new GUIContent(text), _statusBadgeStyle, options);
            EditorGUI.DrawRect(rect, backgroundColor);
            GUI.Label(rect, text, _statusBadgeStyle);
        }

        private static Color GetSuccessColor()
        {
            return EditorGUIUtility.isProSkin
                ? new Color(0.13f, 0.45f, 0.25f, 0.95f)
                : new Color(0.64f, 0.86f, 0.71f, 1f);
        }

        private static Color GetWarnColor()
        {
            return EditorGUIUtility.isProSkin
                ? new Color(0.55f, 0.34f, 0.08f, 0.95f)
                : new Color(0.95f, 0.78f, 0.48f, 1f);
        }

        private static Color GetInfoColor()
        {
            return EditorGUIUtility.isProSkin
                ? new Color(0.14f, 0.32f, 0.58f, 0.95f)
                : new Color(0.73f, 0.84f, 0.97f, 1f);
        }

        private static void InitializeStyles()
        {
            _titleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 17, fontStyle = FontStyle.Bold };

            _subtitleStyle = new GUIStyle(EditorStyles.wordWrappedMiniLabel) { fontSize = 10 };

            _sectionTitleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12 };

            _cardStyle = new GUIStyle(EditorStyles.helpBox)
            {
                padding = new RectOffset(12, 12, 10, 10), margin = new RectOffset(0, 0, 2, 8)
            };

            _statusBadgeStyle = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 10,
                normal =
                {
                    textColor = EditorGUIUtility.isProSkin
                        ? new Color(0.94f, 0.94f, 0.94f)
                        : new Color(0.1f, 0.1f, 0.1f)
                }
            };
        }

        private static ConvaiSettings GetOrCreateSettings()
        {
            // Delegate to the canonical singleton so that Project Settings, Configuration Window,
            // and runtime code all operate on the exact same asset instance.
            return ConvaiSettings.Instance;
        }
    }
}
