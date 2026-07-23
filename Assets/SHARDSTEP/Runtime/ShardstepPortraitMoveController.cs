using UnityEngine;
using UnityEngine.SceneManagement;

namespace Shardstep
{
    [DefaultExecutionOrder(-1000)]
    public sealed class ShardstepPortraitMoveController : MonoBehaviour
    {
        private const float MaxMoveDistance = 6.5f;
        private const float MoveSpeed = 18f;
        private const float PlayerRadius = 0.32f;
        private const float FloorProbeHeight = 8f;
        private const float FloorProbeDistance = 20f;
        private const float CancelDragRatio = 0.16f;
        private const float DestinationTolerance = 0.06f;

        private Transform player;
        private PlayerMotor motor;
        private ArenaDirector director;
        private Camera worldCamera;
        private GameObject marker;
        private Renderer markerRenderer;
        private Material markerMaterial;

        private int activeFinger = -1;
        private Vector2 pressScreen;
        private Vector3 destination;
        private bool destinationValid;
        private bool cancelled;
        private bool moving;

        private static readonly Color ValidColor = new Color(0.1f, 1f, 0.9f, 0.82f);
        private static readonly Color InvalidColor = new Color(1f, 0.18f, 0.12f, 0.82f);
        private static readonly Color CancelColor = new Color(1f, 0.55f, 0.08f, 0.82f);

        private void Awake()
        {
            CreateMarker();
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            CancelInputAndMovement();
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            player = null;
            motor = null;
            director = null;
            worldCamera = null;
            CancelInputAndMovement();
        }

