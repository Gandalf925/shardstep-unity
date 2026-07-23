using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Shardstep
{
    public enum WeaponMode
    {
        Rail,
        Blade
    }

    public enum EnemyKind
    {
        Gunner,
        Hound,
        Sweeper
    }

    [Serializable]
    public sealed class SimpleApocalypseCatalogEntry
    {
        public string key;
        public GameObject prefab;
    }

    [CreateAssetMenu(menuName = "SHARDSTEP/SIMPLE Apocalypse Catalog")]
    public sealed class SimpleApocalypseCatalog : ScriptableObject
    {
        public List<SimpleApocalypseCatalogEntry> entries = new List<SimpleApocalypseCatalogEntry>();

        public GameObject Find(string key)
        {
            foreach (SimpleApocalypseCatalogEntry entry in entries)
            {
                if (entry != null && string.Equals(entry.key, key, StringComparison.OrdinalIgnoreCase))
                {
                    return entry.prefab;
                }
            }

            return null;
        }
    }

    public static class ShardstepVisuals
    {
        public static Material CreateMaterial(Color color, bool emission = false)
        {
            bool urp = GraphicsSettings.currentRenderPipeline != null;
            Shader shader = Shader.Find(urp ? "Universal Render Pipeline/Lit" : "Standard");
            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
            }

            Material material = new Material(shader)
            {
                color = color
            };

            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            if (emission)
            {
                material.EnableKeyword("_EMISSION");
                Color emitted = color * 2.5f;
                if (material.HasProperty("_EmissionColor"))
                {
                    material.SetColor("_EmissionColor", emitted);
                }
            }

            return material;
        }
    }

    [DefaultExecutionOrder(10000)]
    public sealed class ShardstepClock : MonoBehaviour
    {
        public const float StandardActionDuration = 0.18f;

        public static ShardstepClock Instance { get; private set; }

        public bool IsAiming { get; set; }
        public float MovementMagnitude { get; set; }
        public float ActiveSeconds { get; private set; }
        public int CompletedActions { get; private set; }
        public bool ForcedFreeze { get; set; }
        public bool IsExecuting { get; private set; }
        public float ActionProgress => IsExecuting && actionDuration > 0f
            ? Mathf.Clamp01(actionElapsed / actionDuration)
            : 0f;

        public event Action<int> ActionStarted;
        public event Action<int> ActionCompleted;

        private float actionDuration;
        private float actionElapsed;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            Time.timeScale = 0f;
        }

        private void Update()
        {
            if (ForcedFreeze)
            {
                Time.timeScale = 0f;
                return;
            }

            if (IsExecuting)
            {
                Time.timeScale = IsAiming ? 0f : 1f;
                return;
            }

            float fallback = ComputeTimeScale(IsAiming, false, MovementMagnitude);
            Time.timeScale = fallback;
            ActiveSeconds += Time.unscaledDeltaTime * fallback;
        }

        private void LateUpdate()
        {
            if (ForcedFreeze || !IsExecuting || IsAiming)
            {
                return;
            }

            float delta = Time.deltaTime;
            if (delta <= 0f)
            {
                return;
            }

            actionElapsed = Mathf.Min(actionDuration, actionElapsed + delta);
            ActiveSeconds += delta;
            if (actionElapsed >= actionDuration)
            {
                CompleteCurrentAction();
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            Time.timeScale = 1f;
        }

        public bool TryBeginAction(float duration = StandardActionDuration)
        {
            if (ForcedFreeze || IsExecuting)
            {
                return false;
            }

            IsAiming = false;
            MovementMagnitude = 0f;
            actionDuration = Mathf.Max(0.02f, duration);
            actionElapsed = 0f;
            IsExecuting = true;
            Time.timeScale = 1f;
            ActionStarted?.Invoke(CompletedActions + 1);
            return true;
        }

        public void ExecuteFor(float realSeconds)
        {
            if (!TryBeginAction(realSeconds) && IsExecuting)
            {
                actionDuration = Mathf.Max(actionDuration, actionElapsed + Mathf.Max(0.02f, realSeconds));
            }
        }

        public void CancelCurrentAction()
        {
            IsExecuting = false;
            actionElapsed = 0f;
            actionDuration = 0f;
            MovementMagnitude = 0f;
            Time.timeScale = 0f;
        }

        private void CompleteCurrentAction()
        {
            IsExecuting = false;
            actionElapsed = actionDuration;
            CompletedActions++;
            MovementMagnitude = 0f;
            Time.timeScale = 0f;
            ActionCompleted?.Invoke(CompletedActions);
        }

        public static float ComputeTimeScale(bool aiming, bool executing, float movementMagnitude)
        {
            if (aiming)
            {
                return 0f;
            }

            if (executing)
            {
                return 1f;
            }

            float magnitude = Mathf.Clamp01(movementMagnitude);
            if (magnitude < 0.08f)
            {
                return 0f;
            }

            return Mathf.Lerp(0.18f, 1f, magnitude);
        }
    }

    public sealed class Health : MonoBehaviour
    {
        public int Current { get; private set; }
        public int Maximum { get; private set; }
        public bool IsDead { get; private set; }
        public event Action<Health> Died;

        public void Configure(int maximum)
        {
            Maximum = Mathf.Max(1, maximum);
            Current = Maximum;
            IsDead = false;
        }

        public void Damage(int amount)
        {
            if (IsDead || amount <= 0)
            {
                return;
            }

            Current = Mathf.Max(0, Current - amount);
            StartCoroutine(HitFlash());

            if (Current == 0)
            {
                IsDead = true;
                Died?.Invoke(this);
            }
        }

        public int Heal(int amount)
        {
            if (IsDead || amount <= 0)
            {
                return 0;
            }

            int before = Current;
            Current = Mathf.Min(Maximum, Current + amount);
            return Current - before;
        }

        private IEnumerator HitFlash()
        {
            Renderer[] renderers = GetComponentsInChildren<Renderer>();
            foreach (Renderer renderer in renderers)
            {
                if (renderer.material.HasProperty("_Color"))
                {
                    renderer.material.color = Color.white;
                }
            }

            yield return new WaitForSecondsRealtime(0.07f);
        }
    }

    public sealed class PlayerMotor : MonoBehaviour
    {
        public float speed = 5.8f;
        public Vector2 MoveInput { get; set; }

        private CharacterController controller;

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
        }

        private void Update()
        {
            if (controller == null || Time.deltaTime <= 0f)
            {
                return;
            }

            Vector3 direction = new Vector3(MoveInput.x, 0f, MoveInput.y);
            if (direction.sqrMagnitude > 1f)
            {
                direction.Normalize();
            }

            controller.Move(direction * speed * Time.deltaTime);
            if (direction.sqrMagnitude > 0.02f)
            {
                transform.forward = Vector3.Slerp(transform.forward, direction.normalized, 18f * Time.deltaTime);
            }
        }
    }
}
