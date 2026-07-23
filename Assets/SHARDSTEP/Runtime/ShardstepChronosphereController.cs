using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Shardstep
{
    public enum ChronosphereActionMode
    {
        Move,
        Aim
    }

    public static class ChronosphereMovePolicy
    {
        public const float StandardStepDistance = 2.6f;
        public const float MinimumDirectionDistance = 0.15f;

        public static bool TryResolveNominalStep(
            Vector3 origin,
            Vector3 indicatedPoint,
            out Vector3 direction,
            out Vector3 destination)
        {
            Vector3 planar = indicatedPoint - origin;
            planar.y = 0f;
            if (planar.sqrMagnitude < MinimumDirectionDistance * MinimumDirectionDistance)
            {
                direction = Vector3.zero;
                destination = origin;
                return false;
            }

            direction = planar.normalized;
            destination = origin + direction * StandardStepDistance;
            return true;
        }
    }

    [DefaultExecutionOrder(-1500)]
    public sealed class ShardstepChronosphereController : MonoBehaviour
    {
        private const float ActionDuration = ShardstepClock.StandardActionDuration;
        private const float ObstaclePadding = 0.08f;
        private const float AfterimageLifetime = 0.28f;
        private const int AfterimageCount = 5;

        private Transform player;
        private CharacterController characterController;
        private PlayerMotor motor;
        private PlayerCombat combat;
        private Health playerHealth;
        private ArenaDirector director;
        private Camera worldCamera;

        private ChronosphereActionMode mode = ChronosphereActionMode.Move;
        private int activeFinger = -1;
        private int hudFinger = -1;
        private int hudButton = -1;
        private Vector3 plannedPoint;
        private bool plannedPointValid;
        private bool executingMove;
        private Coroutine moveRoutine;

        private GameObject marker;
        private Material markerMaterial;
        private LineRenderer path;
        private Material pathMaterial;
        private Texture2D pixel;
        private string feedback = string.Empty;
        private float feedbackUntil;

        public ChronosphereActionMode Mode => mode;
        public bool IsExecutingAction => executingMove ||
            (ShardstepClock.Instance != null && ShardstepClock.Instance.IsExecuting);

        private void Awake()
        {
            pixel = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            pixel.SetPixel(0, 0, Color.white);
            pixel.Apply();
            CreatePlanningVisuals();
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            ResetInput();
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode modeValue)
        {
            player = null;
            characterController = null;
            motor = null;
            combat = null;
            playerHealth = null;
            director = null;
            worldCamera = null;
            mode = ChronosphereActionMode.Move;
            ResetInput();
        }

        private void Update()
        {
            BindReferences();
            DisableLegacyInput();
            if (player == null || combat == null || worldCamera == null)
            {
                return;
            }

            if (director != null && (director.Victory || director.Defeat))
            {
                ResetInput();
                return;
            }

            if (IsExecutingAction)
            {
                return;
            }

            HandlePointerInput();
            HandleKeyboardInput();
        }

        private void BindReferences()
        {
            if (player == null)
            {
                GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
                if (playerObject != null)
                {
                    player = playerObject.transform;
                    characterController = playerObject.GetComponent<CharacterController>();
                    motor = playerObject.GetComponent<PlayerMotor>();
                    combat = playerObject.GetComponent<PlayerCombat>();
                    playerHealth = playerObject.GetComponent<Health>();
                }
            }

            if (director == null)
            {
                director = FindObjectOfType<ArenaDirector>();
            }

            if (worldCamera == null)
            {
                worldCamera = Camera.main;
            }
        }

        private static void DisableLegacyInput()
        {
            foreach (ShardstepInput input in FindObjectsOfType<ShardstepInput>())
            {
                input.enabled = false;
            }
        }

        private void HandlePointerInput()
        {
            if (Input.touchCount > 0)
            {
                HandleTouches();
                return;
            }

#if UNITY_EDITOR || UNITY_STANDALONE || UNITY_WEBGL
            Vector2 mouse = Input.mousePosition;
            if (Input.GetMouseButtonDown(0) && !IsHudPoint(mouse))
            {
                BeginPointer(0, mouse);
            }
            else if (Input.GetMouseButton(0) && activeFinger == 0)
            {
                UpdatePointer(mouse);
            }
            else if (Input.GetMouseButtonUp(0) && activeFinger == 0)
            {
                EndPointer(mouse);
            }
#endif
        }

        private void HandleTouches()
        {
            for (int index = 0; index < Input.touchCount; index++)
            {
                Touch touch = Input.GetTouch(index);
                if (touch.phase == TouchPhase.Began)
                {
                    int button = HitHudButton(touch.position);
                    if (button >= 0 && hudFinger < 0)
                    {
                        hudFinger = touch.fingerId;
                        hudButton = button;
                        continue;
                    }

                    if (activeFinger < 0)
                    {
                        BeginPointer(touch.fingerId, touch.position);
                    }
                }

                if (touch.fingerId == hudFinger)
                {
                    if (touch.phase == TouchPhase.Ended)
                    {
                        int releasedButton = HitHudButton(touch.position);
                        int committedButton = hudButton;
                        hudFinger = -1;
                        hudButton = -1;
                        if (releasedButton == committedButton)
                        {
                            InvokeHudButton(committedButton);
                        }
                    }
                    else if (touch.phase == TouchPhase.Canceled)
                    {
                        hudFinger = -1;
                        hudButton = -1;
                    }
                    continue;
                }

                if (touch.fingerId != activeFinger)
                {
                    continue;
                }

                if (touch.phase == TouchPhase.Moved || touch.phase == TouchPhase.Stationary)
                {
                    UpdatePointer(touch.position);
                }
                else if (touch.phase == TouchPhase.Ended)
                {
                    EndPointer(touch.position);
                }
                else if (touch.phase == TouchPhase.Canceled)
                {
                    CancelPointer();
                }
            }
        }

        private void BeginPointer(int fingerId, Vector2 screenPoint)
        {
            activeFinger = fingerId;
            if (mode == ChronosphereActionMode.Aim)
            {
                combat.BeginAim();
            }
            UpdatePointer(screenPoint);
        }

        private void UpdatePointer(Vector2 screenPoint)
        {
            if (!TryScreenToGround(screenPoint, out Vector3 indicated))
            {
                plannedPointValid = false;
                HidePlanningVisuals();
                return;
            }

            if (mode == ChronosphereActionMode.Move)
            {
                plannedPointValid = TryResolveStepDestination(indicated, out plannedPoint);
                if (plannedPointValid)
                {
                    ShowPlanningPath(plannedPoint, new Color(0.12f, 1f, 0.82f, 0.9f));
                }
                else
                {
                    HidePlanningVisuals();
                }
                return;
            }

            Vector3 aim = indicated - player.position;
            aim.y = 0f;
            plannedPointValid = aim.sqrMagnitude > 0.001f;
            if (plannedPointValid)
            {
                plannedPoint = indicated;
                combat.SetAimDirection(aim.normalized);
                ShowMarker(plannedPoint, new Color(1f, 0.35f, 0.12f, 0.9f));
                if (path != null)
                {
                    path.enabled = false;
                }
            }
        }

        private void EndPointer(Vector2 screenPoint)
        {
            UpdatePointer(screenPoint);
            activeFinger = -1;
            if (mode == ChronosphereActionMode.Move)
            {
                if (plannedPointValid)
                {
                    CommitMove(plannedPoint);
                }
                else
                {
                    SetFeedback("NO VALID STEP");
                }
                HidePlanningVisuals();
                return;
            }

            bool fired = plannedPointValid && combat.ReleaseAim(100f);
            if (!fired)
            {
                SetFeedback(combat.Mode == WeaponMode.Rail && combat.RailAmmo <= 0
                    ? "RAIL EMPTY — RELOAD"
                    : "NO VALID SHOT");
            }
            HidePlanningVisuals();
        }

        private bool TryResolveStepDestination(Vector3 indicated, out Vector3 destination)
        {
            destination = player.position;
            if (!ChronosphereMovePolicy.TryResolveNominalStep(
                    player.position,
                    indicated,
                    out Vector3 direction,
                    out _))
            {
                return false;
            }

            float radius = characterController != null
                ? Mathf.Max(0.24f, characterController.radius)
                : 0.36f;
            Vector3 castOrigin = player.position + Vector3.up * Mathf.Max(radius, 0.35f);
            float allowed = ChronosphereMovePolicy.StandardStepDistance;
            RaycastHit[] hits = Physics.SphereCastAll(
                castOrigin,
                radius * 0.82f,
                direction,
                allowed,
                ~0,
                QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (RaycastHit hit in hits)
            {
                if (hit.collider == null ||
                    hit.collider.transform == player ||
                    hit.collider.transform.IsChildOf(player) ||
                    hit.collider.GetComponentInParent<ChronosphereProjectile>() != null)
                {
                    continue;
                }

                Health health = hit.collider.GetComponentInParent<Health>();
                if (health != null && !health.CompareTag("Player"))
                {
                    continue;
                }

                if (hit.normal.y > 0.65f)
                {
                    continue;
                }

                allowed = Mathf.Max(0f, hit.distance - ObstaclePadding);
                break;
            }

            if (allowed < ChronosphereMovePolicy.MinimumDirectionDistance)
            {
                return false;
            }

            destination = player.position + direction * allowed;
            Vector3 probeOrigin = destination + Vector3.up * 3f;
            if (!Physics.Raycast(
                    probeOrigin,
                    Vector3.down,
                    out RaycastHit ground,
                    7f,
                    ~0,
                    QueryTriggerInteraction.Ignore) ||
                Mathf.Abs(ground.point.y - player.position.y) > 1.25f)
            {
                return false;
            }

            destination.y = player.position.y;
            return true;
        }

        private void CommitMove(Vector3 destination)
        {
            ShardstepClock clock = ShardstepClock.Instance;
            if (clock == null || !clock.TryBeginAction(ActionDuration))
            {
                SetFeedback("ACTION BUSY");
                return;
            }

            if (moveRoutine != null)
            {
                StopCoroutine(moveRoutine);
            }
            moveRoutine = StartCoroutine(ExecuteMove(destination, clock));
        }

        private IEnumerator ExecuteMove(Vector3 destination, ShardstepClock clock)
        {
            executingMove = true;
            Vector3 start = player.position;
            Vector3 delta = destination - start;
            delta.y = 0f;
            if (delta.sqrMagnitude <
                ChronosphereMovePolicy.MinimumDirectionDistance *
                ChronosphereMovePolicy.MinimumDirectionDistance)
            {
                executingMove = false;
                moveRoutine = null;
                yield break;
            }

            player.forward = delta.normalized;
            SpawnAfterimage();
            float elapsed = 0f;
            int nextGhost = 1;
            while (player != null && clock != null && clock.IsExecuting && elapsed < ActionDuration)
            {
                float frame = Time.deltaTime;
                if (frame <= 0f)
                {
                    yield return null;
                    continue;
                }

                elapsed = Mathf.Min(ActionDuration, elapsed + frame);
                float progress = elapsed / ActionDuration;
                Vector3 desired = Vector3.Lerp(start, destination, progress);
                Vector3 movement = desired - player.position;
                movement.y = 0f;
                if (characterController != null)
                {
                    characterController.Move(movement);
                }
                else
                {
                    player.position += movement;
                }

                if (nextGhost < AfterimageCount &&
                    progress >= (float)nextGhost / AfterimageCount)
                {
                    SpawnAfterimage();
                    nextGhost++;
                }
                yield return null;
            }

            SpawnAfterimage();
            if (motor != null)
            {
                motor.MoveInput = Vector2.zero;
            }
            executingMove = false;
            moveRoutine = null;
        }

        private void TryWait()
        {
            CancelPointer();
            if (ShardstepClock.Instance == null || !ShardstepClock.Instance.TryBeginAction())
            {
                SetFeedback("ACTION BUSY");
                return;
            }
            SetFeedback("WAIT");
        }

        private void TryReload()
        {
            CancelPointer();
            if (combat == null || !combat.TryReload())
            {
                SetFeedback(combat != null && combat.RailAmmo >= PlayerCombat.RailMagazineSize
                    ? "MAGAZINE FULL"
                    : "CANNOT RELOAD");
                return;
            }
            SetFeedback("RELOADING");
        }

        private void TrySwitchWeapon()
        {
            CancelPointer();
            if (combat == null || !combat.TryCycleWeapon())
            {
                SetFeedback("ACTION BUSY");
                return;
            }
            SetFeedback(combat.Mode == WeaponMode.Rail ? "EQUIPPED RAIL" : "EQUIPPED BLADE");
        }

        private void SetMode(ChronosphereActionMode next)
        {
            if (IsExecutingAction)
            {
                return;
            }
            CancelPointer();
            mode = next;
        }

        private void HandleKeyboardInput()
        {
#if UNITY_EDITOR || UNITY_STANDALONE || UNITY_WEBGL
            if (Input.GetKeyDown(KeyCode.M) || Input.GetKeyDown(KeyCode.Alpha1)) SetMode(ChronosphereActionMode.Move);
            if (Input.GetKeyDown(KeyCode.F) || Input.GetKeyDown(KeyCode.Alpha2)) SetMode(ChronosphereActionMode.Aim);
            if (Input.GetKeyDown(KeyCode.Space)) TryWait();
            if (Input.GetKeyDown(KeyCode.Q)) TrySwitchWeapon();
            if (Input.GetKeyDown(KeyCode.R)) TryReload();
#endif
        }

        private bool TryScreenToGround(Vector2 screenPoint, out Vector3 worldPoint)
        {
            worldPoint = player.position;
            Ray ray = worldCamera.ScreenPointToRay(screenPoint);
            RaycastHit[] hits = Physics.RaycastAll(ray, 60f, ~0, QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (RaycastHit hit in hits)
            {
                if (hit.collider == null ||
                    hit.collider.GetComponentInParent<ChronosphereProjectile>() != null)
                {
                    continue;
                }

                Bounds bounds = hit.collider.bounds;
                if (hit.normal.y > 0.45f || bounds.size.x > 5f || bounds.size.z > 5f)
                {
                    worldPoint = hit.point + Vector3.up * 0.06f;
                    return true;
                }
            }

            Plane plane = new Plane(Vector3.up, player.position);
            if (plane.Raycast(ray, out float distance))
            {
                worldPoint = ray.GetPoint(distance);
                return true;
            }
            return false;
        }

        private void CreatePlanningVisuals()
        {
            Shader shader = Shader.Find("Sprites/Default");
            marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            marker.name = "Chronosphere Action Marker";
            marker.transform.SetParent(transform, false);
            marker.transform.localScale = new Vector3(0.48f, 0.018f, 0.48f);
            Collider collider = marker.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            markerMaterial = new Material(shader);
            marker.GetComponent<Renderer>().material = markerMaterial;
            marker.SetActive(false);

            GameObject pathObject = new GameObject("Chronosphere Move Preview");
            pathObject.transform.SetParent(transform, false);
            path = pathObject.AddComponent<LineRenderer>();
            path.positionCount = 2;
            path.useWorldSpace = true;
            path.startWidth = 0.08f;
            path.endWidth = 0.08f;
            path.numCapVertices = 6;
            pathMaterial = new Material(shader);
            path.material = pathMaterial;
            path.enabled = false;
        }

        private void ShowPlanningPath(Vector3 destination, Color color)
        {
            ShowMarker(destination, color);
            path.enabled = true;
            pathMaterial.color = color;
            path.SetPosition(0, player.position + Vector3.up * 0.08f);
            path.SetPosition(1, destination + Vector3.up * 0.08f);
        }

        private void ShowMarker(Vector3 position, Color color)
        {
            marker.transform.position = position;
            markerMaterial.color = color;
            marker.SetActive(true);
        }

        private void HidePlanningVisuals()
        {
            if (marker != null) marker.SetActive(false);
            if (path != null) path.enabled = false;
        }

        private void SpawnAfterimage()
        {
            if (player == null) return;
            GameObject ghost = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            ghost.name = "Chronosphere Afterimage";
            ghost.transform.position = player.position;
            ghost.transform.rotation = player.rotation;
            ghost.transform.localScale = player.localScale;
            Collider collider = ghost.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            Material material = ShardstepVisuals.CreateMaterial(new Color(0.15f, 0.92f, 1f, 0.34f), true);
            ghost.GetComponent<Renderer>().material = material;
            StartCoroutine(FadeAfterimage(ghost, material));
        }

        private IEnumerator FadeAfterimage(GameObject ghost, Material material)
        {
            float elapsed = 0f;
            while (ghost != null && elapsed < AfterimageLifetime)
            {
                elapsed += Time.unscaledDeltaTime;
                Color color = material.color;
                color.a = 0.34f * (1f - Mathf.Clamp01(elapsed / AfterimageLifetime));
                material.color = color;
                yield return null;
            }
            if (material != null) Destroy(material);
            if (ghost != null) Destroy(ghost);
        }

        private static Rect[] ActionButtonsScreen()
        {
            Rect safe = Screen.safeArea;
            float margin = Mathf.Clamp(safe.width * 0.03f, 10f, 24f);
            float gap = Mathf.Clamp(safe.width * 0.018f, 7f, 14f);
            float height = Mathf.Clamp(safe.height * 0.062f, 52f, 76f);
            float lowerY = safe.yMin + margin;
            float upperY = lowerY + height + gap;
            float third = (safe.width - margin * 2f - gap * 2f) / 3f;
            float half = (safe.width - margin * 2f - gap) / 2f;
            return new[]
            {
                new Rect(safe.xMin + margin, lowerY, third, height),
                new Rect(safe.xMin + margin + third + gap, lowerY, third, height),
                new Rect(safe.xMin + margin + (third + gap) * 2f, lowerY, third, height),
                new Rect(safe.xMin + margin, upperY, half, height),
                new Rect(safe.xMin + margin + half + gap, upperY, half, height)
            };
        }

        private static int HitHudButton(Vector2 point)
        {
            Rect[] buttons = ActionButtonsScreen();
            for (int i = 0; i < buttons.Length; i++)
            {
                if (buttons[i].Contains(point)) return i;
            }
            return -1;
        }

        private static bool IsHudPoint(Vector2 point) => HitHudButton(point) >= 0;

        private void InvokeHudButton(int button)
        {
            switch (button)
            {
                case 0: SetMode(ChronosphereActionMode.Move); break;
                case 1: SetMode(ChronosphereActionMode.Aim); break;
                case 2: TryWait(); break;
                case 3: TrySwitchWeapon(); break;
                case 4: TryReload(); break;
            }
        }

        private void CancelPointer()
        {
            activeFinger = -1;
            plannedPointValid = false;
            if (combat != null && combat.IsAiming) combat.CancelAim();
            HidePlanningVisuals();
        }

        private void ResetInput()
        {
            CancelPointer();
            hudFinger = -1;
            hudButton = -1;
            executingMove = false;
            if (moveRoutine != null)
            {
                StopCoroutine(moveRoutine);
                moveRoutine = null;
            }
            if (motor != null) motor.MoveInput = Vector2.zero;
        }

        private void SetFeedback(string message)
        {
            feedback = message;
            feedbackUntil = Time.unscaledTime + 0.85f;
        }

        private void OnGUI()
        {
            if (pixel == null || combat == null ||
                (director != null && (director.Victory || director.Defeat)))
            {
                return;
            }

            Rect[] buttons = ActionButtonsScreen();
            DrawButton(buttons[0], "MOVE", mode == ChronosphereActionMode.Move, () => SetMode(ChronosphereActionMode.Move));
            DrawButton(buttons[1], "AIM / FIRE", mode == ChronosphereActionMode.Aim, () => SetMode(ChronosphereActionMode.Aim));
            DrawButton(buttons[2], "WAIT", false, TryWait);
            DrawButton(buttons[3], combat.Mode == WeaponMode.Rail ? "SWAP → BLADE" : "SWAP → RAIL", false, TrySwitchWeapon);
            DrawButton(buttons[4], combat.IsReloading ? "RELOADING" : "RELOAD RAIL", false, TryReload);

            Rect safe = Screen.safeArea;
            float top = Screen.height - safe.yMax;
            GUIStyle status = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                fontSize = Mathf.Clamp(Mathf.RoundToInt(Screen.width * 0.031f), 12, 20),
                wordWrap = true
            };
            ShardstepClock clock = ShardstepClock.Instance;
            string state = clock != null && clock.IsExecuting
                ? $"ACTION {clock.CompletedActions + 1} MOVING"
                : $"TIME FROZEN  •  ACTIONS {clock?.CompletedActions ?? 0}";
            string instruction = mode == ChronosphereActionMode.Move
                ? "MOVE: TAP A DIRECTION — ONE FIXED STEP"
                : "AIM: TAP A DIRECTION — RELEASE TO FIRE";
            string message = Time.unscaledTime < feedbackUntil ? $"\n{feedback}" : string.Empty;
            GUI.Box(
                new Rect(safe.xMin + 12f, top + 10f, safe.width - 24f, 92f),
                $"{state}\nHP {playerHealth?.Current}/{playerHealth?.Maximum}  " +
                $"HOSTILES {director?.RemainingEnemies}  {combat.Mode} {combat.AmmoLabel}\n" +
                $"{instruction}{message}",
                status);
        }

        private void DrawButton(Rect screenRect, string label, bool selected, Action action)
        {
            Rect guiRect = new Rect(
                screenRect.x,
                Screen.height - screenRect.yMax,
                screenRect.width,
                screenRect.height);
            Color old = GUI.color;
            GUI.color = selected
                ? new Color(0.12f, 0.9f, 0.84f, 0.95f)
                : new Color(0.08f, 0.1f, 0.14f, 0.92f);
            GUI.DrawTexture(guiRect, pixel);
            GUI.color = old;
            GUIStyle style = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                fontSize = Mathf.Clamp(Mathf.RoundToInt(Screen.width * 0.03f), 12, 21)
            };
            if (GUI.Button(guiRect, label, style)) action?.Invoke();
        }

        private void OnDestroy()
        {
            if (markerMaterial != null) Destroy(markerMaterial);
            if (pathMaterial != null) Destroy(pathMaterial);
            if (pixel != null) Destroy(pixel);
        }
    }
}
