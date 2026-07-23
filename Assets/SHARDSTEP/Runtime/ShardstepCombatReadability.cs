using System.Collections.Generic;
using UnityEngine;

namespace Shardstep
{
    [DefaultExecutionOrder(9000)]
    public sealed class ShardstepCombatReadability : MonoBehaviour
    {
        private const float ProbeInterval = 0.35f;
        private const float OffscreenMargin = 34f;
        private const float EdgePadding = 64f;

        private readonly List<Health> enemies = new List<Health>();
        private Transform player;
        private Health playerHealth;
        private Camera worldCamera;
        private ArenaDirector director;
        private Collider currentFloor;
        private Bounds safeBounds;
        private bool hasSafeBounds;
        private float nextProbe;
        private Texture2D pixel;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (Object.FindObjectOfType<ShardstepCombatReadability>() != null)
            {
                return;
            }

            GameObject host = new GameObject("SHARDSTEP Combat Readability");
            Object.DontDestroyOnLoad(host);
            host.AddComponent<ShardstepCombatReadability>();
        }

        private void Awake()
        {
            pixel = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            pixel.SetPixel(0, 0, Color.white);
            pixel.Apply();
        }

        private void Update()
        {
            BindReferences();
            if (player == null)
            {
                return;
            }

            if (Time.unscaledTime >= nextProbe)
            {
                nextProbe = Time.unscaledTime + ProbeInterval;
                RefreshEnemies();
                RefreshSafeBounds();
            }

            KeepPlayerInsideVisibleArena();
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
            Health[] allHealth = Object.FindObjectsOfType<Health>();
            foreach (Health health in allHealth)
            {
                if (health == null || health == playerHealth || health.Current <= 0)
                {
                    continue;
                }

                enemies.Add(health);
            }
        }

