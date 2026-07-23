using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Shardstep
{
    [DefaultExecutionOrder(-500)]
    public sealed class ShardstepCombatAssist : MonoBehaviour
    {
        private const float ProbeInterval = 0.2f;
        private const float AssistRange = 14f;
        private const float CloseThreatRange = 3.2f;
        private const float ThreatDuration = 0.55f;

        private readonly List<Health> enemies = new List<Health>();
        private readonly Dictionary<Health, Vector3> previousPositions = new Dictionary<Health, Vector3>();
        private Transform player;
        private PlayerCombat combat;
        private Health playerHealth;
        private Camera worldCamera;
        private ArenaDirector director;
        private float nextProbe;
        private Health assistedTarget;
        private Health threatTarget;
        private float threatUntil;
        private Texture2D pixel;

        private void Awake()
        {
            pixel = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            pixel.SetPixel(0, 0, Color.white);
            pixel.Apply();
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
            player = null;
            combat = null;
            playerHealth = null;
            worldCamera = null;
            director = null;
            assistedTarget = null;
            threatTarget = null;
            threatUntil = 0f;
            enemies.Clear();
            previousPositions.Clear();
        }

        private void Update()
        {
            BindReferences();
            if (player == null || combat == null)
            {
                return;
            }

            if (director != null && (director.Victory || director.Defeat))
            {
                assistedTarget = null;
                threatTarget = null;
                threatUntil = 0f;
                return;
            }

            if (Time.unscaledTime >= nextProbe)
            {
                nextProbe = Time.unscaledTime + ProbeInterval;
                RefreshEnemies();
                DetectIncomingThreat();
            }
        }

        private void BindReferences()
        {
            if (player == null)
            {
                GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
                if (playerObject != null)
                {
                    player = playerObject.transform;
                    combat = playerObject.GetComponent<PlayerCombat>();
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
            foreach (Health health in Object.FindObjectsOfType<Health>())
            {
                if (health == null || health == playerHealth || health.Current <= 0)
                {
                    continue;
                }

                enemies.Add(health);
                if (!previousPositions.ContainsKey(health))
                {
                    previousPositions[health] = health.transform.position;
                }
            }

            List<Health> stale = new List<Health>();
            foreach (Health tracked in previousPositions.Keys)
            {
                if (tracked == null || !enemies.Contains(tracked))
                {
                    stale.Add(tracked);
                }
            }

            foreach (Health removed in stale)
            {
                previousPositions.Remove(removed);
            }

            assistedTarget = SelectBestTarget();
        }

        private Health SelectBestTarget()
        {
            Health best = null;
            float bestScore = float.MaxValue;
            foreach (Health enemy in enemies)
            {
                Vector3 delta = enemy.transform.position - player.position;
                delta.y = 0f;
                float distance = delta.magnitude;
                if (distance > AssistRange || distance < 0.05f)
                {
                    continue;
                }

                float angle = Vector3.Angle(player.forward, delta.normalized);
                float score = distance + angle * 0.045f;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = enemy;
                }
            }

            return best;
        }

        private void DetectIncomingThreat()
        {
            Health strongestThreat = null;
            float strongestScore = 0f;
            foreach (Health enemy in enemies)
            {
                Vector3 current = enemy.transform.position;
                Vector3 previous = previousPositions.TryGetValue(enemy, out Vector3 known) ? known : current;
                previousPositions[enemy] = current;

                Vector3 toPlayer = player.position - current;
                toPlayer.y = 0f;
                float distance = toPlayer.magnitude;
                if (distance > CloseThreatRange || distance < 0.1f)
                {
                    continue;
                }

                Vector3 velocity = (current - previous) / Mathf.Max(ProbeInterval, 0.01f);
                velocity.y = 0f;
                float approach = Vector3.Dot(velocity, toPlayer.normalized);
                float facing = Vector3.Dot(enemy.transform.forward, toPlayer.normalized);
                float score = approach + Mathf.Max(0f, facing) * 0.45f + (CloseThreatRange - distance) * 0.8f;
                if (score > strongestScore && score > 0.75f)
                {
                    strongestScore = score;
                    strongestThreat = enemy;
                }
            }

            if (strongestThreat != null)
            {
                threatTarget = strongestThreat;
                threatUntil = Time.unscaledTime + ThreatDuration;
            }
        }

        private void OnGUI()
        {
            if (pixel == null || player == null || combat == null)
            {
                return;
            }

            Rect safe = Screen.safeArea;
            float top = Screen.height - safe.yMax;
            float margin = Mathf.Clamp(safe.width * 0.035f, 12f, 26f);
            DrawWeaponIdentity(new Rect(safe.xMin + margin, top + 152f, safe.width - margin * 2f, 42f));
            DrawTargetLock();
            DrawThreatTelegraph();
        }

        private void DrawWeaponIdentity(Rect rect)
        {
            string text = combat.Mode == WeaponMode.Rail
                ? "RAIL  •  LONG RANGE / PRECISE"
                : "BLADE  •  CLOSE RANGE / HEAVY";
            GUIStyle style = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                fontSize = Mathf.Clamp(Mathf.RoundToInt(Screen.width * 0.032f), 13, 20)
            };
            GUI.Box(rect, text, style);
        }

        private void DrawTargetLock()
        {
            if (assistedTarget == null || worldCamera == null)
            {
                return;
            }

            Vector3 screen = worldCamera.WorldToScreenPoint(assistedTarget.transform.position + Vector3.up * 1.25f);
            if (screen.z <= 0f)
            {
                return;
            }

            float x = screen.x;
            float y = Screen.height - screen.y;
            GUIStyle style = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                fontSize = Mathf.Clamp(Mathf.RoundToInt(Screen.width * 0.04f), 15, 24)
            };
            GUI.Label(new Rect(x - 52f, y - 28f, 104f, 28f), "TARGET", style);
        }

        private void DrawThreatTelegraph()
        {
            if (Time.unscaledTime >= threatUntil || threatTarget == null || worldCamera == null)
            {
                return;
            }

            Vector3 screen = worldCamera.WorldToScreenPoint(threatTarget.transform.position + Vector3.up * 0.15f);
            if (screen.z <= 0f)
            {
                return;
            }

            float pulse = 0.42f + Mathf.PingPong(Time.unscaledTime * 3.5f, 0.28f);
            float size = 70f + pulse * 18f;
            Rect ring = new Rect(screen.x - size * 0.5f, Screen.height - screen.y - size * 0.5f, size, size);
            Color previous = GUI.color;
            GUI.color = new Color(1f, 0.18f, 0.08f, pulse);
            GUI.DrawTexture(new Rect(ring.x, ring.y, ring.width, 4f), pixel);
            GUI.DrawTexture(new Rect(ring.x, ring.yMax - 4f, ring.width, 4f), pixel);
            GUI.DrawTexture(new Rect(ring.x, ring.y, 4f, ring.height), pixel);
            GUI.DrawTexture(new Rect(ring.xMax - 4f, ring.y, 4f, ring.height), pixel);
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
