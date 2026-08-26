using Convai.Domain.Embodiment.Semantics;
using Convai.Modules.Gaze.Data;
using Convai.Modules.Gaze.Editor;
using NUnit.Framework;
using System.Collections.Generic;
using System.Reflection;
using System;
using UnityEditor;
using Object = UnityEngine.Object;

namespace Convai.Tests.EditMode.Gaze
{
    /// <summary>
    ///     The state table editor draws states rather than array elements, which means a state the
    ///     editor does not know about becomes invisible in the UI while still applying at runtime.
    ///     These tests make adding a <see cref="DialogueState" /> without updating the editor a
    ///     build failure rather than a silent gap.
    /// </summary>
    internal sealed class GazeStatePolicyTableTests
    {
        private static DialogueState[] EditorOrder =>
            (DialogueState[])typeof(GazeStatePolicyTable)
                .GetField("Order", BindingFlags.Static | BindingFlags.NonPublic)!
                .GetValue(null);

        private static Dictionary<DialogueState, string> Explanations =>
            (Dictionary<DialogueState, string>)typeof(GazeStatePolicyTable)
                .GetField("Explanations", BindingFlags.Static | BindingFlags.NonPublic)!
                .GetValue(null);

        [Test]
        public void EveryDialogueState_HasARowInTheEditor()
        {
            var listed = new HashSet<DialogueState>(EditorOrder);
            var missing = new List<string>();

            foreach (DialogueState state in Enum.GetValues(typeof(DialogueState)))
                if (!listed.Contains(state))
                    missing.Add(state.ToString());

            Assert.IsEmpty(missing,
                "These conversation states are not drawn by the state table, so a profile authoring " +
                "them would apply behaviour the user cannot see or edit:\n  " +
                string.Join("\n  ", missing));
        }

        [Test]
        public void EveryDrawnState_HasAnExplanationAndAFriendlyName()
        {
            Dictionary<DialogueState, string> explanations = Explanations;

            foreach (DialogueState state in EditorOrder)
            {
                Assert.IsTrue(explanations.ContainsKey(state),
                    $"{state} has no plain-English explanation, so its row is a bare enum name.");
                Assert.IsNotEmpty(explanations[state]);

                string friendly = GazeStatePolicyTable.FriendlyName(state);
                Assert.IsNotEmpty(friendly);
            }
        }

        [Test]
        public void FriendlyNames_TranslateTheInternalVocabulary()
        {
            // "Attending" and "Settling" are the module's words, not a user's.
            Assert.AreEqual("Addressed", GazeStatePolicyTable.FriendlyName(DialogueState.Attending));
            Assert.AreEqual("Winding down", GazeStatePolicyTable.FriendlyName(DialogueState.Settling));
        }

        [Test]
        public void EditorOrder_HasNoDuplicates()
        {
            var seen = new HashSet<DialogueState>();
            foreach (DialogueState state in EditorOrder)
                Assert.IsTrue(seen.Add(state), $"{state} is listed twice, so it would draw two rows.");
        }

        [Test]
        public void IndexOf_FindsAuthoredStatesAndReportsMissingOnes()
        {
            ConvaiGazeProfile profile = ConvaiGazeProfile.CreateDefault();
            try
            {
                var serialized = new SerializedObject(profile);
                SerializedProperty policies = GazeProfileSerializedPaths.Find(serialized, "statePolicies");

                Assert.That(GazeStatePolicyTable.IndexOf(policies, DialogueState.Idle), Is.GreaterThanOrEqualTo(0),
                    "The shipped profile authors Idle, which is the fallback for everything else.");

                // Remove every row and confirm the lookup reports absence rather than throwing.
                policies.ClearArray();
                serialized.ApplyModifiedProperties();
                Assert.AreEqual(-1, GazeStatePolicyTable.IndexOf(policies, DialogueState.Idle));
                Assert.AreEqual(-1, GazeStatePolicyTable.IndexOf(null, DialogueState.Idle));
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }
    }
}
