using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
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
        private const float MoveActionDuration = 0.18f;
        private const float MoveStopDistance = 0.035f;
        private const float GroundProbeDistance = 60f;
        private const float AttackDragThreshold = 18f;
        private const float ObstaclePadding = 0.08f;
        private const float AfterimageLifetime = 0.28f;
        private const int AfterimageCount = 5;

        private Transform player;
        private PlayerMotor motor;
        private PlayerCombat combat;
        private Health playerHealth;
        private ArenaDirector director;
        private Camera worldCamera;
        private CharacterController characterController;

        private ChronosphereActionMode mode = ChronosphereActionMode.Move;
        private int activeFinger = -1;
        private Vector2 pointerStart;
        private Vector2 pointerCurrent;
        private Vector3 indicatedPoint;
        private Vector3 plannedPoint;
        private bool plannedPointValid;
        private bool executingMove;
        private Coroutine moveRoutine;

        private GameObject marker;
        private Renderer markerRenderer;
        private Material markerMaterial;
        private LineRenderer pathPreview;
        private Texture2D pixel;
        private Shader afterimageShader;

        public ChronosphereActionMode Mode => mode;
        public bool IsPlanning => activeFinger >= 0;
        public bool IsExecutingAction => executingMove ||
            (ShardstepClock.Instance != null && ShardstepClock.Instance.IsExecuting);

        private void Awake()
        {
            pixel = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            pixel.SetPixel(0, 0, Color.white);
            pixel.Apply();
            afterimageShader = Shader.Find("Sprites/Default");
            CreatePlanningVisuals();
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            ResetInputAndTime();
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode loadMode)
        {
            player = null;
            motor = null;
            combat = null;
            playerHealth = null;
            director = null;
            worldCamera = null;
            characterController = null;
            mode = ChronosphereActionMode.Move;
            ResetInputAndTime();
        }

        private void Update()
        {
            BindReferences();
            DisableCompetingInputs();

            if (player == null || combat == null || worldCamera == null)
            {
                FreezeWorld();
                return;
            }

            if (director != null && (director.Victory || director.Defeat))
            {
                ResetInputAndTime();
                return;
            }

            if (!executingMove &&
                (ShardstepClock.Instance == null || !ShardstepClock.Instance.IsExecuting))
            {
                FreezeWorld();
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
                    motor = playerObject.GetComponent<PlayerMotor>();
                    combat = playerObject.GetComponent<PlayerCombat>();
                    playerHealth = playerObject.GetComponent<Health>();
                    characterController = playerObject.GetComponent<CharacterController>();
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

        private static void DisableCompetingInputs()
        {
            foreach (ShardstepInput input in Object.FindObjectsOfType<ShardstepInput>())
            {
                input.enabled = false;
            }
        }

        private void HandlePointerInput()
        {
            if (executingMove)
            {
                return;
            }

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
                if (touch.phase == TouchPhase.Began && activeFinger < 0 &&
                    !IsHudPoint(touch.position))
                {
                    BeginPointer(touch.fingerId, touch.position);
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
            pointerStart = screenPoint;
            pointerCurrent = screenPoint;
            FreezeWorld();

            if (mode == ChronosphereActionMode.Aim)
            {
                combat.BeginAim();
            }

            UpdatePointer(screenPoint);
        }

        private void UpdatePointer(Vector2 screenPoint)
        {
            pointerCurrent = screenPoint;
            bool groundPointValid = TryScreenToGround(screenPoint, out indicatedPoint);

            if (mode == ChronosphereActionMode.Move)
            {
                plannedPointValid = groundPointValid &&
                    TryResolveStepDestination(indicatedPoint, out plannedPoint);
                UpdateMovePreview();
            }
            else
            {
                plannedPointValid = groundPointValid;
                plannedPoint = indicatedPoint;
                UpdateAimPreview();
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
                    HidePlanningVisuals();
                }
            }
            else
            {
                float drag = Vector2.Distance(pointerStart, screenPoint);
                if (plannedPointValid && drag >= AttackDragThreshold)
                {
                    combat.ReleaseAim(100f);
                }
                else
                {
                    combat.CancelAim();
                }

                HidePlanningVisuals();
            }
        }

        private void CancelPointer()
        {
            activeFinger = -1;
            plannedPointValid = false;
            if (combat != null && combat.IsAiming)
            {
                combat.CancelAim();
            }
            HidePlanningVisuals();
            FreezeWorld();
        }

        private bool TryResolveStepDestination(Vector3 targetPoint, out Vector3 destination)
        {
            destination = player != null ? player.position : Vector3.zero;
            if (player == null || !ChronosphereMovePolicy.TryResolveNominalStep(
                    player.position,
                    targetPoint,
                    out Vector3 direction,
                    out Vector3 nominalDestination))
            {
                return false;
            }

            float radius = characterController != null
                ? Mathf.Max(0.24f, characterController.radius)
                : 0.36f;
            Vector3 castOrigin = player.position + Vector3.up * Mathf.Max(radius, 0.35f);
            float allowedDistance = ChronosphereMovePolicy.StandardStepDistance;

            RaycastHit[] hits = Physics.SphereCastAll(
                castOrigin,
                radius * 0.82f,
                direction,
                allowedDistance,
                ~0,
                QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));

            for (int index = 0; index < hits.Length; index++)
            {
                RaycastHit hit = hits[index];
                if (hit.collider == null || hit.collider.transform == player ||
                    hit.collider.transform.IsChildOf(player))
                {
                    continue;
                }

                if (hit.normal.y > 0.65f)
                {
                    continue;
                }

                allowedDistance = Mathf.Max(0f, hit.distance - ObstaclePadding);
                break;
            }

            if (allowedDistance < ChronosphereMovePolicy.MinimumDirectionDistance)
            {
                return false;
            }

            destination = player.position + direction * allowedDistance;

            Vector3 groundProbeOrigin = destination + Vector3.up * 3f;
            if (!Physics.Raycast(
                    groundProbeOrigin,
                    Vector3.down,
                    out RaycastHit groundHit,
                    7f,
                    ~0,
                    QueryTriggerInteraction.Ignore))
            {
                return false;
            }

            if (Mathf.Abs(groundHit.point.y - player.position.y) > 1.25f)
            {
                return false;
            }

            destination.y = player.position.y;
            return Vector3.Distance(player.position, destination) >=
                ChronosphereMovePolicy.MinimumDirectionDistance;
        }

        private void CommitMove(Vector3 destination)
        {
            if (moveRoutine != null)
            {
                StopCoroutine(moveRoutine);
            }
            moveRoutine = StartCoroutine(ExecuteMove(destination));
        }

        private IEnumerator ExecuteMove(Vector3 destination)
        {
            executingMove = true;
            HidePlanningVisuals();

            if (motor != null)
            {
                motor.MoveInput = Vector2.zero;
            }

            Vector3 start = player.position;
            Vector3 planar = destination - start;
            planar.y = 0f;
            float totalDistance = planar.magnitude;
            if (totalDistance < ChronosphereMovePolicy.MinimumDirectionDistance)
            {
                FinishMove();
                yield break;
            }

            Vector3 direction = planar / totalDistance;
            player.rotation = Quaternion.LookRotation(direction, Vector3.up);
            SpawnAfterimage();

            if (ShardstepClock.Instance != null)
            {
                ShardstepClock.Instance.IsAiming = false;
                ShardstepClock.Instance.MovementMagnitude = 1f;
            }

            float elapsed = 0f;
            int nextAfterimage = 1;
            Vector3 lastPosition = player.position;
            int blockedFrames = 0;

            while (player != null && elapsed < MoveActionDuration)
            {
                if (ShardstepClock.Instance != null)
                {
                    ShardstepClock.Instance.MovementMagnitude = 1f;
                }

                float deltaTime = Time.deltaTime;
                if (deltaTime <= 0f)
                {
                    yield return null;
                    continue;
                }

                elapsed = Mathf.Min(MoveActionDuration, elapsed + deltaTime);
                float progress = elapsed / MoveActionDuration;
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

                float afterimageThreshold = (float)nextAfterimage / AfterimageCount;
                if (progress >= afterimageThreshold && nextAfterimage < AfterimageCount)
                {
                    SpawnAfterimage();
                    nextAfterimage++;
                }

                float moved = Vector3.Distance(lastPosition, player.position);
                blockedFrames = moved < MoveStopDistance ? blockedFrames + 1 : 0;
                lastPosition = player.position;
                if (blockedFrames >= 2)
                {
                    break;
                }

                yield return null;
            }

            SpawnAfterimage();
            FinishMove();
        }

        private void FinishMove()
        {
            if (motor != null)
            {
                motor.MoveInput = Vector2.zero;
            }

            executingMove = false;
            moveRoutine = null;
            FreezeWorld();
        }

        private void SpawnAfterimage()
        {
            if (player == null || afterimageShader == null)
            {
                return;
            }

            GameObject ghostRoot = new GameObject("Chronosphere Afterimage");
            List<Material> ghostMaterials = new List<Material>();
            List<Mesh> bakedMeshes = new List<Mesh>();
            bool createdRenderer = false;

            foreach (MeshRenderer sourceRenderer in player.GetComponentsInChildren<MeshRenderer>())
            {
                MeshFilter sourceFilter = sourceRenderer.GetComponent<MeshFilter>();
                if (sourceFilter == null || sourceFilter.sharedMesh == null)
                {
                    continue;
                }

                GameObject ghost = new GameObject(sourceRenderer.gameObject.name + " Ghost");
                ghost.transform.SetParent(ghostRoot.transform, false);
                ghost.transform.position = sourceRenderer.transform.position;
                ghost.transform.rotation = sourceRenderer.transform.rotation;
                ghost.transform.localScale = sourceRenderer.transform.lossyScale;

                ghost.AddComponent<MeshFilter>().sharedMesh = sourceFilter.sharedMesh;
                MeshRenderer ghostRenderer = ghost.AddComponent<MeshRenderer>();
                ConfigureGhostRenderer(ghostRenderer, ghostMaterials);
                createdRenderer = true;
            }

            foreach (SkinnedMeshRenderer sourceRenderer in player.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                Mesh baked = new Mesh { name = sourceRenderer.gameObject.name + " Afterimage Mesh" };
                sourceRenderer.BakeMesh(baked);
                bakedMeshes.Add(baked);

                GameObject ghost = new GameObject(sourceRenderer.gameObject.name + " Ghost");
                ghost.transform.SetParent(ghostRoot.transform, false);
                ghost.transform.position = sourceRenderer.transform.position;
                ghost.transform.rotation = sourceRenderer.transform.rotation;
                ghost.transform.localScale = sourceRenderer.transform.lossyScale;

                ghost.AddComponent<MeshFilter>().sharedMesh = baked;
                MeshRenderer ghostRenderer = ghost.AddComponent<MeshRenderer>();
                ConfigureGhostRenderer(ghostRenderer, ghostMaterials);
                createdRenderer = true;
            }

            if (!createdRenderer)
            {
                Destroy(ghostRoot);
                for (int index = 0; index < bakedMeshes.Count; index++)
                {
                    Destroy(bakedMeshes[index]);
                }
                return;
            }

            StartCoroutine(FadeAfterimage(ghostRoot, ghostMaterials, bakedMeshes));
        }

        private void ConfigureGhostRenderer(Renderer ghostRenderer, List<Material> materials)
        {
            Material material = new Material(afterimageShader)
            {
                color = new Color(0.12f, 0.95f, 1f, 0.36f)
            };
            ghostRenderer.material = material;
            ghostRenderer.shadowCastingMode = ShadowCastingMode.Off;
            ghostRenderer.receiveShadows = false;
            materials.Add(material);
        }

        private IEnumerator FadeAfterimage(
            GameObject ghostRoot,
            List<Material> materials,
            List<Mesh> bakedMeshes)
        {
            float elapsed = 0f;
            while (ghostRoot != null && elapsed < AfterimageLifetime)
            {
                elapsed += Time.unscaledDeltaTime;
                float remaining = 1f - Mathf.Clamp01(elapsed / AfterimageLifetime);
                for (int index = 0; index < materials.Count; index++)
                {
                    if (materials[index] == null)
                    {
                        continue;
                    }

                    Color color = materials[index].color;
                    color.a = 0.36f * remaining;
                    materials[index].color = color;
                }
                yield return null;
            }

            if (ghostRoot != null)
            {
                Destroy(ghostRoot);
            }
            for (int index = 0; index < materials.Count; index++)
            {
                if (materials[index] != null)
                {
                    Destroy(materials[index]);
                }
            }
            for (int index = 0; index < bakedMeshes.Count; index++)
            {
                if (bakedMeshes[index] != null)
                {
                    Destroy(bakedMeshes[index]);
                }
            }
        }

        private void UpdateMovePreview()
        {
            if (!plannedPointValid || player == null)
            {
                HidePlanningVisuals();
                return;
            }

            ShowMarker(plannedPoint, new Color(0.12f, 1f, 0.82f, 0.9f));
            ShowPath(
                player.position + Vector3.up * 0.08f,
                plannedPoint + Vector3.up * 0.08f,
                new Color(0.12f, 1f, 0.82f, 0.8f));
        }

        private void UpdateAimPreview()
        {
            if (!plannedPointValid || player == null)
            {
                return;
            }

            Vector3 direction = plannedPoint - player.position;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.001f)
            {
                return;
            }

            combat.SetAimDirection(direction.normalized);
            ShowMarker(plannedPoint, new Color(1f, 0.35f, 0.12f, 0.9f));
        }

        private bool TryScreenToGround(Vector2 screenPoint, out Vector3 worldPoint)
        {
            worldPoint = player != null ? player.position : Vector3.zero;
            if (worldCamera == null || player == null)
            {
                return false;
            }

            Ray ray = worldCamera.ScreenPointToRay(screenPoint);
            if (Physics.Raycast(
                    ray,
                    out RaycastHit hit,
                    GroundProbeDistance,
                    ~0,
                    QueryTriggerInteraction.Ignore))
            {
                worldPoint = hit.point + Vector3.up * 0.06f;
                return true;
            }

            Plane plane = new Plane(Vector3.up, player.position);
            if (plane.Raycast(ray, out float distance))
            {
                worldPoint = ray.GetPoint(distance);
                return true;
            }

            return false;
        }

        private void HandleKeyboardInput()
        {
#if UNITY_EDITOR || UNITY_STANDALONE || UNITY_WEBGL
            if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.M))
            {
                SetMode(ChronosphereActionMode.Move);
            }
            if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.F))
            {
                SetMode(ChronosphereActionMode.Aim);
            }
            if (Input.GetKeyDown(KeyCode.Q) && combat != null)
            {
                combat.SetWeapon(combat.Mode == WeaponMode.Rail
                    ? WeaponMode.Blade
                    : WeaponMode.Rail);
            }
