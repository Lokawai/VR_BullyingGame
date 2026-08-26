using System;
using System.Reflection;
using Convai.Modules.LipSync;
using Convai.Runtime.Adapters.Networking;
using Convai.Runtime.Components;
using Convai.Runtime.Presentation.Views.Notifications;
using Convai.Shared.Compatibility;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Convai.Tests.EditMode.Release
{
    [Category("Release")]
    public sealed class SampleBootValidationTests
    {
        private const string BasicSampleScenePath =
            "Packages/com.convai.convai-sdk-for-unity/Samples/BasicSample/Scenes/Basic Sample.unity";

        private const string LipSyncSampleScenePath =
            "Packages/com.convai.convai-sdk-for-unity/Samples/LipSyncSample/Scenes/LipSync Sample.unity";

        [TearDown]
        public void TearDown()
        {
            // Ensure subsequent tests do not inherit the sample scene state.
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        // The Basic sample is the minimal conversation path: manager, room, player, character and
        // the notification UI. It deliberately ships without an Actions setup, so nothing here
        // asserts action config, dispatchers or behaviors.
        [Test]
        public void BasicSample_Loads_AndContainsCoreRuntimeObjects()
        {
            Scene scene = EditorSceneManager.OpenScene(BasicSampleScenePath, OpenSceneMode.Single);
            Assert.IsTrue(scene.IsValid(), "Expected Basic sample scene to load.");

            ConvaiManager manager = Object.FindAnyObjectByType<ConvaiManager>(FindObjectsInactive.Include);
            Assert.IsNotNull(manager,
                "Basic sample should contain ConvaiManager.");
            Assert.IsNotNull(ResolveOrProvisionRoomManager(manager),
                "Basic sample should expose a ConvaiRoomManager path (serialized or manager-provisioned).");
            Assert.IsNotNull(Object.FindAnyObjectByType<ConvaiPlayer>(FindObjectsInactive.Include),
                "Basic sample should contain ConvaiPlayer.");

            ConvaiCharacter[] characters = ConvaiObjectFind.All<ConvaiCharacter>(FindObjectsInactive.Include);
            Assert.GreaterOrEqual(characters.Length, 1, "Basic sample should contain at least one ConvaiCharacter.");

            Assert.IsNotNull(Object.FindAnyObjectByType<NotificationHandler>(FindObjectsInactive.Include),
                "Basic sample should include the NotificationSystem prefab instance.");

            AssertNoEditorOnlyBehavioursInScene();
        }

        [Test]
        public void LipSyncSample_Loads_AndContainsLipSyncComponent()
        {
            Scene scene = EditorSceneManager.OpenScene(LipSyncSampleScenePath, OpenSceneMode.Single);
            Assert.IsTrue(scene.IsValid(), "Expected LipSync sample scene to load.");

            ConvaiManager manager = Object.FindAnyObjectByType<ConvaiManager>(FindObjectsInactive.Include);
            Assert.IsNotNull(manager,
                "LipSync sample should contain ConvaiManager.");
            Assert.IsNotNull(ResolveOrProvisionRoomManager(manager),
                "LipSync sample should expose a ConvaiRoomManager path (serialized or manager-provisioned).");

            ConvaiCharacter[] characters = ConvaiObjectFind.All<ConvaiCharacter>(FindObjectsInactive.Include);
            Assert.GreaterOrEqual(characters.Length, 1, "LipSync sample should contain at least one ConvaiCharacter.");

            Assert.IsNotNull(Object.FindAnyObjectByType<ConvaiLipSyncComponent>(FindObjectsInactive.Include),
                "LipSync sample should include at least one ConvaiLipSyncComponent.");

            AssertLipSyncBackgroundLightLayers();
            AssertNoEditorOnlyBehavioursInScene();
        }

        private static void AssertLipSyncBackgroundLightLayers()
        {
            Light[] lights = ConvaiObjectFind.All<Light>(FindObjectsInactive.Include);
            Light backgroundLight = Array.Find(lights, light => light != null && light.name == "Background Light");
            Assert.IsNotNull(backgroundLight, "LipSync sample should contain its isolated Background Light.");
            Assert.That(backgroundLight.renderingLayerMask, Is.EqualTo(2u),
                "Background Light must affect only the background rendering layer.");

            MonoBehaviour additionalLightData = Array.Find(
                backgroundLight.GetComponents<MonoBehaviour>(),
                behaviour => behaviour != null &&
                             behaviour.GetType().FullName ==
                             "UnityEngine.Rendering.Universal.UniversalAdditionalLightData");
            Assert.IsNotNull(additionalLightData,
                "Background Light should include Universal Additional Light Data.");

            SerializedObject serializedLightData = new(additionalLightData);
            AssertRenderingLayerMask(serializedLightData.FindProperty("m_RenderingLayers"),
                "URP 17.0 legacy rendering layers");

            SerializedProperty currentMask = serializedLightData.FindProperty("m_RenderingLayersMask");
            if (currentMask != null)
                AssertRenderingLayerMask(currentMask.FindPropertyRelative("m_Bits"),
                    "URP 17.4+ rendering layers");
        }

        private static void AssertRenderingLayerMask(SerializedProperty property, string label)
        {
            Assert.IsNotNull(property, $"Missing {label} serialization on Background Light.");
            Assert.That(property.longValue, Is.EqualTo(2L),
                $"{label} must stay aligned so the high-intensity background light cannot affect the character.");
        }

        private static void AssertNoEditorOnlyBehavioursInScene()
        {
            MonoBehaviour[] behaviours = ConvaiObjectFind.All<MonoBehaviour>(FindObjectsInactive.Include);

            for (int i = 0; i < behaviours.Length; i++)
            {
                MonoBehaviour behaviour = behaviours[i];
                if (behaviour == null) continue;

                Type type = behaviour.GetType();
                string assemblyName = type.Assembly.GetName().Name ?? string.Empty;
                if (assemblyName.EndsWith(".Editor", StringComparison.Ordinal))
                {
                    Assert.Fail(
                        $"Scene contains editor-only behaviour '{type.FullName}' from assembly '{assemblyName}'.");
                }

                string ns = type.Namespace ?? string.Empty;
                if (ns.Contains(".Editor", StringComparison.Ordinal))
                {
                    Assert.Fail($"Scene contains editor-only behaviour '{type.FullName}' (namespace '{ns}').");
                }
            }
        }

        private static ConvaiRoomManager ResolveOrProvisionRoomManager(ConvaiManager manager)
        {
            if (manager == null) return null;

            ConvaiRoomManager roomManager = Object.FindAnyObjectByType<ConvaiRoomManager>(FindObjectsInactive.Include);
            if (roomManager != null) return roomManager;

            MethodInfo ensureRoomManagerReference = typeof(ConvaiManager).GetMethod(
                "EnsureRoomManagerReference",
                BindingFlags.Instance | BindingFlags.NonPublic);
            ensureRoomManagerReference?.Invoke(manager, null);

            return manager.GetComponent<ConvaiRoomManager>()
                   ?? Object.FindAnyObjectByType<ConvaiRoomManager>(FindObjectsInactive.Include);
        }
    }
}
