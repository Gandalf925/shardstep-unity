using System.Collections;
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

    public static class ShardstepPortraitTouchBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            if (!Application.isMobilePlatform && !Input.touchSupported)
            {
                return;
            }

            Screen.autorotateToLandscapeLeft = false;
            Screen.autorotateToLandscapeRight = false;
            Screen.autorotateToPortrait = true;
            Screen.autorotateToPortraitUpsideDown = false;
            Screen.orientation = ScreenOrientation.Portrait;

            if (Object.FindObjectOfType<ShardstepPortraitTouchInstaller>() != null)
            {
                return;
            }

            GameObject installer = new GameObject("SHARDSTEP Portrait Touch Installer");
            Object.DontDestroyOnLoad(installer);
            installer.AddComponent<ShardstepPortraitTouchInstaller>();
        }
    }

    public sealed class ShardstepPortraitTouchInstaller : MonoBehaviour
    {
        private float nextProbe;

        private void Update()
        {
            if (Time.unscaledTime < nextProbe)
            {
                return;
            }

            nextProbe = Time.unscaledTime + 0.2f;
            ShardstepClock clock = ShardstepClock.Instance;
            if (clock == null || clock.GetComponent<ShardstepPortraitTouchInput>() != null)
            {
                return;
            }

            clock.gameObject.AddComponent<ShardstepPortraitTouchInput>();
        }
    }

    public sealed class ShardstepPortraitTouchInput : MonoBehaviour
    {
        private const float TurnSpeed = 720f;
        private const float DashDuration = 0.24f;
        private const float DashStopDistance = 0.18f;
        private const float AttackReleaseMagnitude = 100f;

        private PlayerMotor motor;
        private PlayerCombat combat;
        private ArenaDirector director;
        private Health playerHealth;
        private Transform player;
        private Camera worldCamera;
        private TrailRenderer trail;
        private GameObject previewMarker;
        private Renderer previewRenderer;

        private int previewFinger = -1;
        private Vector2 previewStart;
        private Vector3 previewWorld;
        private bool previewValid;
        private bool previewCanceled;
        private bool dashing;
        private Coroutine dashRoutine;

        private void Start()
        {
            DisablePreviousMobileInput();
            BindReferences();
            CreatePreviewMarker();
        }

        private void Update()
        {
            if (motor == null || combat == null || player == null)
            {
                BindReferences();
                if (motor == null || combat == null || player == null)
                {
                    return;
                }
            }

            if (director != null && (director.Victory || director.Defeat))
            {
                CancelPreview();
                SetMovement(Vector2.zero);
                return;
            }

            HandleTouches();
            UpdatePreviewFacing();
        }

        private void DisablePreviousMobileInput()
        {
            ShardstepAdaptiveInput adaptive = GetComponent<ShardstepAdaptiveInput>();
            if (adaptive != null)
            {
                adaptive.enabled = false;
            }
        }

        private void BindReferences()
        {
            GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
            if (playerObject != null)
            {
                player = playerObject.transform;
                motor = playerObject.GetComponent<PlayerMotor>();
                combat = playerObject.GetComponent<PlayerCombat>();
                playerHealth = playerObject.GetComponent<Health>();
                EnsureTrail(playerObject);
            }

            director = Object.FindObjectOfType<ArenaDirector>();
            worldCamera = Camera.main;
        }

        private void EnsureTrail(GameObject playerObject)
        {
            trail = playerObject.GetComponent<TrailRenderer>();
            if (trail == null)
            {
                trail = playerObject.AddComponent<TrailRenderer>();
                trail.time = 0.28f;
                trail.startWidth = 0.42f;
                trail.endWidth = 0f;
                trail.minVertexDistance = 0.04f;
                trail.emitting = false;
                Shader shader = Shader.Find("Sprites/Default");
                if (shader != null)
                {
                    trail.material = new Material(shader);
                    trail.material.color = new Color(0.2f, 0.9f, 1f, 0.65f);
                }
            }
        }

        private void CreatePreviewMarker()
        {
            previewMarker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            previewMarker.name = "Move Preview";
            previewMarker.transform.localScale = new Vector3(0.55f, 0.025f, 0.55f);
            Collider markerCollider = previewMarker.GetComponent<Collider>();
            if (markerCollider != null)
            {
                Destroy(markerCollider);
            }

            previewRenderer = previewMarker.GetComponent<Renderer>();
            Shader shader = Shader.Find("Sprites/Default");
            if (previewRenderer != null && shader != null)
            {
                previewRenderer.material = new Material(shader);
            }

            previewMarker.SetActive(false);
        }

        private void HandleTouches()
        {
            for (int i = 0; i < Input.touchCount; i++)
            {
                Touch touch = Input.GetTouch(i);

                if (touch.phase == TouchPhase.Began)
                {
                    if (AttackRectScreen().Contains(touch.position))
                    {
                        AttackForward();
                        continue;
                    }

                    if (previewFinger < 0 && !dashing)
                    {
                        previewFinger = touch.fingerId;
                        previewStart = touch.position;
                        previewCanceled = false;
                        previewValid = TryScreenToWorld(touch.position, out previewWorld);
                        ShowPreview();
                    }
                }

                if (touch.fingerId != previewFinger)
                {
                    continue;
                }

                if (touch.phase == TouchPhase.Moved || touch.phase == TouchPhase.Stationary)
                {
                    previewCanceled = !ShardstepPortraitTouchPolicy.ShouldCommit(
                        previewStart, touch.position, Screen.width, Screen.height);
                    UpdatePreviewColor();
                }
                else if (touch.phase == TouchPhase.Ended)
                {
                    bool commit = previewValid && !previewCanceled &&
                        ShardstepPortraitTouchPolicy.ShouldCommit(
                            previewStart, touch.position, Screen.width, Screen.height);
                    Vector3 target = previewWorld;
                    CancelPreview();
                    if (commit)
                    {
                        StartDash(target);
                    }
                }
                else if (touch.phase == TouchPhase.Canceled)
                {
                    CancelPreview();
                }
            }
        }

        private bool TryScreenToWorld(Vector2 screenPoint, out Vector3 worldPoint)
        {
            worldPoint = Vector3.zero;
            if (worldCamera == null || player == null)
            {
                worldCamera = Camera.main;
                if (worldCamera == null)
                {
                    return false;
                }
            }

            Ray ray = worldCamera.ScreenPointToRay(screenPoint);
            Plane plane = new Plane(Vector3.up, player.position);
            if (!plane.Raycast(ray, out float distance))
            {
                return false;
            }

            worldPoint = ray.GetPoint(distance);
            worldPoint.y = player.position.y;
            return true;
        }

        private void ShowPreview()
        {
            if (!previewValid || previewMarker == null)
            {
                return;
            }

            previewMarker.transform.position = previewWorld + Vector3.up * 0.03f;
            previewMarker.SetActive(true);
            UpdatePreviewColor();
        }

        private void UpdatePreviewColor()
        {
            if (previewRenderer == null)
            {
                return;
            }

            previewRenderer.material.color = previewCanceled
                ? new Color(1f, 0.2f, 0.2f, 0.75f)
                : new Color(0.15f, 1f, 0.85f, 0.75f);
        }

        private void UpdatePreviewFacing()
        {
            if (!previewValid || player == null || dashing)
            {
                return;
            }

            Vector3 direction = previewWorld - player.position;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.001f)
            {
                return;
            }

            Quaternion targetRotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
            player.rotation = Quaternion.RotateTowards(
                player.rotation, targetRotation, TurnSpeed * Time.unscaledDeltaTime);

            if (ShardstepClock.Instance != null)
            {
                ShardstepClock.Instance.IsAiming = false;
                ShardstepClock.Instance.MovementMagnitude = 0.18f;
            }
        }

        private void StartDash(Vector3 target)
        {
            if (dashRoutine != null)
            {
                StopCoroutine(dashRoutine);
            }

            dashRoutine = StartCoroutine(DashTo(target));
        }

        private IEnumerator DashTo(Vector3 target)
        {
            dashing = true;
            if (trail != null)
            {
                trail.Clear();
                trail.emitting = true;
            }

            float elapsed = 0f;
            while (elapsed < DashDuration && player != null)
            {
                Vector3 delta = target - player.position;
                delta.y = 0f;
                if (delta.magnitude <= DashStopDistance)
                {
                    break;
                }

                Vector2 input = new Vector2(delta.x, delta.z).normalized;
                SetMovement(input);
                player.rotation = Quaternion.RotateTowards(
                    player.rotation,
                    Quaternion.LookRotation(delta.normalized, Vector3.up),
                    TurnSpeed * Time.deltaTime);

                elapsed += Time.deltaTime;
                yield return null;
            }

            SetMovement(Vector2.zero);
            if (trail != null)
            {
                trail.emitting = false;
            }

            dashing = false;
            dashRoutine = null;
        }

        private void AttackForward()
        {
            if (combat == null || player == null || dashing)
            {
                return;
            }

            combat.BeginAim();
            combat.SetAimDirection(player.forward);
            combat.ReleaseAim(AttackReleaseMagnitude);
            if (ShardstepClock.Instance != null)
            {
                ShardstepClock.Instance.IsAiming = false;
                ShardstepClock.Instance.MovementMagnitude = 0.18f;
            }
        }

        private void SetMovement(Vector2 movement)
        {
            if (motor != null)
            {
                motor.MoveInput = movement;
            }

            if (ShardstepClock.Instance != null)
            {
                ShardstepClock.Instance.MovementMagnitude = movement.magnitude;
            }
        }

        private void CancelPreview()
        {
            previewFinger = -1;
            previewValid = false;
            previewCanceled = false;
            if (previewMarker != null)
            {
                previewMarker.SetActive(false);
            }
        }

        private Rect AttackRectGui()
        {
            Rect safe = Screen.safeArea;
            float size = Mathf.Clamp(safe.width * 0.22f, 92f, 142f);
            float margin = Mathf.Clamp(safe.width * 0.035f, 12f, 28f);
            return new Rect(
                safe.xMax - size - margin,
                Screen.height - safe.yMax + safe.height - size - margin,
                size,
                size);
        }

        private Rect AttackRectScreen()
        {
            Rect gui = AttackRectGui();
            return new Rect(gui.x, Screen.height - gui.yMax, gui.width, gui.height);
        }

        private void OnGUI()
        {
            if (combat == null)
            {
                return;
            }

            Rect safe = Screen.safeArea;
            float margin = Mathf.Clamp(safe.width * 0.035f, 12f, 28f);
            Rect attack = AttackRectGui();
            Rect rail = new Rect(safe.xMin + margin, Screen.height - safe.yMax + margin, 92f, 48f);
            Rect blade = new Rect(rail.x + 100f, rail.y, 92f, 48f);

            GUI.Box(new Rect(safe.xMin + margin, rail.y + 58f, safe.width - margin * 2f, 54f),
                previewCanceled
                    ? "MOVE CANCELED — RELEASE OR TOUCH AGAIN"
                    : previewValid
                        ? "RELEASE TO DASH — DRAG FARTHER TO CANCEL"
                        : "TOUCH A DESTINATION — ATTACK USES FACING DIRECTION");

            if (GUI.Button(rail, "RAIL"))
            {
                combat.SetWeapon(WeaponMode.Rail);
            }

            if (GUI.Button(blade, "BLADE"))
            {
                combat.SetWeapon(WeaponMode.Blade);
            }

            if (GUI.Button(attack, "ATTACK"))
            {
                AttackForward();
            }

            GUI.Label(new Rect(safe.xMin + margin, rail.y + 116f, safe.width - margin * 2f, 28f),
                $"HP {playerHealth?.Current}/{playerHealth?.Maximum}   ENEMIES {director?.RemainingEnemies}   {combat.Mode}");
        }

        private void OnDisable()
        {
            CancelPreview();
            SetMovement(Vector2.zero);
            if (trail != null)
            {
                trail.emitting = false;
            }
        }
    }
}
