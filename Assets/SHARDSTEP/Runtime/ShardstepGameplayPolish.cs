using UnityEngine;

namespace Shardstep
{
    [DefaultExecutionOrder(10000)]
    public sealed class ShardstepGameplayPolish : MonoBehaviour
    {
        private const float IntroDuration = 2.2f;
        private const float FeedbackDuration = 0.28f;
        private const float RescueFloor = -2.5f;

        private Transform player;
        private PlayerMotor motor;
        private Health playerHealth;
        private ArenaDirector director;
        private Camera worldCamera;

        private Vector3 lastSafePosition;
        private bool hasSafePosition;
        private int previousHealth = -1;
        private int previousEnemies = -1;
        private float introUntil;
        private float damageUntil;
        private float killUntil;
        private float cameraShake;
        private Texture2D whiteTexture;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (Object.FindObjectOfType<ShardstepGameplayPolish>() != null)
            {
                return;
            }

            GameObject host = new GameObject("SHARDSTEP Gameplay Polish");
            Object.DontDestroyOnLoad(host);
            host.AddComponent<ShardstepGameplayPolish>();
        }

        private void Awake()
        {
            introUntil = Time.unscaledTime + IntroDuration;
            whiteTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            whiteTexture.SetPixel(0, 0, Color.white);
            whiteTexture.Apply();
        }

        private void Update()
        {
            BindReferences();
            if (player == null)
            {
                return;
            }

            TrackSafePosition();
            RescuePlayerIfNeeded();
            DetectCombatFeedback();
        }

        private void LateUpdate()
        {
            if (worldCamera == null || cameraShake <= 0.001f)
            {
                return;
            }

            float strength = cameraShake * 0.08f;
            Vector3 offset = new Vector3(
                Random.Range(-strength, strength),
                Random.Range(-strength, strength),
                0f);
            worldCamera.transform.position += offset;
            cameraShake = Mathf.MoveTowards(cameraShake, 0f, Time.unscaledDeltaTime * 4.5f);
        }

        private void BindReferences()
        {
            if (player == null)
            {
                GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
                if (playerObject != null)
                {
                    player = playerObject.transform;
                    motor = playerObject.GetComponent<PlayerMotor>();
                    playerHealth = playerObject.GetComponent<Health>();
                    lastSafePosition = player.position;
                    hasSafePosition = true;
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

        private void TrackSafePosition()
        {
            if (player.position.y < RescueFloor + 1f)
            {
                return;
            }

            if (Physics.Raycast(player.position + Vector3.up, Vector3.down, out RaycastHit hit, 4f))
            {
                lastSafePosition = new Vector3(player.position.x, hit.point.y + 0.12f, player.position.z);
                hasSafePosition = true;
            }
        }

        private void RescuePlayerIfNeeded()
        {
            if (!hasSafePosition || player.position.y >= RescueFloor)
            {
                return;
            }

            player.position = lastSafePosition;
            if (motor != null)
            {
                motor.MoveInput = Vector2.zero;
            }

            if (ShardstepClock.Instance != null)
            {
                ShardstepClock.Instance.MovementMagnitude = 0f;
            }

            damageUntil = Time.unscaledTime + FeedbackDuration;
            cameraShake = 1f;
        }

        private void DetectCombatFeedback()
        {
            if (playerHealth != null)
            {
                if (previousHealth >= 0 && playerHealth.Current < previousHealth)
                {
                    damageUntil = Time.unscaledTime + FeedbackDuration;
                    cameraShake = 1f;
                }

                previousHealth = playerHealth.Current;
            }

            if (director != null)
            {
                if (previousEnemies >= 0 && director.RemainingEnemies < previousEnemies)
                {
                    killUntil = Time.unscaledTime + FeedbackDuration;
                    cameraShake = Mathf.Max(cameraShake, 0.45f);
                }

                previousEnemies = director.RemainingEnemies;
            }
        }

        private void OnGUI()
        {
            if (whiteTexture == null)
            {
                return;
            }

            Rect safe = Screen.safeArea;
            float top = Screen.height - safe.yMax;
            float margin = Mathf.Clamp(safe.width * 0.035f, 12f, 26f);

            if (Time.unscaledTime < introUntil)
            {
                DrawCenteredPanel(
                    new Rect(safe.xMin + margin, top + safe.height * 0.28f, safe.width - margin * 2f, 126f),
                    "ELIMINATE ALL HOSTILES\nTOUCH A DESTINATION TO DASH\nUSE ATTACK TO STRIKE");
            }

            if (playerHealth != null && playerHealth.Maximum > 0)
            {
                float ratio = Mathf.Clamp01((float)playerHealth.Current / playerHealth.Maximum);
                if (ratio <= 0.35f)
                {
                    float pulse = 0.11f + Mathf.PingPong(Time.unscaledTime * 0.14f, 0.08f);
                    DrawScreenTint(new Color(1f, 0.04f, 0.04f, pulse));
                }
            }

            if (Time.unscaledTime < damageUntil)
            {
                DrawScreenTint(new Color(1f, 0.05f, 0.05f, 0.23f));
            }
            else if (Time.unscaledTime < killUntil)
            {
                DrawScreenTint(new Color(0.1f, 1f, 0.86f, 0.12f));
            }

            if (director != null && director.Victory)
            {
                DrawCenteredPanel(
                    new Rect(safe.xMin + margin, top + safe.height * 0.32f, safe.width - margin * 2f, 116f),
                    "AREA SECURED\nALL HOSTILES ELIMINATED");
            }
            else if (director != null && director.Defeat)
            {
                DrawCenteredPanel(
                    new Rect(safe.xMin + margin, top + safe.height * 0.32f, safe.width - margin * 2f, 116f),
                    "TIMELINE COLLAPSED\nRESTART AND ADAPT");
            }
        }

        private void DrawScreenTint(Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), whiteTexture);
            GUI.color = previous;
        }

        private static void DrawCenteredPanel(Rect rect, string text)
        {
            GUIStyle style = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = Mathf.Clamp(Mathf.RoundToInt(Screen.width * 0.044f), 16, 28),
                fontStyle = FontStyle.Bold,
                wordWrap = true
            };
            GUI.Box(rect, text, style);
        }

        private void OnDestroy()
        {
            if (whiteTexture != null)
            {
                Destroy(whiteTexture);
            }
        }
    }
}
