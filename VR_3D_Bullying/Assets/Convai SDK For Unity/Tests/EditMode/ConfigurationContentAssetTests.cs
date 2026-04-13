using Convai.Editor.ConfigurationWindow.Content;
using Convai.Editor.Inspectors;
using Convai.Runtime.Components;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Convai.Tests.EditMode
{
    public class ConfigurationContentAssetTests
    {
        [Test]
        public void ConfigurationContent_InstanceHasDefaultWelcomeText()
        {
            var content = ConvaiConfigurationContent.Instance;

            Assert.IsNotNull(content);
            Assert.IsNotEmpty(content.WelcomeHeader);
            Assert.IsNotEmpty(content.WelcomeSubheader);
            Assert.IsNotEmpty(content.QuickStartInstructions);
        }

        [Test]
        public void ReleaseNotesAsset_InstanceHasAtLeastOneEntry()
        {
            var notes = ConvaiReleaseNotesAsset.Instance;

            Assert.IsNotNull(notes);
            Assert.IsNotNull(notes.Entries);
            Assert.Greater(notes.Entries.Count, 0);
            Assert.IsNotEmpty(notes.Entries[0].Version);
        }

        [Test]
        public void ConvaiCharacterInspector_ContainsSessionResumeToggle()
        {
            GameObject go = new("TestCharacter");
            try
            {
                go.AddComponent<ConvaiCharacter>();
                var editor = (ConvaiCharacterEditor)UnityEditor.Editor.CreateEditor(go.GetComponent<ConvaiCharacter>(),
                    typeof(ConvaiCharacterEditor));

                try
                {
                    VisualElement inspector = editor.CreateInspectorGUI();
                    var toggle = inspector.Q<Toggle>("enable-session-resume-toggle");

                    Assert.IsNotNull(toggle, "ConvaiCharacter inspector should expose the session resume toggle.");
                }
                finally
                {
                    Object.DestroyImmediate(editor);
                }
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
