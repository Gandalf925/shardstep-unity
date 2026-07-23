using NUnit.Framework;

namespace Shardstep.Tests
{
    public sealed class ShardstepClockTests
    {
        [Test]
        public void AimingAlwaysFreezesTheWorld()
        {
            Assert.That(ShardstepClock.ComputeTimeScale(true, true, 1f), Is.EqualTo(0f));
        }

        [Test]
        public void ExecutionAdvancesAtFullSpeed()
        {
            Assert.That(ShardstepClock.ComputeTimeScale(false, true, 0f), Is.EqualTo(1f));
        }

        [Test]
        public void ReleasedControlsFreezeTheWorld()
        {
            Assert.That(ShardstepClock.ComputeTimeScale(false, false, 0f), Is.EqualTo(0f));
        }

        [Test]
        public void MovementScalesTimeBetweenMinimumAndFullSpeed()
        {
            float slow = ShardstepClock.ComputeTimeScale(false, false, 0.2f);
            float fast = ShardstepClock.ComputeTimeScale(false, false, 1f);

            Assert.That(slow, Is.GreaterThan(0f));
            Assert.That(slow, Is.LessThan(fast));
            Assert.That(fast, Is.EqualTo(1f));
        }
    }
}
