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
            Input.simulateMouseWithTouches = true;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;

            if (Object.FindObjectOfType<ShardstepRuntimeInstaller>() != null)
            {
                return;
            }

            GameObject installerObject = new GameObject("SHARDSTEP Runtime Installer");
            Object.DontDestroyOnLoad(installerObject);
            installerObject.AddComponent<ShardstepRuntimeInstaller>();
        }
    }

    public sealed class ShardstepRuntimeInstaller : MonoBehaviour
    {
        private float nextProbeTime;

        private void Update()
        {
            if (Time.unscaledTime < nextProbeTime)
            {
                return;
            }

            nextProbeTime = Time.unscaledTime + 0.25f;
            ShardstepClock clock = ShardstepClock.Instance;
            if (clock == null || clock.GetComponent<ShardstepAdaptiveInput>() != null)
            {
                return;
            }

            clock.gameObject.AddComponent<ShardstepAdaptiveInput>();
        }
    }

    public sealed class ShardstepAdaptiveInput : MonoBehaviour
    {
        private PlayerMotor motor;
        private PlayerCombat combat;
        private ArenaDirector director;
        private Health playerHealth;

        private int movementFinger = -1;
        private int aimFinger = -1;
        private Vector2 movementStart;
        private Vector2 aimStart;
        private Vector2 aimCurrent;
        private bool touchDevice;

        private void Start()
        {
            DisableLegacyInput();
            BindReferences();
            RefreshTouchCapability();
        }

        private void Update()
        {
            if (motor == null || combat == null)
            {
                BindReferences();
                if (motor == null || combat == null)
                {
                    return;
                }
            }

            RefreshTouchCapability();
            if (director != null && (director.Victory || director.Defeat))
            {
                ApplyMovement(Vector2.zero);
                return;
            }

            Vector2 movement = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
            HandleTouches(ref movement);
            if (!touchDevice)
            {
                HandleDesktopAim();
            }

            if (Input.GetKeyDown(KeyCode.Q))
            {
                ToggleWeapon();
            }

            ApplyMovement(Vector2.ClampMagnitude(movement, 1f));
        }

        private void DisableLegacyInput()
        {
            ShardstepInput[] legacyInputs = GetComponents<ShardstepInput>();
            foreach (ShardstepInput legacyInput in legacyInputs)
            {
                legacyInput.enabled = false;
            }
        }

        private void BindReferences()
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
            {
                motor = player.GetComponent<PlayerMotor>();
                combat = player.GetComponent<PlayerCombat>();
                playerHealth = player.GetComponent<Health>();
            }

            director = Object.FindObjectOfType<ArenaDirector>();
        }

        private void RefreshTouchCapability()
        {
            if (touchDevice)
            {
                return;
            }

            touchDevice = Input.touchCount > 0 ||
                (Input.touchSupported &&
                    (Application.isMobilePlatform || SystemInfo.deviceType == DeviceType.Handheld));
        }

        private void ApplyMovement(Vector2 movement)
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

        private void HandleTouches(ref Vector2 movement)
        {
            for (int index = 0; index < Input.touchCount; index++)
            {
                Touch touch = Input.GetTouch(index);
                if (touch.phase == TouchPhase.Began)
                {
                    if (IsReservedUiPoint(touch.position))
                    {
                        continue;
                    }

                    if (touch.position.x < Screen.width * 0.5f && movementFinger < 0)
                    {
                        movementFinger = touch.fingerId;
                        movementStart = touch.position;
                    }
                    else if (aimFinger < 0)
                    {
                        aimFinger = touch.fingerId;
                        aimStart = touch.position;
                        aimCurrent = touch.position;
                        combat.BeginAim();
                    }
                }

                if (touch.fingerId == movementFinger)
                {
                    if (touch.phase == TouchPhase.Moved || touch.phase == TouchPhase.Stationary)
                    {
                        float radius = ShardstepMobilePolicy.ControlRadius(Screen.width, Screen.height);
                        movement = Vector2.ClampMagnitude((touch.position - movementStart) / radius, 1f);
                    }
                    else if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled)
                    {
                        movementFinger = -1;
                        movement = Vector2.zero;
                    }
                }

                if (touch.fingerId == aimFinger)
                {
                    if (touch.phase == TouchPhase.Moved || touch.phase == TouchPhase.Stationary)
                    {
                        aimCurrent = touch.position;
                        Vector2 delta = aimCurrent - aimStart;
                        combat.SetAimDirection(new Vector3(delta.x, 0f, delta.y));
                    }
                    else if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled)
                    {
                        float magnitude = (touch.position - aimStart).magnitude;
                        combat.ReleaseAim(magnitude);
                        aimFinger = -1;
                    }
                }
            }
        }

        private void HandleDesktopAim()
        {
            if (Input.GetMouseButtonDown(1))
            {
                aimStart = Input.mousePosition;
                aimCurrent = aimStart;
                combat.BeginAim();
            }

            if (Input.GetMouseButton(1))
            {
                aimCurrent = Input.mousePosition;
                Vector2 delta = aimCurrent - aimStart;
                combat.SetAimDirection(new Vector3(delta.x, 0f, delta.y));
            }

            if (Input.GetMouseButtonUp(1))
            {
                combat.ReleaseAim((aimCurrent - aimStart).magnitude);
            }
        }

        private void ToggleWeapon()
        {
            if (combat == null)
            {
                return;
            }

            combat.SetWeapon(combat.Mode == WeaponMode.Rail ? WeaponMode.Blade : WeaponMode.Rail);
        }

        private void ResetControls()
        {
            movementFinger = -1;
            aimFinger = -1;
            movementStart = Vector2.zero;
            aimStart = Vector2.zero;
            aimCurrent = Vector2.zero;
            ApplyMovement(Vector2.zero);

            if (combat != null)
            {
                combat.CancelAim();
            }

            if (ShardstepClock.Instance != null)
            {
                ShardstepClock.Instance.IsAiming = false;
            }

            Input.ResetInputAxes();
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus)
            {
                ResetControls();
            }
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused)
            {
                ResetControls();
            }
        }

        private void OnDisable()
        {
            ResetControls();
        }

        private bool IsReservedUiPoint(Vector2 screenPoint)
        {
            BuildHudLayout(out float scale, out Rect statusRect, out Rect railRect, out Rect bladeRect,
                out Rect equippedRect, out Rect instructionRect, out Rect moveHintRect, out Rect aimHintRect,
                out Rect resultRect, out Rect restartRect);

            Vector2 guiPoint = new Vector2(screenPoint.x / scale, (Screen.height - screenPoint.y) / scale);
            if (railRect.Contains(guiPoint) || bladeRect.Contains(guiPoint))
            {
                return true;
            }

            return director != null && (director.Victory || director.Defeat) && restartRect.Contains(guiPoint);
        }

        private void BuildHudLayout(
            out float scale,
            out Rect statusRect,
            out Rect railRect,
            out Rect bladeRect,
            out Rect equippedRect,
            out Rect instructionRect,
            out Rect moveHintRect,
            out Rect aimHintRect,
            out Rect resultRect,
            out Rect restartRect)
        {
            Rect safe = Screen.safeArea;
            if (safe.width <= 0f || safe.height <= 0f)
            {
                safe = new Rect(0f, 0f, Screen.width, Screen.height);
            }

            scale = Mathf.Clamp(Mathf.Min(safe.width / 960f, safe.height / 540f), 0.72f, 1.3f);
            float left = safe.xMin / scale;
            float top = (Screen.height - safe.yMax) / scale;
            float width = safe.width / scale;
            float height = safe.height / scale;

            const float margin = 10f;
            statusRect = new Rect(left + margin, top + margin, 220f, 82f);
            bladeRect = new Rect(left + width - margin - 86f, top + margin, 86f, 52f);
            railRect = new Rect(bladeRect.x - 94f, top + margin, 86f, 52f);
            equippedRect = new Rect(railRect.x, top + 66f, 180f, 24f);
            instructionRect = new Rect(left + margin, top + height - 34f, 380f, 24f);
            moveHintRect = new Rect(left + margin, top + height - 100f, 120f, 52f);
            aimHintRect = new Rect(left + width - margin - 150f, top + height - 100f, 150f, 52f);
            resultRect = new Rect(left + width * 0.5f - 140f, top + 112f, 280f, 104f);
            restartRect = new Rect(resultRect.x + 55f, resultRect.y + 52f, 170f, 40f);
        }

        private void OnGUI()
        {
            if (combat == null)
            {
                return;
            }

            BuildHudLayout(out float scale, out Rect statusRect, out Rect railRect, out Rect bladeRect,
                out Rect equippedRect, out Rect instructionRect, out Rect moveHintRect, out Rect aimHintRect,
                out Rect resultRect, out Rect restartRect);

            Matrix4x4 previousMatrix = GUI.matrix;
            Color previousColor = GUI.color;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

            GUI.Box(statusRect, "SHARDSTEP MOBILE SLICE");
            GUI.Label(new Rect(statusRect.x + 10f, statusRect.y + 25f, 205f, 24f),
                ShardstepClock.Instance != null && Time.timeScale <= 0.001f ? "TIME: FROZEN" : "TIME: MOVING");
            GUI.Label(new Rect(statusRect.x + 10f, statusRect.y + 48f, 205f, 24f),
                $"HP {playerHealth?.Current}/{playerHealth?.Maximum}  ENEMIES {director?.RemainingEnemies}");

            if (GUI.Button(railRect, "RAIL"))
            {
                combat.SetWeapon(WeaponMode.Rail);
            }

            if (GUI.Button(bladeRect, "BLADE"))
            {
                combat.SetWeapon(WeaponMode.Blade);
            }

            GUI.Label(equippedRect, $"EQUIPPED: {combat.Mode}");
            GUI.Label(instructionRect, touchDevice
                ? "LEFT DRAG: MOVE   RIGHT DRAG: AIM / RELEASE"
                : "WASD: MOVE   RIGHT DRAG: AIM / RELEASE   Q: SWITCH");

            if (touchDevice)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.55f);
                GUI.Box(moveHintRect, "MOVE");
                GUI.Box(aimHintRect, "AIM / RELEASE");
                GUI.color = previousColor;
            }

            if (director != null && (director.Victory || director.Defeat))
            {
                string result = director.Victory ? "EXTRACTION COMPLETE" : "TIMELINE COLLAPSED";
                GUI.Box(resultRect, result);
                if (GUI.Button(restartRect, "RESTART"))
                {
                    ResetControls();
                    UnityEngine.SceneManagement.SceneManager.LoadScene(
                        UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
                }
            }

            GUI.matrix = previousMatrix;
            GUI.color = previousColor;
        }
    }
}
