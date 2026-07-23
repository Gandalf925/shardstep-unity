using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Shardstep
{
    public sealed class ShardstepCombatJuice : MonoBehaviour
    {
        private sealed class TrackedHealth
        {
            public Health health;
            public int previous;
        }

        private sealed class FloatingText
        {
            public Vector3 world;
            public string text;
            public float until;
            public Color color;
        }

        private const float ProbeInterval = 0.08f;
        private const float FloatingDuration = 0.62f;
        private readonly List<TrackedHealth> tracked = new List<TrackedHealth>();
        private readonly List<FloatingText> floating = new List<FloatingText>();
        private Transform player;
        private Health playerHealth;
        private Camera worldCamera;
        private float nextProbe;
        private float comboUntil;
        private int combo;
        private Texture2D pixel;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (Object.FindObjectOfType<ShardstepCombatJuice>() != null)
            {
                return;
            }

            GameObject host = new GameObject("SHARDSTEP Combat Juice");
            Object.DontDestroyOnLoad(host);
            host.AddComponent<ShardstepCombatJuice>();
        }

        private void Awake()
        {
            pixel = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            pixel.SetPixel(0, 0, Color.white);
            pixel.Apply();
        }

        private void Update()
        {
            BindReferences();
            if (Time.unscaledTime >= nextProbe)
            {
                nextProbe = Time.unscaledTime + ProbeInterval;
                RefreshAndDetect();
            }

            if (combo > 0 && Time.unscaledTime >= comboUntil)
            {
                combo = 0;
            }

            for (int i = floating.Count - 1; i >= 0; i--)
            {
                if (Time.unscaledTime >= floating[i].until)
                {
                    floating.RemoveAt(i);
                }
            }
        }

        private void BindReferences()
        {
            if (player == null)
            {
                GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
                if (playerObject != null)
                {
                    player = playerObject.transform;
                    playerHealth = playerObject.GetComponent<Health>();
                }
            }

            if (worldCamera == null)
            {
                worldCamera = Camera.main;
            }
        }

        private void RefreshAndDetect()
        {
            Health[] all = Object.FindObjectsOfType<Health>();
            foreach (Health health in all)
            {
                if (health == null || health == playerHealth)
                {
                    continue;
                }

                TrackedHealth item = tracked.Find(entry => entry.health == health);
                if (item == null)
                {
                    tracked.Add(new TrackedHealth { health = health, previous = health.Current });
                    continue;
                }

                if (health.Current < item.previous)
                {
                    int damage = item.previous - health.Current;
                    bool killed = health.Current <= 0;
                    floating.Add(new FloatingText
                    {
                        world = health.transform.position + Vector3.up * 1.35f,
                        text = killed ? "BREAK" : $"-{damage}",
                        until = Time.unscaledTime + FloatingDuration,
                        color = killed ? new Color(0.2f, 1f, 0.85f, 1f) : Color.white
                    });
                    SpawnBurst(health.transform.position + Vector3.up * 0.65f, killed);

                    if (killed)
                    {
                        combo = Time.unscaledTime < comboUntil ? combo + 1 : 1;
                        comboUntil = Time.unscaledTime + 1.8f;
                    }
                }

                item.previous = health.Current;
            }

            tracked.RemoveAll(entry => entry.health == null);
        }

        private void SpawnBurst(Vector3 position, bool killed)
        {
            int count = killed ? 12 : 6;
            for (int i = 0; i < count; i++)
            {
                GameObject shard = GameObject.CreatePrimitive(PrimitiveType.Cube);
                shard.name = "Hit Shard";
                shard.transform.position = position;
                shard.transform.localScale = Vector3.one * Random.Range(0.05f, 0.12f);
                Collider collider = shard.GetComponent<Collider>();
                if (collider != null)
                {
                    Destroy(collider);
                }

                Renderer renderer = shard.GetComponent<Renderer>();
                if (renderer != null)
                {
                    Shader shader = Shader.Find("Sprites/Default");
                    if (shader != null)
                    {
                        renderer.material = new Material(shader);
                        renderer.material.color = killed
                            ? new Color(0.15f, 1f, 0.84f, 0.9f)
                            : new Color(1f, 0.75f, 0.25f, 0.9f);
                    }
                }

                Rigidbody body = shard.AddComponent<Rigidbody>();
                body.useGravity = false;
                body.velocity = Random.onUnitSphere * Random.Range(2.5f, 5.5f);
                body.angularVelocity = Random.onUnitSphere * 8f;
                StartCoroutine(FadeShard(shard, renderer, killed ? 0.58f : 0.32f));
            }
        }

        private IEnumerator FadeShard(GameObject shard, Renderer renderer, float duration)
        {
            float elapsed = 0f;
            Color start = renderer != null ? renderer.material.color : Color.white;
            while (shard != null && elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                if (renderer != null)
                {
                    Color color = start;
                    color.a = 1f - Mathf.Clamp01(elapsed / duration);
                    renderer.material.color = color;
                }
                shard.transform.localScale *= 0.94f;
                yield return null;
            }

            if (shard != null)
            {
                Destroy(shard);
            }
        }

        private void OnGUI()
        {
            if (worldCamera == null)
            {
                return;
            }

            foreach (FloatingText item in floating)
            {
                Vector3 screen = worldCamera.WorldToScreenPoint(item.world);
                if (screen.z <= 0f)
                {
                    continue;
                }

                float remaining = Mathf.Clamp01((item.until - Time.unscaledTime) / FloatingDuration);
                GUIStyle style = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontStyle = FontStyle.Bold,
                    fontSize = Mathf.Clamp(Mathf.RoundToInt(Screen.width * 0.048f), 18, 30)
                };
                Color previous = GUI.color;
                GUI.color = new Color(item.color.r, item.color.g, item.color.b, remaining);
                float lift = (1f - remaining) * 44f;
                GUI.Label(new Rect(screen.x - 60f, Screen.height - screen.y - 25f - lift, 120f, 40f), item.text, style);
                GUI.color = previous;
            }

            if (combo >= 2 && Time.unscaledTime < comboUntil)
            {
                Rect safe = Screen.safeArea;
                GUIStyle style = new GUIStyle(GUI.skin.box)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontStyle = FontStyle.Bold,
                    fontSize = Mathf.Clamp(Mathf.RoundToInt(Screen.width * 0.052f), 20, 34)
                };
                GUI.Box(new Rect(safe.center.x - 90f, Screen.height - safe.yMax + 204f, 180f, 52f), $"CHAIN ×{combo}", style);
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
