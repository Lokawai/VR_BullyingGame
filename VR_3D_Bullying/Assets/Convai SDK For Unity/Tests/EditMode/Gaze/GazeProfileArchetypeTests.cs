using System.Reflection;
using Convai.Domain.Embodiment.Semantics;
using Convai.Modules.Gaze.Editor;
using Convai.Modules.Gaze.Data;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Convai.Tests.EditMode.Gaze
{
    /// <summary>
    ///     E11 gaze profile archetypes: applying any personality fills a valid, complete state
    ///     table, the personalities differ in the expected ways, and applying is undoable.
    /// </summary>
    public sealed class GazeProfileArchetypeTests
    {
        private static ConvaiGazeProfile NewProfile() => ScriptableObject.CreateInstance<ConvaiGazeProfile>();

        private static void Apply(ConvaiGazeProfile profile, GazeProfileArchetypes.GazeArchetype archetype)
        {
            var serialized = new SerializedObject(profile);
            serialized.Update();
            GazeProfileArchetypes.Apply(serialized, archetype);
            serialized.ApplyModifiedProperties();
        }

        [Test]
        public void EveryArchetype_AppliesAValidCompleteStateTable()
        {
            foreach (GazeProfileArchetypes.GazeArchetype archetype in GazeProfileArchetypes.All)
            {
                ConvaiGazeProfile profile = NewProfile();
                try
                {
                    Apply(profile, archetype);

                    // OnValidate must accept the authored values unchanged (in range by construction).
                    float speakingBefore = profile.GetStatePolicy(DialogueState.Speaking).Engagement;
                    InvokeOnValidate(profile);
                    Assert.That(profile.GetStatePolicy(DialogueState.Speaking).Engagement,
                        Is.EqualTo(speakingBefore).Within(1e-4f),
                        $"{archetype.Name}: OnValidate must not have to clamp authored values.");

                    Assert.That(profile.StatePolicies.Count, Is.EqualTo(8),
                        $"{archetype.Name}: every dialogue state must be authored.");
                    foreach (DialogueState state in System.Enum.GetValues(typeof(DialogueState)))
                    {
                        GazeStatePolicy policy = profile.GetStatePolicy(state);
                        Assert.That(policy.State, Is.EqualTo(state),
                            $"{archetype.Name}: {state} must resolve to its own entry.");
                        Assert.That(policy.Engagement, Is.InRange(0f, 1f));
                        Assert.That(policy.HeadContribution, Is.InRange(0f, 1f));
                    }
                }
                finally
                {
                    Object.DestroyImmediate(profile);
                }
            }
        }

        [Test]
        public void Archetypes_DifferInSignatureWays()
        {
            ConvaiGazeProfile confident = NewProfile();
            ConvaiGazeProfile shy = NewProfile();
            ConvaiGazeProfile stoic = NewProfile();
            try
            {
                Apply(confident, Find("Confident"));
                Apply(shy, Find("Shy"));
                Apply(stoic, Find("Stoic"));

                Assert.That(
                    shy.GetStatePolicy(DialogueState.Speaking).Engagement,
                    Is.LessThan(confident.GetStatePolicy(DialogueState.Speaking).Engagement),
                    "Shy must commit less than Confident while speaking.");

                Assert.That(
                    shy.GetStatePolicy(DialogueState.Listening).AversionStrength,
                    Is.GreaterThan(confident.GetStatePolicy(DialogueState.Listening).AversionStrength),
                    "Shy must break contact more than Confident while listening.");

                Assert.That(
                    stoic.GetStatePolicy(DialogueState.Listening).HeadContribution,
                    Is.LessThan(confident.GetStatePolicy(DialogueState.Listening).HeadContribution),
                    "Stoic keeps a steadier head (less head recruitment) than Confident.");
            }
            finally
            {
                Object.DestroyImmediate(confident);
                Object.DestroyImmediate(shy);
                Object.DestroyImmediate(stoic);
            }
        }

        [Test]
        public void ArchetypeOrderAndPlanningPropensity_AreGolden()
        {
            // "Default" leads the row: it mirrors the profile's own field initializers, so a fresh
            // profile always shows one pill active and "put it back" is one click. Its parity with
            // ConvaiGazeProfile is pinned separately by GazeProfileDefaultParityTests.
            string[] names = { "Default", "Confident", "Warm", "Shy", "Stoic", "Attentive" };
            float[] probabilities = { 0.7f, 0.35f, 0.65f, 0.8f, 0.2f, 0.45f };
            Assert.That(GazeProfileArchetypes.All.Length, Is.EqualTo(names.Length));

            for (int i = 0; i < names.Length; i++)
            {
                GazeProfileArchetypes.GazeArchetype archetype = GazeProfileArchetypes.All[i];
                Assert.That(archetype.Name, Is.EqualTo(names[i]), $"toolbar index {i}");
                Assert.That(archetype.PlanningBreakProbability,
                    Is.EqualTo(probabilities[i]).Within(0.0001f), archetype.Name);

                ConvaiGazeProfile profile = NewProfile();
                try
                {
                    Apply(profile, archetype);
                    Assert.That(profile.PlanningBreakProbability,
                        Is.EqualTo(probabilities[i]).Within(0.0001f), archetype.Name);
                }
                finally
                {
                    Object.DestroyImmediate(profile);
                }
            }

            Assert.That(Find("Stoic").PlanningBreakProbability,
                Is.LessThan(Find("Confident").PlanningBreakProbability));
            Assert.That(Find("Confident").PlanningBreakProbability,
                Is.LessThan(Find("Warm").PlanningBreakProbability));
            Assert.That(Find("Warm").PlanningBreakProbability,
                Is.LessThan(Find("Shy").PlanningBreakProbability));
        }

        [Test]
        public void ApplyingArchetype_IsUndoable()
        {
            ConvaiGazeProfile profile = NewProfile();
            try
            {
                Apply(profile, Find("Confident"));
                float confidentSpeaking = profile.GetStatePolicy(DialogueState.Speaking).Engagement;

                Apply(profile, Find("Shy"));
                float shySpeaking = profile.GetStatePolicy(DialogueState.Speaking).Engagement;
                Assert.That(shySpeaking, Is.Not.EqualTo(confidentSpeaking).Within(1e-4f),
                    "Sanity: the second archetype must actually change the table.");

                Undo.PerformUndo();

                Assert.That(profile.GetStatePolicy(DialogueState.Speaking).Engagement,
                    Is.EqualTo(confidentSpeaking).Within(1e-4f),
                    "Undo must restore the previously applied archetype's table.");
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        private static GazeProfileArchetypes.GazeArchetype Find(string name)
        {
            foreach (GazeProfileArchetypes.GazeArchetype archetype in GazeProfileArchetypes.All)
                if (archetype.Name == name)
                    return archetype;
            Assert.Fail($"Archetype '{name}' not found.");
            return null;
        }

        private static void InvokeOnValidate(ConvaiGazeProfile profile)
        {
            MethodInfo method = typeof(ConvaiGazeProfile).GetMethod(
                "OnValidate", BindingFlags.Instance | BindingFlags.NonPublic);
            method?.Invoke(profile, null);
        }
    }
}
