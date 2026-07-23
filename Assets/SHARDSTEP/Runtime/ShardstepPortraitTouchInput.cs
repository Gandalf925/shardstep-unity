using UnityEngine;

namespace Shardstep
{
    public static class ShardstepPortraitTouchPolicy
    {
        public const float MinimumCancelDistance = 48f;
        public const float MaximumCancelDistance = 110f;

        public static float CancelDistance(int width, int height)
        {
            float shortestSide = Mathf.Max(1f, Mathf.Min(width, height));
            return Mathf.Clamp(shortestSide * 0.11f, MinimumCancelDistance, MaximumCancelDistance);
        }

        public static bool ShouldCommit(Vector2 start, Vector2 end, int width, int height)
        {
            return Vector2.Distance(start, end) <= CancelDistance(width, height);
        }
    }
}
