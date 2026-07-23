using UnityEngine;
using UnityEngine.SceneManagement;

namespace Shardstep
{
    [DefaultExecutionOrder(-20000)]
    public sealed class ShardstepRuntimeRoot : MonoBehaviour
    {
        private static ShardstepRuntimeRoot instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            if (instance != null)
            {
                return;
            }

            ShardstepRuntimeRoot existing = Object.FindObjectOfType<ShardstepRuntimeRoot>();
            if (existing != null)
            {
                instance = existing;
                return;
            }

            GameObject host = new GameObject("SHARDSTEP Runtime Root");
            instance = host.AddComponent<ShardstepRuntimeRoot>();
            Object.DontDestroyOnLoad(host);
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            DontDestroyOnLoad(gameObject);
            EnsureModules();
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            EnsureModules();
        }

        private void EnsureModules()
        {
            EnsureSingle<ShardstepPortraitMoveController>();
            EnsureSingle<ShardstepGameplayPolish>();
            EnsureSingle<ShardstepCombatReadability>();
            EnsureSingle<ShardstepCombatAssist>();
            EnsureSingle<ShardstepCombatJuice>();
        }

        private void EnsureSingle<T>() where T : Component
        {
            T[] all = Object.FindObjectsOfType<T>();
            T keeper = null;

            for (int i = 0; i < all.Length; i++)
            {
                T candidate = all[i];
                if (candidate == null)
                {
                    continue;
                }

                if (candidate.gameObject == gameObject)
                {
                    keeper = candidate;
                    break;
                }

                if (keeper == null)
                {
                    keeper = candidate;
                }
            }

            if (keeper == null || keeper.gameObject != gameObject)
            {
                keeper = gameObject.AddComponent<T>();
            }

            for (int i = 0; i < all.Length; i++)
            {
                T candidate = all[i];
                if (candidate != null && candidate != keeper)
                {
                    Destroy(candidate);
                }
            }
        }
    }
}