        private void Update()
        {
            BindReferences();
            if (player == null || worldCamera == null)
            {
                HideMarker();
                return;
            }

            if (director != null && (director.Victory || director.Defeat))
            {
                CancelInputAndMovement();
                return;
            }

            ProcessPointer();
            AdvanceMovement();
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

        private void ProcessPointer()
        {
            if (Input.touchCount > 0)
            {
                ProcessTouches();
                return;
            }

#if UNITY_EDITOR || UNITY_STANDALONE
            ProcessMouse();
#endif
        }

        private void ProcessTouches()
        {
            if (activeFinger < 0)
            {
                for (int i = 0; i < Input.touchCount; i++)
                {
                    Touch touch = Input.GetTouch(i);
                    if (touch.phase == TouchPhase.Began && !IsReservedUi(touch.position))
                    {
                        BeginPointer(touch.fingerId, touch.position);
                        break;
                    }
                }
            }

            if (activeFinger < 0)
            {
                return;
            }

            for (int i = 0; i < Input.touchCount; i++)
            {
                Touch touch = Input.GetTouch(i);
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

                break;
            }
        }

#if UNITY_EDITOR || UNITY_STANDALONE
        private void ProcessMouse()
        {
            Vector2 screen = Input.mousePosition;
            if (Input.GetMouseButtonDown(0) && !IsReservedUi(screen))
            {
                BeginPointer(0, screen);
            }
            else if (Input.GetMouseButton(0) && activeFinger == 0)
            {
                UpdatePointer(screen);
            }
            else if (Input.GetMouseButtonUp(0) && activeFinger == 0)
            {
                EndPointer(screen);
            }
        }
#endif

        private void BeginPointer(int fingerId, Vector2 screen)
        {
            activeFinger = fingerId;
            pressScreen = screen;
            cancelled = false;
            moving = false;
            UpdateDestination(screen);
        }

        private void UpdatePointer(Vector2 screen)
        {
            float cancelDistance = Mathf.Max(48f, Screen.width * CancelDragRatio);
            cancelled = Vector2.Distance(screen, pressScreen) >= cancelDistance;
            if (cancelled)
            {
                destinationValid = false;
                SetMarkerColor(CancelColor);
                return;
            }

            UpdateDestination(screen);
        }

        private void EndPointer(Vector2 screen)
        {
            if (!cancelled)
            {
                UpdateDestination(screen);
                moving = destinationValid;
            }

            activeFinger = -1;
            cancelled = false;
            if (!moving)
            {
                HideMarker();
            }
        }

        private void CancelPointer()
        {
            activeFinger = -1;
            cancelled = false;
            destinationValid = false;
            moving = false;
            HideMarker();
        }

        private void UpdateDestination(Vector2 screen)
        {
            destinationValid = TryResolveDestination(screen, out destination);
            ShowMarker(destination, destinationValid ? ValidColor : InvalidColor);
        }

        private bool TryResolveDestination(Vector2 screen, out Vector3 resolved)
        {
            resolved = player.position;
            Ray ray = worldCamera.ScreenPointToRay(screen);
            if (!Physics.Raycast(ray, out RaycastHit floorHit, FloorProbeDistance, ~0, QueryTriggerInteraction.Ignore))
            {
                resolved = ray.GetPoint(Mathf.Min(8f, FloorProbeDistance));
                return false;
            }

            Vector3 candidate = floorHit.point;
            Vector3 planar = candidate - player.position;
            planar.y = 0f;
            if (planar.magnitude > MaxMoveDistance)
            {
                candidate = player.position + planar.normalized * MaxMoveDistance;
            }

            Vector3 probeOrigin = candidate + Vector3.up * FloorProbeHeight;
            if (!Physics.Raycast(probeOrigin, Vector3.down, out RaycastHit groundHit, FloorProbeDistance, ~0, QueryTriggerInteraction.Ignore))
            {
                resolved = candidate;
                return false;
            }

            resolved = groundHit.point + Vector3.up * 0.08f;
            Vector3 travel = resolved - player.position;
            travel.y = 0f;
            float distance = travel.magnitude;
            if (distance < DestinationTolerance)
            {
                return false;
            }

            Vector3 castOrigin = player.position + Vector3.up * PlayerRadius;
            if (Physics.SphereCast(castOrigin, PlayerRadius, travel.normalized, out RaycastHit obstacle, distance, ~0, QueryTriggerInteraction.Ignore))
            {
                if (obstacle.collider != groundHit.collider && obstacle.distance < distance - PlayerRadius)
                {
                    resolved = obstacle.point - travel.normalized * (PlayerRadius + 0.06f);
                    resolved.y = groundHit.point.y + 0.08f;
                    return Vector3.Distance(player.position, resolved) > PlayerRadius * 1.5f;
                }
            }

            return true;
        }

        private void AdvanceMovement()
        {
            if (!moving || player == null)
            {
                return;
            }

            Vector3 current = player.position;
            Vector3 delta = destination - current;
            delta.y = 0f;
            if (delta.magnitude <= DestinationTolerance)
            {
                FinishMovement();
                return;
            }

            float step = MoveSpeed * Time.deltaTime;
            Vector3 next = Vector3.MoveTowards(current, destination, step);
            Vector3 travel = next - current;
            travel.y = 0f;

            if (travel.sqrMagnitude > 0.0001f)
            {
                Vector3 origin = current + Vector3.up * PlayerRadius;
                if (Physics.SphereCast(origin, PlayerRadius, travel.normalized, out RaycastHit hit, travel.magnitude + 0.04f, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (hit.transform != player && !hit.transform.IsChildOf(player))
                    {
                        FinishMovement();
                        return;
                    }
                }

                player.rotation = Quaternion.RotateTowards(
                    player.rotation,
                    Quaternion.LookRotation(travel.normalized, Vector3.up),
                    900f * Time.deltaTime);
            }

            player.position = next;
            if (motor != null)
            {
                motor.MoveInput = Vector2.zero;
            }

            if (ShardstepClock.Instance != null)
            {
                ShardstepClock.Instance.MovementMagnitude = 1f;
            }

            ShowMarker(destination, ValidColor);
        }

        private void FinishMovement()
        {
            moving = false;
            destinationValid = false;
            if (motor != null)
            {
                motor.MoveInput = Vector2.zero;
            }

            if (ShardstepClock.Instance != null)
            {
                ShardstepClock.Instance.MovementMagnitude = 0f;
            }

            HideMarker();
        }

        private bool IsReservedUi(Vector2 screen)
        {
            Rect safe = Screen.safeArea;
            float attackSize = Mathf.Clamp(safe.width * 0.22f, 92f, 142f);
            float margin = Mathf.Clamp(safe.width * 0.035f, 12f, 28f);
            Rect attack = new Rect(safe.xMax - attackSize - margin, safe.yMin + margin, attackSize, attackSize);
            Rect weapon = new Rect(safe.xMin, safe.yMin, safe.width * 0.38f, Mathf.Max(110f, safe.height * 0.18f));
            return attack.Contains(screen) || weapon.Contains(screen);
        }

        private void CreateMarker()
        {
            marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            marker.name = "Move Destination Preview";
            marker.transform.SetParent(transform, false);
            marker.transform.localScale = new Vector3(0.55f, 0.015f, 0.55f);
            Collider collider = marker.GetComponent<Collider>();
            if (collider != null)
            {
                Destroy(collider);
            }

            markerRenderer = marker.GetComponent<Renderer>();
            Shader shader = Shader.Find("Sprites/Default");
            if (markerRenderer != null && shader != null)
            {
                markerMaterial = new Material(shader);
                markerRenderer.material = markerMaterial;
            }

            HideMarker();
        }

        private void ShowMarker(Vector3 position, Color color)
        {
            if (marker == null)
            {
                return;
            }

            marker.transform.position = position + Vector3.up * 0.025f;
            marker.SetActive(true);
            SetMarkerColor(color);
        }

        private void SetMarkerColor(Color color)
        {
            if (markerMaterial != null)
            {
                markerMaterial.color = color;
            }
        }

        private void HideMarker()
        {
            if (marker != null)
            {
                marker.SetActive(false);
            }
        }

        private void CancelInputAndMovement()
        {
            activeFinger = -1;
            cancelled = false;
            destinationValid = false;
            moving = false;
            if (motor != null)
            {
                motor.MoveInput = Vector2.zero;
            }

            HideMarker();
        }

        private void OnDestroy()
        {
            if (markerMaterial != null)
            {
                Destroy(markerMaterial);
            }
        }
    }
}
