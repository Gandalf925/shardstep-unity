using System.Collections.Generic;
using UnityEngine;

namespace Shardstep
{
    public static class ShardstepPortraitPlaytestBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (!Application.isMobilePlatform && !Input.touchSupported)
            {
                return;
            }

            if (Object.FindObjectOfType<ShardstepPortraitPlaytestPolish>() != null)
            {
                return;
            }

            GameObject host = new GameObject("SHARDSTEP Portrait Playtest Polish");
            Object.DontDestroyOnLoad(host);
            host.AddComponent<ShardstepPortraitPlaytestPolish>();
        }
    }

    public sealed class ShardstepPortraitPlaytestPolish : MonoBehaviour
    {
        private const float ProbeInterval = 0.35f;
        private const float CameraHeight = 13.5f;
        private const float CameraBackOffset = 7.2f;
        private const float FollowSharpness = 7.5f;

        private Transform player;
        private Camera worldCamera;
        private float nextProbe;
        private bool boundariesBuilt;
        private readonly List<GameObject> boundaryVisuals = new List<GameObject>();

        private void Update()
        {
            if (Time.unscaledTime < nextProbe)
            {
                return;
            }

            nextProbe = Time.unscaledTime + ProbeInterval;
            BindReferences();
            ConfigureCamera();
            if (!boundariesBuilt)
            {
                BuildBoundaryVisuals();
            }
        }

        private void LateUpdate()
        {
            if (player == null || worldCamera == null)
            {
                return;
            }

            Vector3 desired = player.position + new Vector3(0f, CameraHeight, -CameraBackOffset);
            float blend = 1f - Mathf.Exp(-FollowSharpness * Time.unscaledDeltaTime);
            worldCamera.transform.position = Vector3.Lerp(worldCamera.transform.position, desired, blend);

            Vector3 lookPoint = player.position + Vector3.forward * 1.8f;
            Quaternion desiredRotation = Quaternion.LookRotation(lookPoint - worldCamera.transform.position, Vector3.up);
            worldCamera.transform.rotation = Quaternion.Slerp(worldCamera.transform.rotation, desiredRotation, blend);
        }

        private void BindReferences()
        {
            if (player == null)
            {
                GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
                if (playerObject != null)
                {
                    player = playerObject.transform;
                    ImprovePlayerReadability(playerObject);
                }
            }

            if (worldCamera == null)
            {
                worldCamera = Camera.main;
            }
        }

        private void ConfigureCamera()
        {
            if (worldCamera == null)
            {
                return;
            }

            worldCamera.nearClipPlane = 0.1f;
            worldCamera.farClipPlane = Mathf.Max(worldCamera.farClipPlane, 120f);
            worldCamera.fieldOfView = 48f;
            worldCamera.depthTextureMode |= DepthTextureMode.Depth;
        }

        private static void ImprovePlayerReadability(GameObject playerObject)
        {
            Renderer[] renderers = playerObject.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer renderer in renderers)
            {
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                renderer.receiveShadows = true;
            }

            Light rim = playerObject.GetComponentInChildren<Light>();
            if (rim == null)
            {
                GameObject lightObject = new GameObject("Mobile Player Rim Light");
                lightObject.transform.SetParent(playerObject.transform, false);
                lightObject.transform.localPosition = new Vector3(0f, 2.2f, -0.8f);
                rim = lightObject.AddComponent<Light>();
                rim.type = LightType.Point;
                rim.range = 6f;
                rim.intensity = 1.8f;
                rim.color = new Color(0.25f, 0.9f, 1f);
                rim.shadows = LightShadows.None;
            }
        }

        private void BuildBoundaryVisuals()
        {
            BoxCollider[] colliders = Object.FindObjectsOfType<BoxCollider>();
            int created = 0;
            foreach (BoxCollider box in colliders)
            {
                if (!box.enabled || box.isTrigger || box.transform.CompareTag("Player"))
                {
                    continue;
                }

                Vector3 size = Vector3.Scale(box.size, box.transform.lossyScale);
                bool wallLike = size.y >= 1.2f && (size.x >= 4f || size.z >= 4f) && Mathf.Min(size.x, size.z) <= 2.5f;
                if (!wallLike || HasVisibleRenderer(box.gameObject))
                {
                    continue;
                }

                CreateBoundaryStrip(box.bounds);
                created++;
                if (created >= 24)
                {
                    break;
                }
            }

            boundariesBuilt = true;
        }

        private static bool HasVisibleRenderer(GameObject target)
        {
            Renderer renderer = target.GetComponent<Renderer>();
            return renderer != null && renderer.enabled;
        }

        private void CreateBoundaryStrip(Bounds bounds)
        {
            GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.name = "Visible Arena Boundary";
            visual.transform.position = new Vector3(bounds.center.x, Mathf.Max(0.08f, bounds.min.y + 0.08f), bounds.center.z);
            visual.transform.localScale = new Vector3(
                Mathf.Max(0.12f, bounds.size.x),
                0.14f,
                Mathf.Max(0.12f, bounds.size.z));

            Collider collider = visual.GetComponent<Collider>();
            if (collider != null)
            {
                Destroy(collider);
            }

            Renderer renderer = visual.GetComponent<Renderer>();
            Shader shader = Shader.Find("Sprites/Default");
            if (renderer != null && shader != null)
            {
                renderer.material = new Material(shader);
                renderer.material.color = new Color(0.15f, 0.85f, 1f, 0.42f);
            }

            boundaryVisuals.Add(visual);
        }

        private void OnDestroy()
        {
            foreach (GameObject visual in boundaryVisuals)
            {
                if (visual != null)
                {
                    Destroy(visual);
                }
            }
            boundaryVisuals.Clear();
        }
    }
}