        private void RefreshSafeBounds()
        {
            if (player == null)
            {
                return;
            }

            Vector3 origin = player.position + Vector3.up * 2f;
            if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 8f, ~0, QueryTriggerInteraction.Ignore))
            {
                return;
            }

            Collider floor = hit.collider;
            if (floor == null)
            {
                return;
            }

            if (floor == currentFloor && hasSafeBounds)
            {
                return;
            }

            Bounds bounds = floor.bounds;
            if (bounds.size.x < 2f || bounds.size.z < 2f)
            {
                return;
            }

            currentFloor = floor;
            safeBounds = bounds;
            safeBounds.Expand(new Vector3(-0.8f, 0f, -0.8f));
            hasSafeBounds = safeBounds.size.x > 1f && safeBounds.size.z > 1f;
        }

        private void KeepPlayerInsideVisibleArena()
        {
            if (!hasSafeBounds || player == null)
            {
                return;
            }

            Vector3 position = player.position;
            float clampedX = Mathf.Clamp(position.x, safeBounds.min.x, safeBounds.max.x);
            float clampedZ = Mathf.Clamp(position.z, safeBounds.min.z, safeBounds.max.z);
            if (Mathf.Abs(clampedX - position.x) < 0.001f && Mathf.Abs(clampedZ - position.z) < 0.001f)
            {
                return;
            }

            player.position = new Vector3(clampedX, position.y, clampedZ);
            PlayerMotor motor = player.GetComponent<PlayerMotor>();
            if (motor != null)
            {
                motor.MoveInput = Vector2.zero;
            }

            if (ShardstepClock.Instance != null)
            {
                ShardstepClock.Instance.MovementMagnitude = 0f;
            }
        }

        private void OnGUI()
        {
            if (pixel == null || player == null || worldCamera == null)
            {
                return;
            }

            DrawEnemyDirectionIndicators();
            DrawEncounterProgress();
        }

        private void DrawEncounterProgress()
        {
            if (director == null || director.Victory || director.Defeat)
            {
                return;
            }

            Rect safe = Screen.safeArea;
            float top = Screen.height - safe.yMax;
            float width = Mathf.Min(safe.width * 0.48f, 220f);
            Rect rect = new Rect(safe.center.x - width * 0.5f, top + 126f, width, 30f);
            GUIStyle style = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                fontSize = Mathf.Clamp(Mathf.RoundToInt(Screen.width * 0.034f), 13, 20)
            };
            GUI.Box(rect, director.RemainingEnemies > 0
                ? $"HOSTILES  {director.RemainingEnemies}"
                : "AREA CLEAR", style);
        }

        private void DrawEnemyDirectionIndicators()
        {
            Rect safe = Screen.safeArea;
            foreach (Health enemyHealth in enemies)
            {
                if (enemyHealth == null || enemyHealth.Current <= 0)
                {
                    continue;
                }

                Transform enemy = enemyHealth.transform;
                Vector3 viewport = worldCamera.WorldToViewportPoint(enemy.position + Vector3.up * 0.8f);
                bool behind = viewport.z <= 0f;
                bool offscreen = behind || viewport.x < 0f || viewport.x > 1f || viewport.y < 0f || viewport.y > 1f;
                if (!offscreen)
                {
                    DrawEnemyMarker(worldCamera.WorldToScreenPoint(enemy.position + Vector3.up * 1.2f), enemyHealth);
                    continue;
                }

                Vector3 direction = enemy.position - player.position;
                direction.y = 0f;
                Vector3 screenDirection = worldCamera.transform.InverseTransformDirection(direction.normalized);
                Vector2 normalized = new Vector2(screenDirection.x, screenDirection.z);
                if (behind)
                {
                    normalized = -normalized;
                }

                if (normalized.sqrMagnitude < 0.001f)
                {
                    normalized = Vector2.up;
                }

                normalized.Normalize();
                Vector2 center = new Vector2(safe.center.x, Screen.height - safe.center.y);
                float halfWidth = Mathf.Max(20f, safe.width * 0.5f - EdgePadding);
                float halfHeight = Mathf.Max(20f, safe.height * 0.5f - EdgePadding);
                float scaleX = Mathf.Abs(normalized.x) > 0.001f ? halfWidth / Mathf.Abs(normalized.x) : float.MaxValue;
                float scaleY = Mathf.Abs(normalized.y) > 0.001f ? halfHeight / Mathf.Abs(normalized.y) : float.MaxValue;
                float distance = Mathf.Min(scaleX, scaleY);
                Vector2 point = center + normalized * distance;
                DrawArrow(point, normalized, enemyHealth);
            }
        }

        private void DrawEnemyMarker(Vector3 screenPoint, Health enemyHealth)
        {
            if (screenPoint.z <= 0f)
            {
                return;
            }

            float x = screenPoint.x;
            float y = Screen.height - screenPoint.y;
            Rect marker = new Rect(x - 18f, y - 18f, 36f, 8f);
            DrawBar(marker, enemyHealth);
        }

        private void DrawArrow(Vector2 point, Vector2 direction, Health enemyHealth)
        {
            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            Matrix4x4 previous = GUI.matrix;
            GUIUtility.RotateAroundPivot(angle, point);

            Color previousColor = GUI.color;
            GUI.color = new Color(1f, 0.35f, 0.18f, 0.92f);
            GUI.DrawTexture(new Rect(point.x - 18f, point.y - 4f, 30f, 8f), pixel);
            GUI.DrawTexture(new Rect(point.x + 4f, point.y - 10f, 12f, 20f), pixel);
            GUI.color = previousColor;
            GUI.matrix = previous;

            DrawBar(new Rect(point.x - 24f, point.y + 18f, 48f, 6f), enemyHealth);
        }

        private void DrawBar(Rect rect, Health health)
        {
            float ratio = health.Maximum > 0 ? Mathf.Clamp01((float)health.Current / health.Maximum) : 0f;
            Color previous = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.72f);
            GUI.DrawTexture(rect, pixel);
            GUI.color = new Color(1f, 0.25f, 0.15f, 0.92f);
            GUI.DrawTexture(new Rect(rect.x + 1f, rect.y + 1f, (rect.width - 2f) * ratio, Mathf.Max(1f, rect.height - 2f)), pixel);
            GUI.color = previous;
        }

        private void OnDestroy()
        {
            if (pixel != null)
            {
                Destroy(pixel);
            }
        }
    }
}
