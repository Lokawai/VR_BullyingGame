using System;
using System.Collections.Generic;
using Convai.Domain.EventSystem;
using Convai.Domain.Logging;
using Convai.Runtime;
using Convai.Runtime.Behaviors;
using Convai.Runtime.Components;
using Convai.Shared;
using Convai.Shared.DependencyInjection;
using Convai.Tests.EditMode.Mocks;
using NUnit.Framework;
using UnityEngine;
using ILogger = Convai.Domain.Logging.ILogger;
using Object = UnityEngine.Object;

namespace Convai.Tests.EditMode
{
    /// <summary>
    ///     Tests for ConvaiCompositionRoot component discovery and dependency injection.
    /// </summary>
    public class CompositionRootTests
    {
        private readonly List<GameObject> _createdObjects = new();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in _createdObjects)
                if (go != null)
                    Object.DestroyImmediate(go);
            _createdObjects.Clear();

            ConvaiServiceLocator.Shutdown();
        }

        [Test]
        public void CompositionRoot_FindsComponents_Successfully()
        {
            var characterGo = new GameObject("TestCharacter");
            _createdObjects.Add(characterGo);
            var character = characterGo.AddComponent<ConvaiCharacter>();
            character.Configure("test-char-id", "TestCharacter");

            var compositionRootGo = new GameObject("CompositionRoot");
            _createdObjects.Add(compositionRootGo);

            ConvaiCharacter[] foundCharacters = Object.FindObjectsByType<ConvaiCharacter>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None
            );

            Assert.IsNotNull(foundCharacters, "FindObjectsByType should return an array");
            Assert.AreEqual(1, foundCharacters.Length, "Should find exactly 1 ConvaiCharacter");
            Assert.AreEqual(character, foundCharacters[0], "Found character should match created character");
            Assert.AreEqual("test-char-id", foundCharacters[0].CharacterId, "Character ID should be preserved");
        }

        [Test]
        public void CompositionRoot_InjectsDependencies_IntoNPC()
        {
            var scheduler = new ImmediateScheduler();
            var eventHub = new EventHub(scheduler);
            var connectionService = new MockRoomConnectionService();
            var audioService = new MockRoomAudioService();
            var locatorService = new MockCharacterLocatorService();
            var logger = new TestLogger();

            var characterGo = new GameObject("TestNPC");
            _createdObjects.Add(characterGo);
            var character = characterGo.AddComponent<ConvaiCharacter>();
            character.Configure("npc-char-id", "TestNPC");

            character.Inject(eventHub, connectionService, audioService, locatorService, logger);

            Assert.IsNotNull(character, "Character should exist after injection");
            Assert.AreEqual("npc-char-id", character.CharacterId, "Character ID should be preserved after injection");
            Assert.AreEqual("TestNPC", character.CharacterName, "Character name should be preserved after injection");

            Assert.IsFalse(character.IsSessionConnected, "Character should not be connected initially");

            connectionService.RaiseConnected();
            Assert.IsTrue(character.IsSessionConnected, "Character should be connected after connection event");
        }

        [Test]
        public void CompositionRoot_InjectsDependencies_IntoPlayer()
        {
            var scheduler = new ImmediateScheduler();
            var eventHub = new EventHub(scheduler);

            var playerGo = new GameObject("TestPlayer");
            _createdObjects.Add(playerGo);
            var player = playerGo.AddComponent<ConvaiPlayer>();

            ConvaiPlayer[] foundPlayers = Object.FindObjectsByType<ConvaiPlayer>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None
            );

            Assert.IsNotNull(foundPlayers, "FindObjectsByType should return an array");
            Assert.AreEqual(1, foundPlayers.Length, "Should find exactly 1 ConvaiPlayer");
            Assert.AreEqual(player, foundPlayers[0], "Found player should match created player");

            Assert.IsTrue(player is IConvaiPlayerAgent,
                "ConvaiPlayer should implement IConvaiPlayerAgent interface");
        }

        [Test]
        public void CompositionRoot_Handles_MissingService()
        {
            var characterGo = new GameObject("TestCharacter");
            _createdObjects.Add(characterGo);
            var character = characterGo.AddComponent<ConvaiCharacter>();
            character.Configure("test-char-id", "TestCharacter");

            Assert.Throws<ArgumentNullException>(() =>
            {
                character.Inject(null, null, null, null);
            }, "Inject should throw ArgumentNullException when EventHub is null");

            Assert.IsNotNull(character, "Character should still exist after failed injection");
            Assert.AreEqual("test-char-id", character.CharacterId, "Character ID should be preserved");
        }

        [Test]
        public void CompositionRoot_Discovers_And_Injects_Custom_IInjectable()
        {
            ConvaiServiceLocator.Initialize();

            var customInjectableGo = new GameObject("CustomInjectable");
            _createdObjects.Add(customInjectableGo);
            var customInjectable = customInjectableGo.AddComponent<CustomInjectableProbe>();

            var compositionRootGo = new GameObject("CompositionRoot");
            _createdObjects.Add(compositionRootGo);
            var compositionRoot = compositionRootGo.AddComponent<ConvaiCompositionRoot>();

            compositionRoot.InjectAllComponents();

            Assert.IsTrue(customInjectable.WasInjected, "Custom IInjectable should be discovered and injected.");
        }

        #region Test Helpers

        private sealed class ImmediateScheduler : IUnityScheduler
        {
            public void ScheduleOnMainThread(Action action) => action?.Invoke();
            public void ScheduleOnBackground(Action action) => action?.Invoke();
            public bool IsMainThread() => true;
        }

        private sealed class TestLogger : ILogger
        {
            public List<string> DebugMessages { get; } = new();
            public List<string> InfoMessages { get; } = new();
            public List<string> WarningMessages { get; } = new();
            public List<string> ErrorMessages { get; } = new();

            public void Log(LogLevel level, string message, LogCategory category = LogCategory.SDK) { }

            public void Log(LogLevel level, string message, IReadOnlyDictionary<string, object> context,
                LogCategory category = LogCategory.SDK)
            {
            }

            public void Debug(string message, LogCategory category = LogCategory.SDK) => DebugMessages.Add(message);

            public void Debug(string message, IReadOnlyDictionary<string, object> context,
                LogCategory category = LogCategory.SDK) => Debug(message, category);

            public void Info(string message, LogCategory category = LogCategory.SDK) => InfoMessages.Add(message);

            public void Info(string message, IReadOnlyDictionary<string, object> context,
                LogCategory category = LogCategory.SDK) => Info(message, category);

            public void Warning(string message, LogCategory category = LogCategory.SDK) => WarningMessages.Add(message);

            public void Warning(string message, IReadOnlyDictionary<string, object> context,
                LogCategory category = LogCategory.SDK) => Warning(message, category);

            public void Error(string message, LogCategory category = LogCategory.SDK) => ErrorMessages.Add(message);

            public void Error(string message, IReadOnlyDictionary<string, object> context,
                LogCategory category = LogCategory.SDK) => Error(message, category);

            public void Error(Exception exception, string message = null, LogCategory category = LogCategory.SDK) =>
                ErrorMessages.Add(message ?? exception.Message);

            public void Error(Exception exception, string message, IReadOnlyDictionary<string, object> context,
                LogCategory category = LogCategory.SDK) => Error(exception, message, category);

            public bool IsEnabled(LogLevel level, LogCategory category) => true;
        }

        private sealed class CustomInjectableProbe : MonoBehaviour, IInjectable
        {
            public bool WasInjected { get; private set; }

            public void InjectServices(IServiceContainer container) => WasInjected = true;
        }

        #endregion
    }
}
