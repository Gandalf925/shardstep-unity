using NUnit.Framework;
using UnityEngine;

namespace Shardstep.Tests
{
    public sealed class ChronosphereMovePolicyTests
    {
        [Test]
        public void RemoteTargetsOnlyChooseDirection()
        {
            Vector3 origin = new Vector3(1f, 0.9f, -2f);
            Vector3 nearTarget = origin + new Vector3(2f, 0f, 1f);
            Vector3 farTarget = origin + new Vector3(200f, 0f, 100f);

            Assert.That(ChronosphereMovePolicy.TryResolveNominalStep(
                origin,
                nearTarget,
                out Vector3 nearDirection,
                out Vector3 nearDestination), Is.True);
            Assert.That(ChronosphereMovePolicy.TryResolveNominalStep(
                origin,
                farTarget,
                out Vector3 farDirection,
                out Vector3 farDestination), Is.True);

            Assert.That(Vector3.Distance(origin, nearDestination),
                Is.EqualTo(ChronosphereMovePolicy.StandardStepDistance).Within(0.001f));
            Assert.That(Vector3.Distance(origin, farDestination),
                Is.EqualTo(ChronosphereMovePolicy.StandardStepDistance).Within(0.001f));
            Assert.That(Vector3.Angle(nearDirection, farDirection), Is.LessThan(0.01f));
        }

        [Test]
        public void VerticalDifferenceDoesNotChangeStepDirection()
        {
            Vector3 origin = new Vector3(0f, 1f, 0f);
            Vector3 indicated = new Vector3(3f, 40f, 4f);

            Assert.That(ChronosphereMovePolicy.TryResolveNominalStep(
                origin,
                indicated,
                out Vector3 direction,
                out Vector3 destination), Is.True);

            Assert.That(direction.y, Is.EqualTo(0f).Within(0.001f));
            Assert.That(destination.y, Is.EqualTo(origin.y).Within(0.001f));
            Assert.That(Vector3.Distance(origin, destination),
                Is.EqualTo(ChronosphereMovePolicy.StandardStepDistance).Within(0.001f));
        }

        [Test]
        public void TouchingTooCloseDoesNotCommitAccidentalStep()
        {
            Vector3 origin = Vector3.zero;
            Vector3 indicated = new Vector3(
                ChronosphereMovePolicy.MinimumDirectionDistance * 0.25f,
                0f,
                0f);

            Assert.That(ChronosphereMovePolicy.TryResolveNominalStep(
                origin,
                indicated,
                out _,
                out _), Is.False);
        }
    }
}
