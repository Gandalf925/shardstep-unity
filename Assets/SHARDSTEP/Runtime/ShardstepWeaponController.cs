using UnityEngine;
using UnityEngine.SceneManagement;

namespace Shardstep
{
    [DefaultExecutionOrder(-320)]
    public sealed class ShardstepWeaponController : MonoBehaviour
    {
        private Transform player;
        private PlayerCombat combat;
        private ArenaDirector director;
        private int activeFinger = -1;
        private WeaponMode pendingMode;
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
            activeFinger = -1;
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            player = null;
            combat = null;
            director = null;
            activeFinger = -1;
        }

        private void Update()
        {
            BindReferences();
            if (combat == null || (director != null && (director.Victory || director.Defeat)))
            {
                activeFinger = -1;
                return;
            }

            HandleTouchInput();
#if UNITY_EDITOR || UNITY_STANDALONE
            if (Input.GetKeyDown(KeyCode.Alpha1))
            {
                Select(WeaponMode.Rail);
            }
            else if (Input.GetKeyDown(KeyCode.Alpha2))
            {
                Select(WeaponMode.Blade);
            }
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
                }
            }

            if (director == null)
            {
                director = Object.FindObjectOfType<ArenaDirector>();
            }
        }

        private void HandleTouchInput()
        {
            Rect rail = RailRectScreen();
            Rect blade = BladeRectScreen();

            for (int i = 0; i < Input.touchCount; i++)
            {
                Touch touch = Input.GetTouch(i);
                if (activeFinger < 0)
                {
                    if (touch.phase != TouchPhase.Began)
                    {
                        continue;
                    }

                    if (rail.Contains(touch.position))
                    {
                        activeFinger = touch.fingerId;
                        pendingMode = WeaponMode.Rail;
                    }
                    else if (blade.Contains(touch.position))
                    {
                        activeFinger = touch.fingerId;
                        pendingMode = WeaponMode.Blade;
                    }

                    continue;
                }

                if (touch.fingerId != activeFinger)
                {
                    continue;
                }

                if (touch.phase == TouchPhase.Ended)
                {
                    bool inside = pendingMode == WeaponMode.Rail
                        ? rail.Contains(touch.position)
                        : blade.Contains(touch.position);
                    activeFinger = -1;
                    if (inside)
                    {
                        Select(pendingMode);
                    }
                }
                else if (touch.phase == TouchPhase.Canceled)
                {
                    activeFinger = -1;
                }
            }
        }

        private void Select(WeaponMode mode)
        {
            if (combat == null || combat.Mode == mode)
            {
                return;
            }

            if (combat.IsAiming)
            {
                combat.CancelAim();
            }

            combat.SetWeapon(mode);
        }

        public static Rect RailRectScreen()
        {
            Rect safe = Screen.safeArea;
            float margin = Mathf.Clamp(safe.width * 0.035f, 12f, 28f);
            float width = Mathf.Clamp(safe.width * 0.2f, 82f, 126f);
            float height = Mathf.Clamp(safe.height * 0.075f, 54f, 76f);
            return new Rect(safe.xMin + margin, safe.yMin + margin + height + 10f, width, height);
        }

        public static Rect BladeRectScreen()
        {
            Rect rail = RailRectScreen();
            return new Rect(rail.x, rail.y - rail.height - 10f, rail.width, rail.height);
        }

        private void OnGUI()
        {
            if (combat == null || pixel == null)
            {
                return;
            }

            DrawWeaponButton(RailRectScreen(), WeaponMode.Rail, "RAIL", "LONG / PRECISE");
            DrawWeaponButton(BladeRectScreen(), WeaponMode.Blade, "BLADE", "CLOSE / HEAVY");
        }

        private void DrawWeaponButton(Rect screenRect, WeaponMode mode, string title, string subtitle)
        {
            Rect rect = new Rect(screenRect.x, Screen.height - screenRect.yMax, screenRect.width, screenRect.height);
            bool selected = combat.Mode == mode;
            Color previous = GUI.color;
            GUI.color = selected
                ? (mode == WeaponMode.Rail ? new Color(0.08f, 0.72f, 0.84f, 0.92f) : new Color(0.62f, 0.16f, 0.82f, 0.92f))
                : new Color(0.08f, 0.08f, 0.11f, 0.72f);
            GUI.DrawTexture(rect, pixel);
            GUI.color = previous;

            GUIStyle titleStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.UpperCenter,
                fontStyle = FontStyle.Bold,
                fontSize = Mathf.Clamp(Mathf.RoundToInt(Screen.width * 0.035f), 14, 22)
            };
            GUIStyle subtitleStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.LowerCenter,
                fontSize = Mathf.Clamp(Mathf.RoundToInt(Screen.width * 0.022f), 10, 15)
            };
            GUI.Label(new Rect(rect.x, rect.y + 5f, rect.width, rect.height * 0.55f), title, titleStyle);
            GUI.Label(new Rect(rect.x, rect.y + rect.height * 0.43f, rect.width, rect.height * 0.45f), subtitle, subtitleStyle);
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
