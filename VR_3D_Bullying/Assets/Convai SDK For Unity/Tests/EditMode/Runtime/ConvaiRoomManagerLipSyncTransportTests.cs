using System;
using System.Collections.Generic;
using System.Reflection;
using Convai.Domain.DomainEvents.Session;
using Convai.Domain.EventSystem;
using Convai.Domain.Models.LipSync;
using Convai.Runtime;
using Convai.Runtime.Adapters.Networking;
using Convai.Runtime.Behaviors;
using Convai.Shared.Interfaces;
using Convai.Shared.Types;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Convai.Tests.EditMode.Runtime
{
    public class ConvaiRoomManagerLipSyncTransportTests
    {
        private readonly List<GameObject> _cleanup = new();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _cleanup.Count; i++)
                if (_cleanup[i] != null)
                    Object.DestroyImmediate(_cleanup[i]);

            _cleanup.Clear();
        }

        [Test]
        public void ResolveLipSyncTransportOptions_UsesSingleUniqueContract()
        {
            ConvaiRoomManager manager = CreateRoomManager();
            TestLipSyncCharacter characterA =
                CreateCharacter("char-a", CreateOptions(LipSyncProfileId.ARKit, "arkit", "EyeBlinkLeft"));
            TestLipSyncCharacter characterB =
                CreateCharacter("char-b", CreateOptions(LipSyncProfileId.ARKit, "arkit", "EyeBlinkLeft"));

            SetPrivateField(manager, "_characterList", new List<IConvaiCharacterAgent> { characterA, characterB });

            LipSyncTransportOptions resolved = InvokeResolve(manager);

            Assert.IsTrue(resolved.IsValid);
            Assert.AreEqual(LipSyncProfileId.ARKit, resolved.ProfileId);
            Assert.AreEqual("arkit", resolved.Format);
        }

        [Test]
        public void ResolveLipSyncTransportOptions_DisablesAndPublishesError_OnContractConflict()
        {
            ConvaiRoomManager manager = CreateRoomManager();
            var eventHub = new EventHub(new ImmediateScheduler());
            SessionError? published = null;
            eventHub.Subscribe<SessionError>(evt => published = evt, EventDeliveryPolicy.Immediate);
            LipSyncProfileId mhaProfile = new("mha");

            TestLipSyncCharacter characterA =
                CreateCharacter("char-a", CreateOptions(LipSyncProfileId.ARKit, "arkit", "EyeBlinkLeft"));
            TestLipSyncCharacter characterB =
                CreateCharacter("char-b", CreateOptions(mhaProfile, "mha", "EyeBlinkLeft"));

            SetPrivateField(manager, "_eventHub", eventHub);
            SetPrivateField(manager, "_characterList", new List<IConvaiCharacterAgent> { characterA, characterB });

            LipSyncTransportOptions resolved = InvokeResolve(manager);

            Assert.IsFalse(resolved.Enabled);
            Assert.IsTrue(published.HasValue);
            Assert.AreEqual("lipsync.contract_conflict", published.Value.ErrorCode);
        }

        [Test]
        public void ResolveLipSyncTransportOptions_Disables_WhenNoValidSourceExists()
        {
            ConvaiRoomManager manager = CreateRoomManager();
            TestLipSyncCharacter characterA = CreateCharacter("char-a", LipSyncTransportOptions.Disabled);
            characterA.HasCapability = false;

            SetPrivateField(manager, "_characterList", new List<IConvaiCharacterAgent> { characterA });

            LipSyncTransportOptions resolved = InvokeResolve(manager);

            Assert.IsFalse(resolved.Enabled);
            Assert.AreEqual(string.Empty, resolved.Provider);
        }

        [Test]
        public void CoreServerURL_FallsBackToSettingsServerUrl_WhenManagerBaseUrlIsBlank()
        {
            ConvaiSettings originalSettings = GetConvaiSettingsInstance();
            ConvaiSettings settings = CreateSettings("https://fallback.convai.test");

            try
            {
                SetConvaiSettingsInstance(settings);

                ConvaiRoomManager manager = CreateRoomManager();
                SetAutoPropertyBackingField(manager, nameof(ConvaiRoomManager.CoreServerBaseURL), string.Empty);
                SetAutoPropertyBackingField(manager, nameof(ConvaiRoomManager.ServerEndpoint), ConvaiServerEndpoint.Connect);

                Assert.AreEqual("https://fallback.convai.test/connect", manager.CoreServerURL);
            }
            finally
            {
                SetConvaiSettingsInstance(originalSettings);
                Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void CoreServerURL_UsesManagerOverride_WhenManagerBaseUrlIsConfigured()
        {
            ConvaiSettings originalSettings = GetConvaiSettingsInstance();
            ConvaiSettings settings = CreateSettings("https://fallback.convai.test");

            try
            {
                SetConvaiSettingsInstance(settings);

                ConvaiRoomManager manager = CreateRoomManager();
                SetAutoPropertyBackingField(manager, nameof(ConvaiRoomManager.CoreServerBaseURL),
                    "https://override.convai.test");
                SetAutoPropertyBackingField(manager, nameof(ConvaiRoomManager.ServerEndpoint),
                    ConvaiServerEndpoint.RoomSession);

                Assert.AreEqual("https://override.convai.test/room-session", manager.CoreServerURL);
            }
            finally
            {
                SetConvaiSettingsInstance(originalSettings);
                Object.DestroyImmediate(settings);
            }
        }

        private ConvaiRoomManager CreateRoomManager()
        {
            var go = new GameObject("room-manager-test");
            _cleanup.Add(go);
            return go.AddComponent<ConvaiRoomManager>();
        }

        private TestLipSyncCharacter CreateCharacter(string characterId, LipSyncTransportOptions options)
        {
            var go = new GameObject(characterId);
            _cleanup.Add(go);
            var character = go.AddComponent<TestLipSyncCharacter>();
            character.CharacterIdValue = characterId;
            character.TransportOptions = options;
            character.HasCapability = true;
            return character;
        }

        private static LipSyncTransportOptions CreateOptions(
            LipSyncProfileId profileId,
            string format,
            params string[] sourceNames)
        {
            return new LipSyncTransportOptions(
                true,
                "neurosync",
                profileId,
                format,
                sourceNames,
                true,
                10,
                60,
                LipSyncTransportOptions.DefaultFramesBufferDuration);
        }

        private static LipSyncTransportOptions InvokeResolve(ConvaiRoomManager manager)
        {
            MethodInfo method = typeof(ConvaiRoomManager).GetMethod(
                "ResolveLipSyncTransportOptions",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (method == null)
                throw new MissingMethodException(typeof(ConvaiRoomManager).Name, "ResolveLipSyncTransportOptions");

            return (LipSyncTransportOptions)method.Invoke(manager, null);
        }

        private static ConvaiSettings CreateSettings(string serverUrl)
        {
            var settings = ScriptableObject.CreateInstance<ConvaiSettings>();
            SetPrivateField(settings, "_serverUrl", serverUrl);
            return settings;
        }

        private static ConvaiSettings GetConvaiSettingsInstance()
        {
            FieldInfo instanceField = typeof(ConvaiSettings).GetField("_instance",
                BindingFlags.Static | BindingFlags.NonPublic);
            if (instanceField == null) throw new MissingFieldException(typeof(ConvaiSettings).Name, "_instance");

            return (ConvaiSettings)instanceField.GetValue(null);
        }

        private static void SetConvaiSettingsInstance(ConvaiSettings settings)
        {
            FieldInfo instanceField = typeof(ConvaiSettings).GetField("_instance",
                BindingFlags.Static | BindingFlags.NonPublic);
            if (instanceField == null) throw new MissingFieldException(typeof(ConvaiSettings).Name, "_instance");

            instanceField.SetValue(null, settings);
        }

        private static void SetAutoPropertyBackingField(object target, string propertyName, object value)
        {
            SetPrivateField(target, $"<{propertyName}>k__BackingField", value);
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null) throw new MissingFieldException(target.GetType().Name, fieldName);

            field.SetValue(target, value);
        }

        private sealed class ImmediateScheduler : IUnityScheduler
        {
            public void ScheduleOnMainThread(Action action) => action?.Invoke();
            public void ScheduleOnBackground(Action action) => action?.Invoke();
            public bool IsMainThread() => true;
        }

        private sealed class TestLipSyncCharacter : MonoBehaviour, IConvaiCharacterAgent, ILipSyncCapabilitySource
        {
            public string CharacterIdValue = "character";
            public LipSyncTransportOptions TransportOptions = LipSyncTransportOptions.Disabled;
            public bool HasCapability = true;

            public string CharacterId => CharacterIdValue;
            public string CharacterName => CharacterIdValue;
            public Color NameTagColor => Color.white;

            public bool EnableSessionResume => false;

            public void SendTrigger(string triggerName, string triggerMessage = null) { }
            public void SendDynamicInfo(string contextText) { }
            public void UpdateTemplateKeys(Dictionary<string, string> templateKeys) { }

            public bool TryGetLipSyncTransportOptions(out LipSyncTransportOptions options)
            {
                options = TransportOptions;
                return HasCapability;
            }
        }
    }
}
