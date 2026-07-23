using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Shardstep
{
    [DefaultExecutionOrder(15000)]
    public sealed class ShardstepPortraitCameraAssist : MonoBehaviour
    {
        private const float ProbeInterval = 0.25f;
        private const float MaxAssistDistance = 2.4f;
        private const float AssistSmoothTime = 0.22f;
        private const float ObstructionAlpha = 0.22f;

        private sealed class FadedRenderer
        {
            public Renderer renderer;
            public Color[] originalColors;
        }

        private readonly List<Health> enemies = new List<Health>();
        private readonly List<FadedRenderer> faded = new List<FadedRenderer>();
        private Transform player;
        private Health playerHealth;
        private ArenaDirector director;
        private Camera worldCamera;
        private float nextProbe;
        private Vector3 appliedOffset;
        private Vector3 smoothedOffset;
        private Vector3 smoothVelocity;

        private void OnEnable()
        {
            SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            RestoreCameraOffset();
            RestoreObstructions();
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            RestoreCameraOffset();
            RestoreObstructions();
            player = null;
            playerHealth = null;
            director = null;
            worldCamera = null;
            enemies.Clear();
            smoothedOffset = Vector3.zero;
            smoothVelocity = Vector3.zero;
        }

        private void Update()
        {
            BindReferences();
            if (Time.unscaledTime >= nextProbe)
            {
                nextProbe = Time.unscaledTime + ProbeInterval;
                RefreshEnemies();
            }
        }

        private void LateUpdate()
        {
            RestoreCameraOffset();
            RestoreObstructions();

            if (player == null || worldCamera == null || (director != null && (director.Victory || director.Defeat)))
            {
                smoothedOffset = Vector3.SmoothDamp(smoothedOffset, Vector3.zero, ref smoothVelocity, AssistSmoothTime, Mathf.Infinity, Time.unscaledDeltaTime);
                return;
            }

            Vector3 desired = CalculateAssistOffset();
            smoothedOffset = Vector3.SmoothDamp(smoothedOffset, desired, ref smoothVelocity, AssistSmoothTime, Mathf.Infinity, Time.unscaledDeltaTime);
            appliedOffset = smoothedOffset;
            worldCamera.transform.position += appliedOffset;
            FadeObstructions();
        }

        private void BindReferences()
        {
            if (player == null)
            {
                GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
                if (playerObject != null)
                {
                    player = playerObject.transform;
                    playerHealth = playerObject.GetComponent<Health>();
                }
            }

            if (worldCamera == null)
            {
                worldCamera = Camera.main;
            }

            if (director == null)
            {
                director = Object.FindObjectOfType<ArenaDirector>();
            }
        }

        private void RefreshEnemies()
        {
            enemies.Clear();
            Health[] all = Object.FindObjectsOfType<Health>();
            for (int i = 0; i < all.Length; i++)
            {
                Health health = all[i];
                if (health != null && health != playerHealth && health.Current > 0)
                {
                    enemies.Add(health);
                }
            }
        }

        private Vector3 CalculateAssistOffset()
        {
            if (enemies.Count == 0)
            {
                return Vector3.zero;
            }

            Vector3 centroid = Vector3.zero;
            int count = 0;
            int offscreen = 0;
            for (int i = 0; i < enemies.Count; i++)
            {
                Health enemy = enemies[i];
                if (enemy == null || enemy.Current <= 0)
                {
                    continue;
                }

                centroid += enemy.transform.position;
                count++;
                Vector3 viewport = worldCamera.WorldToViewportPoint(enemy.transform.position + Vector3.up * 0.8f);
                if (viewport.z <= 0f || viewport.x < 0.06f || viewport.x > 0.94f || viewport.y < 0.08f || viewport.y > 0.92f)
                {
                    offscreen++;
                }
            }

            if (count == 0)
            {
                return Vector3.zero;
            }

            centroid /= count;
            Vector3 focus = Vector3.Lerp(player.position, centroid, Mathf.Clamp01(0.18f + (float)offscreen / count * 0.22f));
            Vector3 playerViewport = worldCamera.WorldToViewportPoint(player.position + Vector3.up * 0.8f);
            Vector3 viewportCorrection = Vector3.zero;

            if (playerViewport.z > 0f)
            {
                float horizontalError = Mathf.Clamp(playerViewport.x - 0.5f, -0.22f, 0.22f);
                float verticalError = Mathf.Clamp(playerViewport.y - 0.42f, -0.18f, 0.18f);
                viewportCorrection = -worldCamera.transform.right * horizontalError * 2.8f
                    - worldCamera.transform.up * verticalError * 2.1f;
            }

            Vector3 focusDirection = focus - player.position;
            focusDirection.y = 0f;
            Vector3 worldAssist = focusDirection.sqrMagnitude > 0.01f
                ? focusDirection.normalized * Mathf.Min(MaxAssistDistance, focusDirection.magnitude * 0.24f)
                : Vector3.zero;

            Vector3 combined = worldAssist + viewportCorrection;
            return Vector3.ClampMagnitude(combined, MaxAssistDistance);
        }

        private void RestoreCameraOffset()
        {
            if (worldCamera != null && appliedOffset.sqrMagnitude > 0f)
            {
                worldCamera.transform.position -= appliedOffset;
            }

            appliedOffset = Vector3.zero;
        }

        private void FadeObstructions()
        {
            if (player == null || worldCamera == null)
            {
                return;
            }

            Vector3 from = worldCamera.transform.position;
            Vector3 target = player.position + Vector3.up * 0.75f;
            Vector3 direction = target - from;
            float distance = direction.magnitude;
            if (distance <= 0.1f)
            {
                return;
            }

            RaycastHit[] hits = Physics.RaycastAll(from, direction.normalized, distance, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < hits.Length; i++)
            {
                Renderer renderer = hits[i].collider != null ? hits[i].collider.GetComponentInChildren<Renderer>() : null;
                if (renderer == null || renderer.transform == player || renderer.transform.IsChildOf(player))
                {
                    continue;
                }

                Material[] materials = renderer.materials;
                Color[] originals = new Color[materials.Length];
                bool changed = false;
                for (int m = 0; m < materials.Length; m++)
                {
                    Material material = materials[m];
                    if (material == null || !material.HasProperty("_Color"))
                    {
                        continue;
                    }

                    originals[m] = material.color;
                    Color fadedColor = material.color;
                    fadedColor.a = Mathf.Min(fadedColor.a, ObstructionAlpha);
                    material.color = fadedColor;
                    changed = true;
                }

                if (changed)
                {
                    faded.Add(new FadedRenderer { renderer = renderer, originalColors = originals });
                }
            }
        }

        private void RestoreObstructions()
        {
            for (int i = 0; i < faded.Count; i++)
            {
                FadedRenderer entry = faded[i];
                if (entry.renderer == null)
                {
                    continue;
                }

                Material[] materials = entry.renderer.materials;
                int count = Mathf.Min(materials.Length, entry.originalColors.Length);
                for (int m = 0; m < count; m++)
                {
                    if (materials[m] != null && materials[m].HasProperty("_Color"))
                    {
                        materials[m].color = entry.originalColors[m];
                    }
                }
            }

            faded.Clear();
        }
    }
}
