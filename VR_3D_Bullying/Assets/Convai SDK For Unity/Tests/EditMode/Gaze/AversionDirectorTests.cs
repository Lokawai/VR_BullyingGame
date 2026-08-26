using Convai.Modules.Gaze.Core.Behaviors;
using Convai.Modules.Gaze.Data;
using Convai.Runtime.Embodiment;
using NUnit.Framework;
using UnityEngine;

namespace Convai.Tests.EditMode.Gaze
{
    public sealed class AversionDirectorTests
    {
        private const float Dt = 1f / 60f;

        private AversionDirector _director;
        private DeterministicEmbodimentRandom _random;

        [SetUp]
        public void SetUp()
        {
            _director = new AversionDirector();
            _random = new DeterministicEmbodimentRandom(77u);
        }

        private (int beats, float maxOffset) Run(
            GazeAversionMode mode, float strength, float seconds, bool engaged = true,
            GazeAversionBias bias = GazeAversionBias.CognitiveDefault)
        {
            int beats = 0;
            bool wasAverting = false;
            float maxOffset = 0f;

            int steps = Mathf.CeilToInt(seconds / Dt);
            for (int i = 0; i < steps; i++)
            {
                _director.Tick(mode, strength, bias, engaged, Dt, ref _random);
                if (_director.IsAverting && !wasAverting) beats++;
                wasAverting = _director.IsAverting;
                maxOffset = Mathf.Max(maxOffset, _director.Offset.magnitude);
            }

            return (beats, maxOffset);
        }

        [Test]
        public void CognitiveAversion_ProducesLookAwayBeats()
        {
            (int beats, float maxOffset) = Run(GazeAversionMode.Cognitive, 0.7f, 30f);

            Assert.That(beats, Is.GreaterThanOrEqualTo(3), "Thinking must break contact in beats.");
            Assert.That(maxOffset, Is.GreaterThan(8f), "Cognitive look-aways are visible, not micro.");
        }

        [Test]
        public void CognitiveBeats_BiasUpward()
        {
            bool sawUpwardBeat = false;
            for (int i = 0; i < 3600; i++)
            {
                _director.Tick(GazeAversionMode.Cognitive, 0.8f, GazeAversionBias.CognitiveDefault, true, Dt, ref _random);
                if (_director.IsAverting && _director.Offset.y > 4f) sawUpwardBeat = true;
            }
            Assert.IsTrue(sawUpwardBeat, "Cognitive aversion looks up ('recalling') at least sometimes.");
        }

        [Test]
        public void NoneMode_KeepsUnbrokenContact()
        {
            (int beats, float maxOffset) = Run(GazeAversionMode.None, 1f, 20f);

            Assert.That(beats, Is.EqualTo(0));
            Assert.That(maxOffset, Is.EqualTo(0f), "Speaking default: full lock, no breaks.");
        }

        [Test]
        public void ZeroStrength_KeepsUnbrokenContact()
        {
            (int beats, _) = Run(GazeAversionMode.Natural, 0f, 20f);
            Assert.That(beats, Is.EqualTo(0));
        }

        [Test]
        public void Disengaged_EasesOffsetBackToZero()
        {
            Run(GazeAversionMode.Cognitive, 1f, 6f);

            for (int i = 0; i < 120; i++)
                _director.Tick(GazeAversionMode.Cognitive, 1f, GazeAversionBias.CognitiveDefault, engaged: false, Dt, ref _random);

            Assert.That(_director.Offset.magnitude, Is.LessThan(0.1f));
            Assert.IsFalse(_director.IsAverting);
        }

        [Test]
        public void StrengthScale_ChangesBeatFrequency()
        {
            (int weakBeats, _) = Run(GazeAversionMode.Cognitive, 0.15f, 40f);

            _director.Reset();
            _random = new DeterministicEmbodimentRandom(77u);
            (int strongBeats, _) = Run(GazeAversionMode.Cognitive, 1f, 40f);

            Assert.That(strongBeats, Is.GreaterThan(weakBeats),
                "Higher strength must break contact more often.");
        }

        // ── Emotional gaze signature: AversionBias ─────────────────────────

        [Test]
        public void CognitiveDefaultBias_IsEnumZero_SoOlderSerializedRowsStayCompatible()
        {
            // EmotionGazeModifier rows serialized before AversionBias existed have no value for it;
            // Unity default-inits missing serialized enum fields to 0, which must resolve to
            // the legacy mode-based direction pick, not a new biased shape.
            Assert.That((int)GazeAversionBias.CognitiveDefault, Is.EqualTo(0));
        }

