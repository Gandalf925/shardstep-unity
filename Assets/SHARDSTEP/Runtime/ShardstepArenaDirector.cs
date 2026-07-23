using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Shardstep
{
    public sealed class ArenaDirector : MonoBehaviour
    {
        private const int ReinforcementActionThreshold = 12;

        public int RemainingEnemies { get; private set; }
        public bool ReinforcementsDeployed { get; private set; }
        public bool Victory { get; private set; }
        public bool Defeat { get; private set; }

        private int kills;
        private GameObject exitObject;
        private SimpleApocalypseCatalog catalog;

        private readonly Vector3[] initialPositions =
        {
            new Vector3(-5.8f, 0.9f, -1.5f),
            new Vector3(5.8f, 0.9f, -0.2f),
            new Vector3(-3.2f, 0.9f, 3.8f),
            new Vector3(3.6f, 0.9f, 4.5f),
            new Vector3(0f, 0.9f, 1.4f),
            new Vector3(0.5f, 0.9f, 7.4f)
        };

        private readonly Vector3[] reinforcementPositions =
        {
            new Vector3(-7f, 0.9f, 7.5f),
            new Vector3(7f, 0.9f, 7.5f),
            new Vector3(-7f, 0.9f, -6f),
            new Vector3(7f, 0.9f, -6f)
        };

        private void Start()
        {
            catalog = Resources.Load<SimpleApocalypseCatalog>("SimpleApocalypseCatalog");
            BuildArena();
            SpawnInitialWave();

            Health playerHealth = GameObject.FindGameObjectWithTag("Player")?.GetComponent<Health>();
            if (playerHealth != null)
            {
                playerHealth.Died += _ =>
                {
                    Defeat = true;
                    if (ShardstepClock.Instance != null)
                    {
                        ShardstepClock.Instance.ForcedFreeze = true;
                    }
                };
            }
        }

        private void Update()
        {
            if (ReinforcementsDeployed)
            {
                return;
            }

            ShardstepClock clock = ShardstepClock.Instance;
            if (kills >= 3 || (clock != null && clock.CompletedActions >= ReinforcementActionThreshold))
            {
                DeployReinforcements();
            }
        }

        public void ReportEnemyDeath(EnemyAgent enemy)
        {
            kills++;
            RemainingEnemies = Mathf.Max(0, RemainingEnemies - 1);

            if (!ReinforcementsDeployed && kills >= 3)
            {
                DeployReinforcements();
            }

            if (ReinforcementsDeployed && RemainingEnemies == 0)
            {
                UnlockExit();
            }
        }

        public void CompleteRun()
        {
            if (RemainingEnemies > 0 || Victory || Defeat)
            {
                return;
            }

            Victory = true;
            if (ShardstepClock.Instance != null)
            {
                ShardstepClock.Instance.ForcedFreeze = true;
            }
        }

        private void SpawnInitialWave()
        {
            EnemyKind[] kinds =
            {
                EnemyKind.Gunner,
                EnemyKind.Gunner,
                EnemyKind.Hound,
                EnemyKind.Hound,
                EnemyKind.Sweeper,
                EnemyKind.Gunner
            };

            for (int index = 0; index < initialPositions.Length; index++)
            {
                SpawnEnemy(kinds[index], initialPositions[index]);
            }
        }

        private void DeployReinforcements()
        {
            if (ReinforcementsDeployed)
            {
                return;
            }

            ReinforcementsDeployed = true;
            EnemyKind[] kinds =
            {
                EnemyKind.Hound,
                EnemyKind.Gunner,
                EnemyKind.Hound,
                EnemyKind.Sweeper
            };

            for (int index = 0; index < reinforcementPositions.Length; index++)
            {
                SpawnEnemy(kinds[index], reinforcementPositions[index]);
            }
        }

        private void SpawnEnemy(EnemyKind kind, Vector3 position)
        {
            PrimitiveType primitive = kind == EnemyKind.Sweeper
                ? PrimitiveType.Cylinder
                : PrimitiveType.Capsule;
            GameObject enemy = GameObject.CreatePrimitive(primitive);
            enemy.name = kind.ToString();
            enemy.transform.position = position;
            enemy.transform.localScale = kind == EnemyKind.Hound
                ? new Vector3(0.9f, 0.65f, 1.35f)
                : kind == EnemyKind.Sweeper
                    ? new Vector3(1.45f, 0.55f, 1.45f)
                    : Vector3.one;

            Collider primitiveCollider = enemy.GetComponent<Collider>();
            if (primitiveCollider != null)
            {
                Destroy(primitiveCollider);
            }

            CharacterController controller = enemy.AddComponent<CharacterController>();
            controller.height = kind == EnemyKind.Hound ? 1.1f : 1.8f;
            controller.radius = kind == EnemyKind.Sweeper ? 0.75f : 0.45f;
            controller.center = Vector3.zero;

            Color color = kind == EnemyKind.Gunner
                ? new Color(1f, 0.62f, 0.12f)
                : kind == EnemyKind.Hound
                    ? new Color(0.95f, 0.08f, 0.06f)
                    : new Color(0.72f, 0.1f, 0.95f);
            enemy.GetComponent<Renderer>().material = ShardstepVisuals.CreateMaterial(color, true);

            Health health = enemy.AddComponent<Health>();
            health.Configure(kind == EnemyKind.Sweeper ? 5 : 3);
            EnemyAgent agent = enemy.AddComponent<EnemyAgent>();
            agent.Configure(kind, this);
            RemainingEnemies++;
        }

        private void BuildArena()
        {
            CreatePrimitiveEnvironment(
                "Ground",
                PrimitiveType.Cube,
                new Vector3(0f, -0.15f, 0f),
                new Vector3(18f, 0.3f, 22f),
                new Color(0.08f, 0.1f, 0.12f));

            CreateAssetOrFallback("SecurityWall", "North Wall", new Vector3(0f, 1.4f, 11f), new Vector3(18f, 2.8f, 0.65f));
            CreateAssetOrFallback("SecurityWall", "South Wall", new Vector3(0f, 1.4f, -11f), new Vector3(18f, 2.8f, 0.65f));
            CreateAssetOrFallback("SecurityWall", "West Wall", new Vector3(-9f, 1.4f, 0f), new Vector3(0.65f, 2.8f, 22f));
            CreateAssetOrFallback("SecurityWall", "East Wall", new Vector3(9f, 1.4f, 0f), new Vector3(0.65f, 2.8f, 22f));

            CreateAssetOrFallback("ShippingContainer", "Container A", new Vector3(-4.8f, 1.05f, -3.5f), new Vector3(2.4f, 2.1f, 4.6f));
            CreateAssetOrFallback("ShippingContainer", "Container B", new Vector3(5.1f, 1.05f, 2.5f), new Vector3(2.4f, 2.1f, 4.6f));
            CreateAssetOrFallback("Barrier", "Barrier A", new Vector3(0f, 0.65f, -2.4f), new Vector3(4f, 1.3f, 0.7f));
            CreateAssetOrFallback("Barrier", "Barrier B", new Vector3(-2.8f, 0.65f, 6.3f), new Vector3(3f, 1.3f, 0.7f));
            CreateAssetOrFallback("Rubble", "Rubble A", new Vector3(6.4f, 0.3f, -6.8f), new Vector3(1.8f, 0.6f, 1.8f));

            ChronospherePickup.Spawn(
                PickupKind.Ammo,
                new Vector3(-6.5f, 0.42f, 4.7f));
            ChronospherePickup.Spawn(
                PickupKind.Health,
                new Vector3(6.6f, 0.42f, -5.8f));

            exitObject = CreateAssetOrFallback(
                "SecurityGate",
                "Extraction Gate",
                new Vector3(0f, 1.45f, 10.3f),
                new Vector3(4.5f, 2.9f, 0.7f));
            Renderer exitRenderer = exitObject.GetComponentInChildren<Renderer>();
            if (exitRenderer != null)
            {
                exitRenderer.material = ShardstepVisuals.CreateMaterial(
                    new Color(0.9f, 0.12f, 0.08f),
                    true);
            }
        }

        private void UnlockExit()
        {
            if (exitObject == null)
            {
                return;
            }

            exitObject.transform.position += Vector3.up * 3.4f;
            GameObject trigger = new GameObject("Extraction Trigger");
            trigger.transform.position = new Vector3(0f, 1f, 9.6f);
            BoxCollider collider = trigger.AddComponent<BoxCollider>();
            collider.isTrigger = true;
            collider.size = new Vector3(4f, 2f, 2f);
            trigger.AddComponent<ExtractionZone>().director = this;

            GameObject glow = CreatePrimitiveEnvironment(
                "Exit Glow",
                PrimitiveType.Cube,
                new Vector3(0f, 0.04f, 9.6f),
                new Vector3(4f, 0.08f, 2f),
                new Color(0.1f, 1f, 0.95f));
            glow.GetComponent<Renderer>().material = ShardstepVisuals.CreateMaterial(
                new Color(0.1f, 1f, 0.95f),
                true);
        }

        private GameObject CreateAssetOrFallback(
            string key,
            string objectName,
            Vector3 position,
            Vector3 scale)
        {
            GameObject source = catalog != null ? catalog.Find(key) : null;
            GameObject instance;
            if (source != null)
            {
                instance = Instantiate(source, position, Quaternion.identity);
                instance.transform.localScale = scale;
            }
            else
            {
                instance = CreatePrimitiveEnvironment(
                    objectName,
                    PrimitiveType.Cube,
                    position,
                    scale,
                    new Color(0.24f, 0.28f, 0.3f));
            }

            instance.name = objectName;
            return instance;
        }

        private static GameObject CreatePrimitiveEnvironment(
            string objectName,
            PrimitiveType type,
            Vector3 position,
            Vector3 scale,
            Color color)
        {
            GameObject instance = GameObject.CreatePrimitive(type);
            instance.name = objectName;
            instance.transform.position = position;
            instance.transform.localScale = scale;
            Renderer renderer = instance.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.material = ShardstepVisuals.CreateMaterial(color);
            }
            return instance;
        }
    }
}
