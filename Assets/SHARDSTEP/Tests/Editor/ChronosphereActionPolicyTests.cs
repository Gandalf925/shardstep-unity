using NUnit.Framework;

namespace Shardstep.Tests
{
    public sealed class ChronosphereActionPolicyTests
    {
        [Test]
        public void EveryCommittedActionUsesOneSharedDuration()
        {
            Assert.That(
                ShardstepClock.StandardActionDuration,
                Is.EqualTo(0.18f).Within(0.001f));
        }

        [Test]
        public void FrozenProjectilesAdvancePredictableDistancePerAction()
        {
            float playerDistance =
                ChronosphereProjectilePolicy.DistancePerAction(
                    PlayerCombat.RailProjectileSpeed);
            float enemyDistance =
                ChronosphereProjectilePolicy.DistancePerAction(
                    EnemyAgent.GunnerProjectileSpeed);

            Assert.That(playerDistance, Is.EqualTo(4.32f).Within(0.001f));
            Assert.That(enemyDistance, Is.EqualTo(2.25f).Within(0.001f));
            Assert.That(playerDistance, Is.GreaterThan(enemyDistance));
        }

        [Test]
        public void RailMagazineRequiresExplicitReloadAfterFourShots()
        {
            Assert.That(PlayerCombat.RailMagazineSize, Is.EqualTo(4));
        }
    }
}