#endif
        }

        private void SetMode(ChronosphereActionMode nextMode)
        {
            if (executingMove)
            {
                return;
            }

            CancelPointer();
            mode = nextMode;
        }

        private void FreezeWorld()
        {
            if (motor != null && !executingMove)
            {
                motor.MoveInput = Vector2.zero;
            }

            if (ShardstepClock.Instance != null)
            {
                ShardstepClock.Instance.MovementMagnitude = executingMove ? 1f : 0f;
                ShardstepClock.Instance.IsAiming =
                    mode == ChronosphereActionMode.Aim && activeFinger >= 0;
            }
        }

        private void ResetInputAndTime()
        {
            activeFinger = -1;
            plannedPointValid = false;
            executingMove = false;
            if (moveRoutine != null)
            {
                StopCoroutine(moveRoutine);
                moveRoutine = null;
            }
            if (motor != null)
            {
                motor.MoveInput = Vector2.zero;
            }
            if (combat != null && combat.IsAiming)
            {
                combat.CancelAim();
            }
            HidePlanningVisuals();
            FreezeWorld();
        }

        private void CreatePlanningVisuals()
        {
            marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            marker.name = "Chronosphere Action Marker";
            marker.transform.SetParent(transform, false);
            marker.transform.localScale = new Vector3(0.48f, 0.018f, 0.48f);
            Collider markerCollider = marker.GetComponent<Collider>();
            if (markerCollider != null)
            {
                Destroy(markerCollider);
            }
            markerRenderer = marker.GetComponent<Renderer>();
            Shader shader = Shader.Find("Sprites/Default");
            if (markerRenderer != null && shader != null)
            {
                markerMaterial = new Material(shader);
                markerRenderer.material = markerMaterial;
            }
            marker.SetActive(false);

            GameObject pathObject = new GameObject("Chronosphere Move Preview");
            pathObject.transform.SetParent(transform, false);
            pathPreview = pathObject.AddComponent<LineRenderer>();
            pathPreview.positionCount = 2;
            pathPreview.useWorldSpace = true;
            pathPreview.startWidth = 0.08f;
            pathPreview.endWidth = 0.08f;
            pathPreview.numCapVertices = 6;
            if (shader != null)
            {
                pathPreview.material = new Material(shader);
            }
            pathPreview.enabled = false;
        }

        private void ShowMarker(Vector3 position, Color color)
        {
            if (marker == null)
            {
                return;
            }
            marker.transform.position = position;
            marker.SetActive(true);
            if (markerMaterial != null)
            {
                markerMaterial.color = color;
            }
        }

        private void ShowPath(Vector3 from, Vector3 to, Color color)
        {
            if (pathPreview == null)
            {
                return;
            }
            pathPreview.enabled = true;
            pathPreview.SetPosition(0, from);
            pathPreview.SetPosition(1, to);
            if (pathPreview.material != null)
            {
                pathPreview.material.color = color;
            }
        }

        private void HidePlanningVisuals()
        {
            if (marker != null)
            {
                marker.SetActive(false);
            }
            if (pathPreview != null)
            {
                pathPreview.enabled = false;
            }
        }

        private static Rect MoveButtonScreen()
        {
            Rect safe = Screen.safeArea;
            float margin = Mathf.Clamp(safe.width * 0.035f, 12f, 26f);
            float height = Mathf.Clamp(safe.height * 0.07f, 58f, 82f);
            float width = Mathf.Clamp(safe.width * 0.27f, 116f, 190f);
            return new Rect(safe.xMin + margin, safe.yMin + margin, width, height);
        }

        private static Rect AimButtonScreen()
        {
            Rect move = MoveButtonScreen();
            return new Rect(move.xMax + 10f, move.y, move.width, move.height);
        }

        private static Rect WeaponButtonScreen()
        {
            Rect safe = Screen.safeArea;
            Rect aim = AimButtonScreen();
            float margin = Mathf.Clamp(safe.width * 0.035f, 12f, 26f);
            return new Rect(safe.xMax - aim.width - margin, aim.y, aim.width, aim.height);
        }

        private static bool IsHudPoint(Vector2 point)
        {
            return MoveButtonScreen().Contains(point) ||
                AimButtonScreen().Contains(point) ||
                WeaponButtonScreen().Contains(point);
        }

        private void OnGUI()
        {
            if (pixel == null || combat == null)
            {
                return;
            }

            DrawActionButton(
                MoveButtonScreen(),
                "MOVE",
                mode == ChronosphereActionMode.Move,
                () => SetMode(ChronosphereActionMode.Move));
            DrawActionButton(
                AimButtonScreen(),
                "AIM / FIRE",
                mode == ChronosphereActionMode.Aim,
                () => SetMode(ChronosphereActionMode.Aim));
            DrawActionButton(
                WeaponButtonScreen(),
                combat.Mode == WeaponMode.Rail ? "RAIL" : "BLADE",
                false,
                () => combat.SetWeapon(combat.Mode == WeaponMode.Rail
                    ? WeaponMode.Blade
                    : WeaponMode.Rail));

            Rect safe = Screen.safeArea;
            float top = Screen.height - safe.yMax;
            GUIStyle status = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                fontSize = Mathf.Clamp(Mathf.RoundToInt(Screen.width * 0.033f), 13, 21)
            };
            string timeState = IsExecutingAction ? "TIME MOVING" : "TIME FROZEN";
            string instruction = mode == ChronosphereActionMode.Move
                ? "MOVE: TOUCH A DIRECTION • ONE FIXED STEP"
                : "AIM: DRAG TOWARD TARGET • RELEASE TO FIRE";
            GUI.Box(
                new Rect(safe.xMin + 14f, top + 12f, safe.width - 28f, 72f),
                $"{timeState}    HP {playerHealth?.Current}/{playerHealth?.Maximum}    " +
                $"ENEMIES {director?.RemainingEnemies}\n{instruction}",
                status);
        }

        private void DrawActionButton(
            Rect screenRect,
            string label,
            bool selected,
            System.Action action)
        {
            Rect guiRect = new Rect(
                screenRect.x,
                Screen.height - screenRect.yMax,
                screenRect.width,
                screenRect.height);
            Color previous = GUI.color;
            GUI.color = selected
                ? new Color(0.12f, 0.9f, 0.84f, 0.95f)
                : new Color(0.08f, 0.1f, 0.14f, 0.9f);
            GUI.DrawTexture(guiRect, pixel);
            GUI.color = previous;

            GUIStyle style = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                fontSize = Mathf.Clamp(Mathf.RoundToInt(Screen.width * 0.036f), 15, 24)
            };
            if (GUI.Button(guiRect, label, style))
            {
                action?.Invoke();
            }
        }

        private void OnDestroy()
        {
            if (markerMaterial != null)
            {
                Destroy(markerMaterial);
            }
            if (pathPreview != null && pathPreview.material != null)
            {
                Destroy(pathPreview.material);
            }
            if (pixel != null)
            {
                Destroy(pixel);
            }
        }
    }
}
