using Convai.Modules.Gaze.Core.Shift;
using NUnit.Framework;
using UnityEngine;

namespace Convai.Tests.EditMode.Gaze
{
    /// <summary>
    ///     How a held pose turns into pressure to change it: the part of the model that stops a
    ///     settled gaze from freezing.
    /// </summary>
    public sealed class GazeComfortModelTests
    {
        private const float Dt = 1f / 60f;
        private const float EyeComfort = 14f;
        private const float HeadComfort = 35f;

        private GazeComfortModel _model;

        [SetUp]
        public void SetUp() => _model = new GazeComfortModel();

        private void Hold(float seconds, float eyeEccentricity, float headYaw, bool engaged = true)
        {
            int steps = Mathf.CeilToInt(seconds / Dt);
            for (int i = 0; i < steps; i++)
                _model.Tick(eyeEccentricity, headYaw, EyeComfort, HeadComfort, engaged, Dt);
        }

        [Test]
        public void AComfortablePose_NeverBuildsPressure()
        {
            Hold(5f, eyeEccentricity: 6f, headYaw: 20f);

            Assert.That(_model.OrbitPressure, Is.EqualTo(0f));
            Assert.That(_model.ComfortPressure, Is.EqualTo(0f));
        }

        [Test]
        public void EyesHeldOffCentre_BuildOrbitPressure()
        {
            Hold(2f, eyeEccentricity: 25f, headYaw: 10f);

            Assert.That(_model.OrbitPressure, Is.EqualTo(1f).Within(0.01f));
            Assert.That(_model.ComfortPressure, Is.EqualTo(0f),
                "A comfortable neck must not accumulate pressure just because the eyes are working.");
        }

        [Test]
        public void AHeldNeckTurn_BuildsComfortPressure()
        {
            Hold(2f, eyeEccentricity: 2f, headYaw: 50f);

            Assert.That(_model.ComfortPressure, Is.EqualTo(1f).Within(0.01f));
            Assert.That(_model.OrbitPressure, Is.EqualTo(0f));
        }

        [Test]
        public void PressureBuildsSlowly_SoAGlanceNeverTriggersIt()
        {
            Hold(0.3f, eyeEccentricity: 30f, headYaw: 50f);

            Assert.That(_model.OrbitPressure, Is.LessThan(0.3f));
            Assert.That(_model.ComfortPressure, Is.LessThan(0.3f),
                "Discomfort accumulates over seconds; a quick glance out of the corner of the " +
                "eye must not read as strain.");
        }

        [Test]
        public void PressureReleasesFasterThanItBuilds()
        {
            Hold(2f, eyeEccentricity: 30f, headYaw: 50f);
            Assert.That(_model.OrbitPressure, Is.EqualTo(1f).Within(0.01f));

            Hold(0.4f, eyeEccentricity: 0f, headYaw: 0f);

            Assert.That(_model.OrbitPressure, Is.EqualTo(0f),
                "Relief is immediate. A pressure that decayed as slowly as it grew would keep " +
                "asking for a turn that has already happened.");
            Assert.That(_model.ComfortPressure, Is.EqualTo(0f));
        }

        [Test]
        public void Disengaged_PressureDecaysRatherThanBuilding()
        {
            Hold(3f, eyeEccentricity: 30f, headYaw: 50f, engaged: false);

            Assert.That(_model.OrbitPressure, Is.EqualTo(0f),
                "A character looking at nothing is not straining to hold anything.");
            Assert.That(_model.ComfortPressure, Is.EqualTo(0f));
        }

        /// <summary>
        ///     The strain test must not chatter when the pose parks on the comfort angle.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Orbit return is a closed loop: pressure recruits the head, the head takes more
        ///         of the shift, and the eyes — whose eccentricity is what the test measures —
        ///         come back in. A bare threshold in that loop does not settle, it oscillates,
        ///         and the asymmetric rates make it worse rather than better: the pressure sheds
        ///         nearly five times faster than it built, so the head gives the share back much
        ///         faster than it took it and the eyes go straight back out. On screen the
        ///         character's head hunts back and forth for as long as it is talking to someone
        ///         at an angle.
        ///     </para>
        ///     <para>
        ///         Modelled here the way the loop actually behaves: eccentricity high while the
        ///         pressure is low, low once the pressure has recruited the head. Without the
        ///         relief band the pressure reverses every time it crosses; with it, it holds.
        ///     </para>
        /// </remarks>
        [Test]
        public void PressureDoesNotChatterWhenThePoseParksOnTheComfortAngle()
        {
            // Build to full pressure on a strained pose.
            Hold(2f, eyeEccentricity: EyeComfort + 6f, headYaw: 10f);
            Assert.That(_model.OrbitPressure, Is.EqualTo(1f).Within(0.01f));

            // The head has now taken over, so the eyes sit just inside the comfort angle — the
            // exact place the loop parks. A bare threshold releases here and the whole cycle
            // restarts; the relief band must hold the strain latched instead.
            Hold(1f, eyeEccentricity: EyeComfort - 1f, headYaw: 10f);

            Assert.That(_model.OrbitPressure, Is.GreaterThan(0.9f),
                "Pressure collapsed the moment the pose came back inside the comfort angle it " +
                "had just been recruited to fix. Release must require coming back well inside " +
                "it (GazeComfortModel.ReliefFraction), or the pressure and the pose it moves " +
                "form a limit cycle and the head hunts.");

            // Genuinely relieved — well inside the band — must still release, and quickly.
            Hold(0.4f, eyeEccentricity: 2f, headYaw: 10f);

            Assert.That(_model.OrbitPressure, Is.EqualTo(0f),
                "Hysteresis must not turn into a latch: a pose that is comfortable by any " +
                "measure still sheds its pressure at the release rate.");
        }

        [Test]
        public void ComfortAngleOfZero_DisablesThatPressureEntirely()
        {
            for (int i = 0; i < 300; i++)
                _model.Tick(30f, 50f, eyeComfortDegrees: 0f, headComfortYawDegrees: 0f,
                    engaged: true, deltaTime: Dt);

            Assert.That(_model.OrbitPressure, Is.EqualTo(0f));
            Assert.That(_model.ComfortPressure, Is.EqualTo(0f),
                "Zero is the off switch, not a comfort angle of zero degrees that everything exceeds.");
        }
    }
}
