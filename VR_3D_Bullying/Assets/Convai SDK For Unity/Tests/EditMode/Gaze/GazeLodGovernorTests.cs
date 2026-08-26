using System.Reflection;
using Convai.Modules.Gaze.Core;
using Convai.Modules.Gaze.Data;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Convai.Tests.EditMode.Gaze
{
    /// <summary>
    ///     E10 crowd LOD governor: the distance-band cognition-rate table with hysteresis, the
    ///     exact accumulation of skipped-tick delta time, and the off-screen expression-skip
    ///     signal that clears on the first visible tick.
    /// </summary>
    public sealed class GazeLodGovernorTests
    {
        private ConvaiGazeProfile _profile;
        private GazeLodGovernor _governor;

        [SetUp]
        public void SetUp()
        {
            _profile = ConvaiGazeProfile.CreateDefault();
            SetProfileSetting(_profile, "enableGazeLod", true);
            SetProfileSetting(_profile, "lodFarDistance", 12f);
            SetProfileSetting(_profile, "lodFarCognitionHz", 10f);
            SetProfileSetting(_profile, "skipWhenInvisible", true);
            _governor = new GazeLodGovernor();
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_profile);

        private bool Tick(float distance, bool visible, float dt, out float cognitionDt, out bool skipExpression) =>
            _governor.TickCognition(_profile, distance, visible, dt, out cognitionDt, out skipExpression);

        [Test]
        public void NearBand_RunsEveryTick()
        {
            for (int i = 0; i < 5; i++)
            {
                bool run = Tick(5f, true, 1f / 60f, out _, out _);
                Assert.IsTrue(run, "Inside the near band the character must think every tick.");
            }
            Assert.IsFalse(_governor.IsFar);
        }

        [Test]
        public void FarBand_UsesHysteresisBothDirections()
        {
            // Just inside the band from the near side: must NOT flip to far yet.
            Tick(12.2f, true, 1f / 60f, out _, out _);
            Assert.IsFalse(_governor.IsFar, "12.2 m must not enter the far band (needs > 12.5).");

            // Clearly beyond the enter threshold: flips to far.
            Tick(13f, true, 1f / 60f, out _, out _);
            Assert.IsTrue(_governor.IsFar, "13 m must enter the far band.");

            // Back inside the band from the far side: must STAY far (needs < 11.5 to exit).
            Tick(12.2f, true, 1f / 60f, out _, out _);
            Assert.IsTrue(_governor.IsFar, "12.2 m must not exit the far band once entered.");

            // Below the exit threshold: returns to near.
            Tick(11f, true, 1f / 60f, out _, out _);
            Assert.IsFalse(_governor.IsFar, "11 m must exit the far band.");
        }

        [Test]
        public void FarBand_AccumulatesSkippedDeltaTimeExactly()
        {
            const float dt = 0.02f; // interval at 10 Hz is 0.10 s → exactly 5 ticks

            // Four skipped ticks (0.02, 0.04, 0.06, 0.08), all below the 0.10 s interval.
            for (int i = 0; i < 4; i++)
            {
                bool run = Tick(20f, true, dt, out _, out _);
                Assert.IsFalse(run, $"Tick {i} in the far band must be skipped below the interval.");
            }

            // The fifth reaches 0.10 s and runs with the full accumulated delta time.
            bool executed = Tick(20f, true, dt, out float cognitionDt, out _);
            Assert.IsTrue(executed, "The fifth far tick must execute.");
            Assert.That(cognitionDt, Is.EqualTo(0.10f).Within(1e-4f),
                "The executed tick must carry the exact sum of the skipped deltas.");
        }

        [Test]
        public void OffScreen_SkipsExpression_AndResumesOnFirstVisibleTick()
        {
            Tick(5f, false, 1f / 60f, out _, out bool skipWhileHidden);
            Assert.IsTrue(skipWhileHidden, "With no renderer visible the solver stage must be skipped.");

            bool run = Tick(5f, true, 1f / 60f, out _, out bool skipWhenVisible);
            Assert.IsTrue(run, "The first visible tick still runs cognition (near band).");
            Assert.IsFalse(skipWhenVisible,
                "Becoming visible must clear the skip so a solve runs on the first visible frame.");
        }

        [Test]
        public void Disabled_AlwaysRunsAndPassesDeltaThrough()
        {
            SetProfileSetting(_profile, "enableGazeLod", false);

            bool run = Tick(100f, false, 0.033f, out float cognitionDt, out bool skipExpression);
            Assert.IsTrue(run, "With LOD off the character always thinks.");
            Assert.IsFalse(skipExpression, "With LOD off the solver stage is never skipped.");
            Assert.That(cognitionDt, Is.EqualTo(0.033f).Within(1e-5f), "Delta time passes through untouched.");
        }

        /// <summary>
        ///     Writes one profile setting by name. The profile groups its settings into nested
        ///     blocks, so the field lives on the block rather than on the profile itself — which
        ///     block is the profile's business, not a test's.
        /// </summary>
        private static void SetProfileSetting(ConvaiGazeProfile profile, string settingName, object value)
        {
            const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;

            foreach (FieldInfo block in typeof(ConvaiGazeProfile).GetFields(Flags))
            {
                FieldInfo setting = block.FieldType.GetField(settingName, Flags);
                if (setting == null) continue;

                setting.SetValue(block.GetValue(profile), value);
                return;
            }

            Assert.Fail($"ConvaiGazeProfile has no setting named {settingName}.");
        }
    }
}
