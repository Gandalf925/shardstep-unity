using UnityEngine;

namespace Shardstep
{
    public static class ShardstepMobilePolicy
    {
        public const float MinimumControlRadius = 72f;
        public const float MaximumControlRadius = 170f;

        public static bool IsLandscape(int width, int height)
        {
            return width >= height;
        }

        public static float ControlRadius(int width, int height)
        {
            float shortestSide = Mathf.Max(1f, Mathf.Min(width, height));
            return Mathf.Clamp(shortestSide * 0.18f, MinimumControlRadius, MaximumControlRadius);
        }

        public static float AimReleaseThreshold(int width, int height)
        {
            return ControlRadius(width, height) * 0.3f;
        }
    }

    public static class ShardstepMobileRuntime
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ConfigureRuntime()
        {
            Input.multiTouchEnabled = true;
            Input.simulateMouseWithTouches = false;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;

            if (Application.isMobilePlatform || Input.touchSupported)
            {
                Screen.autorotateToLandscapeLeft = false;
                Screen.autorotateToLandscapeRight = false;
                Screen.autorotateToPortrait = true;
                Screen.autorotateToPortraitUpsideDown = false;
                Screen.orientation = ScreenOrientation.Portrait;
            }
        }
    }
}
