using NUnit.Framework;
using UnityEngine;

namespace Shardstep.Tests
{
    public sealed class ShardstepPortraitTouchPolicyTests
    {
        [Test]
        public void CancelDistanceScalesWithinMobileBounds()
        {
            float phone = ShardstepPortraitTouchPolicy.CancelDistance(390, 844);
            float tablet = ShardstepPortraitTouchPolicy.CancelDistance(1024, 1366);

            Assert.That(phone, Is.GreaterThanOrEqualTo(ShardstepPortraitTouchPolicy.MinimumCancelDistance));
            Assert.That(tablet, Is.GreaterThan(phone));
            Assert.That(tablet, Is.LessThanOrEqualTo(ShardstepPortraitTouchPolicy.MaximumCancelDistance));
        }

        [Test]
        public void SmallFingerDriftCommitsMove()
        {
            Assert.That(
                ShardstepPortraitTouchPolicy.ShouldCommit(
                    new Vector2(100f, 100f), new Vector2(120f, 118f), 390, 844),
                Is.True);
        }

        [Test]
        public void LargeFingerDriftCancelsMove()
        {
            Assert.That(
                ShardstepPortraitTouchPolicy.ShouldCommit(
                    new Vector2(100f, 100f), new Vector2(220f, 100f), 390, 844),
                Is.False);
        }
    }
}
