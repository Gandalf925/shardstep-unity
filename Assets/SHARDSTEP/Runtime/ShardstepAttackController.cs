using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Shardstep
{
    [DefaultExecutionOrder(-300)]
    public sealed class ShardstepAttackController : MonoBehaviour
    {
        private const float RailRange = 18f;
        private const float BladeRange = 5.2f;
        private const float RailCooldown = 0.42f;
        private const float BladeCooldown = 0.56f;
        private const float MessageDuration = 0.65f;

        private readonly List<Health> enemies = new List<Health>();
        private Transform player;
        private PlayerCombat combat;
        private PlayerMotor motor;
        private Health playerHealth;
        private ArenaDirector director;
        private Camera worldCamera;
        private int activeFinger = -1;
        private float nextAttackAt;
        private float nextProbe;
        private string statusMessage;
        private float statusUntil;
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
            CancelInput();
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            player = null;
            combat = null;
            motor = null;
            playerHealth = null;
            director = null;
            worldCamera = null;
            enemies.Clear();
            nextAttackAt = 0f;
            statusMessage = null;
            CancelInput();
        }

        private void Update()
        {
            BindReferences();
            if (player == null || combat == null)
            {
                return;
            }

            if (Time.unscaledTime >= nextProbe)
            {
                nextProbe = Time.unscaledTime + 0.18f;
                RefreshEnemies();
            }

            if (director != null && (director.Victory || director.Defeat))
            {
                CancelInput();
                return;
            }

            HandleTouchInput();
#if UNITY_EDITOR || UNITY_STANDALONE
            HandleEditorInput();
#endif
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
                    motor = playerObject.GetComponent<PlayerMotor>();
                    playerHealth = playerObject.GetComponent<Health>();
                }
            }

            if (director == null)
            {
                director = Object.FindObjectOfType<ArenaDirector>();
            }

            if (worldCamera == null)
            {
                worldCamera = Camera.main;
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
            }
        }

        private void HandleTouchInput()
        {
            Rect attack = AttackRectScreen();
            for (int i = 0; i < Input.touchCount; i++)
            {
                Touch touch = Input.GetTouch(i);
                if (activeFinger < 0)
                {
                    if (touch.phase == TouchPhase.Began && attack.Contains(touch.position))
                    {
                        activeFinger = touch.fingerId;
                    }

                    continue;
                }

                if (touch.fingerId != activeFinger)
                {
                    continue;
                }

                if (touch.phase == TouchPhase.Ended)
                {
                    bool releasedInside = attack.Contains(touch.position);
                    activeFinger = -1;
                    if (releasedInside)
                    {
                        TryAttack();
                    }
                }
                else if (touch.phase == TouchPhase.Canceled)
                {
                    activeFinger = -1;
                }
            }
        }

#if UNITY_EDITOR || UNITY_STANDALONE
        private void HandleEditorInput()
        {
            if (Input.GetKeyDown(KeyCode.Space))
            {
                TryAttack();
                return;
            }

            if (Input.GetMouseButtonUp(0) && AttackRectScreen().Contains(Input.mousePosition))
            {
                TryAttack();
            }
        }
#endif

        private void TryAttack()
        {
            if (Time.unscaledTime < nextAttackAt)
            {
                ShowStatus("COOLDOWN");
                return;
            }

            Health target = SelectBestTarget(out bool anyTarget);
            if (target == null)
            {
                ShowStatus(anyTarget ? "OUT OF RANGE" : "NO TARGET");
                return;
            }

            Vector3 direction = target.transform.position - player.position;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.001f)
            {
                ShowStatus("NO TARGET");
                return;
            }

            if (motor != null)
            {
                motor.MoveInput = Vector2.zero;
            }

            if (ShardstepClock.Instance != null)
            {
                ShardstepClock.Instance.MovementMagnitude = 0f;
            }

            combat.BeginAim();
            combat.SetAimDirection(direction.normalized);
            combat.ReleaseAim(100f);
            nextAttackAt = Time.unscaledTime + (combat.Mode == WeaponMode.Rail ? RailCooldown : BladeCooldown);
        }

        private Health SelectBestTarget(out bool anyTarget)
        {
            anyTarget = enemies.Count > 0;
            float range = combat.Mode == WeaponMode.Rail ? RailRange : BladeRange;
            Health best = null;
            float bestScore = float.MaxValue;

            foreach (Health enemy in enemies)
            {
                if (enemy == null || enemy.Current <= 0)
                {
                    continue;
                }

                Vector3 delta = enemy.transform.position - player.position;
                delta.y = 0f;
                float distance = delta.magnitude;
                if (distance > range || distance < 0.05f)
                {
                    continue;
                }

                float angle = Vector3.Angle(player.forward, delta.normalized);
                float viewportPenalty = 0f;
                if (worldCamera != null)
                {
                    Vector3 viewport = worldCamera.WorldToViewportPoint(enemy.transform.position + Vector3.up * 0.8f);
                    bool visible = viewport.z > 0f && viewport.x >= 0f && viewport.x <= 1f && viewport.y >= 0f && viewport.y <= 1f;
                    viewportPenalty = visible ? 0f : 4f;
                }

                float score = distance + angle * 0.035f + viewportPenalty;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = enemy;
                }
            }

            return best;
        }

        private void ShowStatus(string message)
        {
            statusMessage = message;
            statusUntil = Time.unscaledTime + MessageDuration;
        }

        private void CancelInput()
        {
            activeFinger = -1;
            if (combat != null && combat.IsAiming)
            {
                combat.CancelAim();
            }
        }

        private Rect AttackRectScreen()
        {
            Rect safe = Screen.safeArea;
            float size = Mathf.Clamp(safe.width * 0.22f, 92f, 142f);
            float margin = Mathf.Clamp(safe.width * 0.035f, 12f, 28f);
            return new Rect(safe.xMax - size - margin, safe.yMin + margin, size, size);
        }

        private void OnGUI()
        {
            if (pixel == null || combat == null)
            {
                return;
            }

            Rect screenRect = AttackRectScreen();
            Rect guiRect = new Rect(screenRect.x, Screen.height - screenRect.yMax, screenRect.width, screenRect.height);
            bool cooling = Time.unscaledTime < nextAttackAt;
            float remaining = Mathf.Max(0f, nextAttackAt - Time.unscaledTime);

            Color previous = GUI.color;
            GUI.color = cooling ? new Color(0.22f, 0.22f, 0.26f, 0.88f) : new Color(0.08f, 0.76f, 0.88f, 0.9f);
            GUI.DrawTexture(guiRect, pixel);
            GUI.color = previous;

            GUIStyle buttonStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                fontSize = Mathf.Clamp(Mathf.RoundToInt(Screen.width * 0.045f), 17, 28)
            };
            GUI.Label(guiRect, cooling ? remaining.ToString("0.0") : "ATTACK", buttonStyle);

            if (!string.IsNullOrEmpty(statusMessage) && Time.unscaledTime < statusUntil)
            {
                GUIStyle messageStyle = new GUIStyle(GUI.skin.box)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontStyle = FontStyle.Bold,
                    fontSize = Mathf.Clamp(Mathf.RoundToInt(Screen.width * 0.036f), 14, 22)
                };
                GUI.Box(new Rect(guiRect.x - 12f, guiRect.y - 42f, guiRect.width + 24f, 34f), statusMessage, messageStyle);
            }
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
