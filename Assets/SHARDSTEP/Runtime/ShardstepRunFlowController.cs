using UnityEngine;
using UnityEngine.SceneManagement;

namespace Shardstep
{
    [DefaultExecutionOrder(500)]
    public sealed class ShardstepRunFlowController : MonoBehaviour
    {
        private const float IntroFullDuration = 2.2f;
        private const float LastEnemyDuration = 1.1f;

        private ArenaDirector director;
        private int previousEnemies = -1;
        private float introUntil;
        private float lastEnemyUntil;
        private bool reloading;
        private int activeFinger = -1;
        private Texture2D pixel;

        private void Awake()
        {
            pixel = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            pixel.SetPixel(0, 0, Color.white);
            pixel.Apply();
            ResetRunState();
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
            director = null;
            ResetRunState();
        }

        private void ResetRunState()
        {
            previousEnemies = -1;
            introUntil = Time.unscaledTime + IntroFullDuration;
            lastEnemyUntil = 0f;
            reloading = false;
            activeFinger = -1;
        }

        private void Update()
        {
            if (director == null)
            {
                director = Object.FindObjectOfType<ArenaDirector>();
            }

            if (director == null)
            {
                return;
            }

            if (previousEnemies > 1 && director.RemainingEnemies == 1)
            {
                lastEnemyUntil = Time.unscaledTime + LastEnemyDuration;
            }
            previousEnemies = director.RemainingEnemies;

            if (director.Victory || director.Defeat)
            {
                HandleRetryInput();
#if UNITY_EDITOR || UNITY_STANDALONE
                if (Input.GetKeyDown(KeyCode.R))
                {
                    ReloadCurrentScene();
                }
#endif
            }
        }

        private void HandleRetryInput()
        {
            Rect retry = RetryRectScreen();
            for (int i = 0; i < Input.touchCount; i++)
            {
                Touch touch = Input.GetTouch(i);
                if (activeFinger < 0)
                {
                    if (touch.phase == TouchPhase.Began && retry.Contains(touch.position))
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
                    bool inside = retry.Contains(touch.position);
                    activeFinger = -1;
                    if (inside)
                    {
                        ReloadCurrentScene();
                    }
                }
                else if (touch.phase == TouchPhase.Canceled)
                {
                    activeFinger = -1;
                }
            }

#if UNITY_EDITOR || UNITY_STANDALONE
            if (Input.GetMouseButtonUp(0) && retry.Contains(Input.mousePosition))
            {
                ReloadCurrentScene();
            }
#endif
        }

        private void ReloadCurrentScene()
        {
            if (reloading)
            {
                return;
            }

            reloading = true;
            if (ShardstepClock.Instance != null)
            {
                ShardstepClock.Instance.ForcedFreeze = false;
                ShardstepClock.Instance.IsAiming = false;
                ShardstepClock.Instance.MovementMagnitude = 0f;
                ShardstepClock.Instance.CancelCurrentAction();
            }

            Scene active = SceneManager.GetActiveScene();
            SceneManager.LoadScene(active.buildIndex);
        }

        private static Rect RetryRectScreen()
        {
            Rect safe = Screen.safeArea;
            float width = Mathf.Clamp(safe.width * 0.58f, 210f, 360f);
            float height = Mathf.Clamp(safe.height * 0.075f, 58f, 84f);
            float margin = Mathf.Clamp(safe.height * 0.055f, 24f, 56f);
            return new Rect(safe.center.x - width * 0.5f, safe.yMin + margin, width, height);
        }

        private void OnGUI()
        {
            if (director == null || pixel == null)
            {
                return;
            }

            Rect safe = Screen.safeArea;
            float top = Screen.height - safe.yMax;
            float margin = Mathf.Clamp(safe.width * 0.04f, 14f, 30f);

            if (!director.Victory && !director.Defeat && Time.unscaledTime < introUntil)
            {
                GUIStyle style = PanelStyle();
                GUI.Box(
                    new Rect(safe.xMin + margin, top + safe.height * 0.22f, safe.width - margin * 2f, 118f),
                    "ELIMINATE ALL HOSTILES\nTAP A DIRECTION • READ BULLETS • COMMIT ONE ACTION",
                    style);
            }

            if (!director.Victory && !director.Defeat && Time.unscaledTime < lastEnemyUntil)
            {
                GUIStyle style = PanelStyle();
                GUI.Box(
                    new Rect(safe.center.x - 130f, top + safe.height * 0.34f, 260f, 58f),
                    "LAST HOSTILE",
                    style);
            }

            if (!director.Victory && !director.Defeat)
            {
                return;
            }

            Color previous = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.58f);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), pixel);
            GUI.color = previous;

            int actions = ShardstepClock.Instance != null
                ? ShardstepClock.Instance.CompletedActions
                : 0;
            string title = director.Victory ? "AREA SECURED" : "TIMELINE COLLAPSED";
            string subtitle = director.Victory
                ? $"ACTIONS  {actions}"
                : "READ THE TIMELINE AND RETRY";
            GUIStyle resultStyle = PanelStyle();
            GUI.Box(
                new Rect(safe.xMin + margin, top + safe.height * 0.28f, safe.width - margin * 2f, 128f),
                title + "\n" + subtitle,
                resultStyle);

            Rect screenRect = RetryRectScreen();
            Rect guiRect = new Rect(
                screenRect.x,
                Screen.height - screenRect.yMax,
                screenRect.width,
                screenRect.height);
            GUI.color = new Color(0.08f, 0.78f, 0.86f, 0.94f);
            GUI.DrawTexture(guiRect, pixel);
            GUI.color = previous;
            GUIStyle retryStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                fontSize = Mathf.Clamp(Mathf.RoundToInt(Screen.width * 0.05f), 20, 32)
            };
            GUI.Label(guiRect, reloading ? "RELOADING" : "RETRY", retryStyle);
        }

        private static GUIStyle PanelStyle()
        {
            return new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                fontSize = Mathf.Clamp(Mathf.RoundToInt(Screen.width * 0.043f), 16, 28),
                wordWrap = true
            };
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
