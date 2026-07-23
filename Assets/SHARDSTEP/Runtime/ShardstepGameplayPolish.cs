using UnityEngine;
using UnityEngine.SceneManagement;

namespace Shardstep
{
    [DefaultExecutionOrder(10000)]
    public sealed class ShardstepGameplayPolish : MonoBehaviour
    {
        private const float FeedbackDuration = 0.28f;
        private const float RescueFloor = -2.5f;

        private Transform player;
        private PlayerMotor motor;
        private Health playerHealth;
        private ArenaDirector director;
        private Camera worldCamera;

        private Vector3 lastSafePosition;
        private Vector3 appliedCameraOffset;
        private bool hasSafePosition;
        private int previousHealth = -1;
        private int previousEnemies = -1;
        private float damageUntil;
        private float killUntil;
        private float cameraShake;
        private Texture2D whiteTexture;

        private void Awake()
        {
            ResetSceneState();
            whiteTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            whiteTexture.SetPixel(0, 0, Color.white);
            whiteTexture.Apply();
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            RestoreCameraPosition();
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            RestoreCameraPosition();
            player = null;
            motor = null;
            playerHealth = null;
            director = null;
            worldCamera = null;
            ResetSceneState();
        }

        private void ResetSceneState()
        {
            hasSafePosition = false;
            previousHealth = -1;
            previousEnemies = -1;
            damageUntil = 0f;
            killUntil = 0f;
            cameraShake = 0f;
            appliedCameraOffset = Vector3.zero;
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
            RestoreCameraPosition();

            if (worldCamera == null || cameraShake <= 0.001f)
            {
                cameraShake = 0f;
                return;
            }

            float strength = cameraShake * 0.08f;
            appliedCameraOffset = new Vector3(
                Random.Range(-strength, strength),
                Random.Range(-strength, strength),
                0f);
            worldCamera.transform.position += appliedCameraOffset;
            cameraShake = Mathf.MoveTowards(cameraShake, 0f, Time.unscaledDeltaTime * 4.5f);
        }

        private void RestoreCameraPosition()
        {
            if (worldCamera != null && appliedCameraOffset.sqrMagnitude > 0f)
            {
                worldCamera.transform.position -= appliedCameraOffset;
            }

            appliedCameraOffset = Vector3.zero;
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
                    previousHealth = playerHealth != null ? playerHealth.Current : -1;
                }
            }

            if (director == null)
            {
                director = Object.FindObjectOfType<ArenaDirector>();
                if (director != null)
                {
                    previousEnemies = director.RemainingEnemies;
                }
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

            if (Physics.Raycast(player.position + Vector3.up, Vector3.down, out RaycastHit hit, 4f, ~0, QueryTriggerInteraction.Ignore))
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
            if (whiteTexture == null || (director != null && (director.Victory || director.Defeat)))
            {
                return;
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
        }

        private void DrawScreenTint(Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), whiteTexture);
            GUI.color = previous;
        }

        private void OnDestroy()
        {
            RestoreCameraPosition();
            if (whiteTexture != null)
            {
                Destroy(whiteTexture);
            }
        }
    }
}