        [Test]
        public void UpBias_ProducesUpwardBeatsOnly()
        {
            bool sawBeat = false;
            for (int i = 0; i < 3600; i++)
            {
                _director.Tick(GazeAversionMode.Natural, 0.8f, GazeAversionBias.Up, true, Dt, ref _random);
                if (_director.IsAverting)
                {
                    sawBeat = true;
                    Assert.That(_director.Offset.y, Is.GreaterThan(0f), "Up bias must pitch upward only.");
                }
            }
            Assert.IsTrue(sawBeat, "Expected at least one aversion beat.");
        }

        [Test]
        public void DownBias_ProducesDownwardBeatsOnly()
        {
            bool sawBeat = false;
            for (int i = 0; i < 3600; i++)
            {
                _director.Tick(GazeAversionMode.Natural, 0.8f, GazeAversionBias.Down, true, Dt, ref _random);
                if (_director.IsAverting)
                {
                    sawBeat = true;
                    Assert.That(_director.Offset.y, Is.LessThan(0f), "Down bias must pitch downward only.");
                }
            }
            Assert.IsTrue(sawBeat, "Expected at least one aversion beat.");
        }

        [Test]
        public void SideBias_ProducesLevelSidewaysBeats()
        {
            bool sawBeat = false;
            for (int i = 0; i < 3600; i++)
            {
                _director.Tick(GazeAversionMode.Cognitive, 0.8f, GazeAversionBias.Side, true, Dt, ref _random);
                if (_director.IsAverting)
                {
                    sawBeat = true;
                    Assert.That(Mathf.Abs(_director.Offset.y), Is.LessThanOrEqualTo(2f),
                        "Side bias keeps the beat level (small pitch only).");
                    // The beat target (EyeOffset), not the eased head ramp (Offset), carries the
                    // shape contract: the ramp passes through arbitrarily small magnitudes.
                    Assert.That(Mathf.Abs(_director.EyeOffset.x), Is.GreaterThan(2f),
                        "Side bias must produce meaningful yaw displacement.");
                }
            }
            Assert.IsTrue(sawBeat, "Expected at least one aversion beat.");
        }

        [Test]
        public void DownSideBias_ProducesDownwardAndSidewaysBeats()
        {
            bool sawBeat = false;
            for (int i = 0; i < 3600; i++)
            {
                _director.Tick(GazeAversionMode.Natural, 0.8f, GazeAversionBias.DownSide, true, Dt, ref _random);
                if (_director.IsAverting)
                {
                    sawBeat = true;
                    Assert.That(_director.Offset.y, Is.LessThan(0f), "DownSide bias must pitch downward.");
                    // The beat target (EyeOffset) carries the shape contract; the eased head
                    // ramp (Offset) passes through arbitrarily small x while it climbs.
                    Assert.That(Mathf.Abs(_director.EyeOffset.x), Is.GreaterThan(2f),
                        "DownSide bias must also carry a side component.");
                }
            }
            Assert.IsTrue(sawBeat, "Expected at least one aversion beat.");
        }

        [Test]
        public void CognitiveDefaultBias_ForceCognitiveBeatShapeIsUnaffectedByBias()
        {
            // ForceCognitiveBeat (turn-taking planning break) never takes a bias parameter — it
            // always samples the classic up/side cognitive shape regardless of what bias the
            // caller would otherwise be feeding Tick(), matching the plan's "a planning break is
            // cognitive, not emotional" rule.
            _director.ForceCognitiveBeat(1.5f, 0.9f, ref _random);

            Assert.IsTrue(_director.IsAverting);
            Assert.That(_director.EyeOffset.y, Is.GreaterThan(0f),
                "Forced planning-break beats keep the cognitive up shape.");
        }

        [Test]
        public void CognitiveDefaultBias_IsDeterministic()
        {
            var randomA = new DeterministicEmbodimentRandom(555u);
            var randomB = new DeterministicEmbodimentRandom(555u);
            var directorA = new AversionDirector();
            var directorB = new AversionDirector();

            for (int i = 0; i < 300; i++)
            {
                directorA.Tick(GazeAversionMode.Cognitive, 0.8f, GazeAversionBias.CognitiveDefault, true, Dt, ref randomA);
                directorB.Tick(GazeAversionMode.Cognitive, 0.8f, GazeAversionBias.CognitiveDefault, true, Dt, ref randomB);

                Assert.That(directorA.Offset.x, Is.EqualTo(directorB.Offset.x));
                Assert.That(directorA.Offset.y, Is.EqualTo(directorB.Offset.y));
                Assert.That(directorA.IsAverting, Is.EqualTo(directorB.IsAverting));
            }
        }
    }
}
