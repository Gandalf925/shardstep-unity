using NUnit.Framework;

namespace Shardstep.Tests
{
    public sealed class ShardstepMobilePolicyTests
    {
        [Test]
        public void LandscapeAcceptsEqualOrWiderViewport()
        {
            Assert.That(ShardstepMobilePolicy.IsLandscape(844, 390), Is.True);
            Assert.That(ShardstepMobilePolicy.IsLandscape(500, 500), Is.True);
            Assert.That(ShardstepMobilePolicy.IsLandscape(390, 844), Is.False);
        }

        [Test]
        public void ControlRadiusScalesWithShortestViewportSide()
        {
            float phone = ShardstepMobilePolicy.ControlRadius(844, 390);
            float tablet = ShardstepMobilePolicy.ControlRadius(1366, 1024);

            Assert.That(phone, Is.GreaterThanOrEqualTo(ShardstepMobilePolicy.MinimumControlRadius));
            Assert.That(tablet, Is.GreaterThan(phone));
            Assert.That(tablet, Is.LessThanOrEqualTo(ShardstepMobilePolicy.MaximumControlRadius));
        }

        [Test]
        public void AimReleaseThresholdTracksControlRadius()
        {
            float radius = ShardstepMobilePolicy.ControlRadius(844, 390);
            float threshold = ShardstepMobilePolicy.AimReleaseThreshold(844, 390);

            Assert.That(threshold, Is.EqualTo(radius * 0.3f).Within(0.001f));
        }
    }
}
