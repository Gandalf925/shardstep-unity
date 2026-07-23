using UnityEngine;

namespace Shardstep
{
    public sealed class ExtractionZone : MonoBehaviour
    {
        public ArenaDirector director;

        private void OnTriggerEnter(Collider other)
        {
            if (other.CompareTag("Player"))
            {
                director?.CompleteRun();
            }
        }
    }

    public sealed class ShardstepInput : MonoBehaviour
    {
    }

    public sealed class CameraRig : MonoBehaviour
    {
        public Transform target;
        private readonly Vector3 offset = new Vector3(0f, 15.5f, -12.5f);

        private void LateUpdate()
        {
            if (target == null)
            {
                return;
            }

            Vector3 desired = target.position + offset;
            transform.position = Vector3.Lerp(
                transform.position,
                desired,
                7f * Time.unscaledDeltaTime);
        }
    }

    public static class ShardstepBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreatePrototype()
        {
            if (Object.FindObjectOfType<ShardstepClock>() != null)
            {
                return;
            }

            GameObject root = new GameObject("SHARDSTEP Runtime");
            root.AddComponent<ShardstepClock>();

            GameObject player = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            player.name = "Frame Runner";
            player.tag = "Player";
            player.transform.position = new Vector3(0f, 0.9f, -8.2f);
            Collider primitiveCollider = player.GetComponent<Collider>();
            if (primitiveCollider != null)
            {
                Object.Destroy(primitiveCollider);
            }

            CharacterController controller = player.AddComponent<CharacterController>();
            controller.height = 1.8f;
            controller.radius = 0.42f;
            controller.center = Vector3.zero;
            Renderer playerRenderer = player.GetComponent<Renderer>();
            if (playerRenderer != null)
            {
                playerRenderer.material = ShardstepVisuals.CreateMaterial(
                    new Color(0.12f, 0.95f, 1f),
                    true);
            }

            player.AddComponent<PlayerMotor>();
            player.AddComponent<PlayerCombat>();
            Health playerHealth = player.AddComponent<Health>();
            playerHealth.Configure(5);

            GameObject cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.025f, 0.035f, 0.05f);
            camera.fieldOfView = 52f;
            cameraObject.transform.position =
                player.transform.position + new Vector3(0f, 15.5f, -12.5f);
            cameraObject.transform.rotation = Quaternion.Euler(51f, 0f, 0f);
            CameraRig rig = cameraObject.AddComponent<CameraRig>();
            rig.target = player.transform;

            GameObject lightObject = new GameObject("Directional Light");
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.25f;
            light.color = new Color(0.72f, 0.82f, 1f);
            lightObject.transform.rotation = Quaternion.Euler(48f, -32f, 0f);

            GameObject directorObject = new GameObject("Arena Director");
            directorObject.AddComponent<ArenaDirector>();
        }
    }
}
