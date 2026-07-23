using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Shardstep
{
    public enum WeaponMode
    {
        Rail,
        Blade
    }

    public enum EnemyKind
    {
        Gunner,
        Hound,
        Sweeper
    }

    [Serializable]
    public sealed class SimpleApocalypseCatalogEntry
    {
        public string key;
        public GameObject prefab;
    }

    [CreateAssetMenu(menuName = "SHARDSTEP/SIMPLE Apocalypse Catalog")]
    public sealed class SimpleApocalypseCatalog : ScriptableObject
    {
        public List<SimpleApocalypseCatalogEntry> entries = new List<SimpleApocalypseCatalogEntry>();

        public GameObject Find(string key)
        {
            foreach (SimpleApocalypseCatalogEntry entry in entries)
            {
                if (entry != null && string.Equals(entry.key, key, StringComparison.OrdinalIgnoreCase))
                {
                    return entry.prefab;
                }
            }

            return null;
        }
    }

    public static class ShardstepVisuals
    {
        public static Material CreateMaterial(Color color, bool emission = false)
        {
            bool urp = GraphicsSettings.currentRenderPipeline != null;
            Shader shader = Shader.Find(urp ? "Universal Render Pipeline/Lit" : "Standard");
            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
            }

            Material material = new Material(shader)
            {
                color = color
            };

            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            if (emission)
            {
                material.EnableKeyword("_EMISSION");
                Color emitted = color * 2.5f;
                if (material.HasProperty("_EmissionColor"))
                {
                    material.SetColor("_EmissionColor", emitted);
                }
            }

            return material;
        }
    }

    public sealed class ShardstepClock : MonoBehaviour
    {
        public static ShardstepClock Instance { get; private set; }

        public bool IsAiming { get; set; }
        public float MovementMagnitude { get; set; }
        public float ActiveSeconds { get; private set; }
        public bool ForcedFreeze { get; set; }

        private float executionUntilRealtime;

        public bool IsExecuting => Time.realtimeSinceStartup < executionUntilRealtime;

        private void Awake()
        {
            Instance = this;
            Time.timeScale = 0f;
        }

        private void Update()
        {
            float target = ForcedFreeze
                ? 0f
                : ComputeTimeScale(IsAiming, IsExecuting, MovementMagnitude);

            Time.timeScale = target;
            ActiveSeconds += Time.unscaledDeltaTime * target;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            Time.timeScale = 1f;
        }

        public void ExecuteFor(float realSeconds)
        {
            executionUntilRealtime = Mathf.Max(executionUntilRealtime, Time.realtimeSinceStartup + realSeconds);
        }

        public static float ComputeTimeScale(bool aiming, bool executing, float movementMagnitude)
        {
            if (aiming)
            {
                return 0f;
            }

            if (executing)
            {
                return 1f;
            }

            float magnitude = Mathf.Clamp01(movementMagnitude);
            if (magnitude < 0.08f)
            {
                return 0f;
            }

            return Mathf.Lerp(0.18f, 1f, magnitude);
        }
    }

    public sealed class Health : MonoBehaviour
    {
        public int Current { get; private set; }
        public int Maximum { get; private set; }
        public bool IsDead { get; private set; }
        public event Action<Health> Died;

        public void Configure(int maximum)
        {
            Maximum = Mathf.Max(1, maximum);
            Current = Maximum;
            IsDead = false;
        }

        public void Damage(int amount)
        {
            if (IsDead || amount <= 0)
            {
                return;
            }

            Current = Mathf.Max(0, Current - amount);
            StartCoroutine(HitFlash());

            if (Current == 0)
            {
                IsDead = true;
                Died?.Invoke(this);
            }
        }

        private IEnumerator HitFlash()
        {
            Renderer[] renderers = GetComponentsInChildren<Renderer>();
            foreach (Renderer renderer in renderers)
            {
                if (renderer.material.HasProperty("_Color"))
                {
                    renderer.material.color = Color.white;
                }
            }

            yield return new WaitForSecondsRealtime(0.07f);
        }
    }

    public sealed class PlayerMotor : MonoBehaviour
    {
        public float speed = 5.8f;
        public Vector2 MoveInput { get; set; }

        private CharacterController controller;

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
        }

        private void Update()
        {
            if (controller == null || Time.deltaTime <= 0f)
            {
                return;
            }

            Vector3 direction = new Vector3(MoveInput.x, 0f, MoveInput.y);
            if (direction.sqrMagnitude > 1f)
            {
                direction.Normalize();
            }

            controller.Move(direction * speed * Time.deltaTime);
            if (direction.sqrMagnitude > 0.02f)
            {
                transform.forward = Vector3.Slerp(transform.forward, direction.normalized, 18f * Time.deltaTime);
            }
        }
    }

    public sealed class PlayerCombat : MonoBehaviour
    {
        public WeaponMode Mode { get; private set; } = WeaponMode.Rail;
        public bool IsAiming { get; private set; }
        public Vector3 AimDirection { get; private set; } = Vector3.forward;

        private CharacterController controller;
        private LineRenderer preview;
        private Material railMaterial;
        private Material bladeMaterial;
        private readonly HashSet<Health> bladeHits = new HashSet<Health>();

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            railMaterial = ShardstepVisuals.CreateMaterial(new Color(0.2f, 0.95f, 1f), true);
            bladeMaterial = ShardstepVisuals.CreateMaterial(new Color(0.8f, 0.25f, 1f), true);

            GameObject lineObject = new GameObject("Aim Preview");
            lineObject.transform.SetParent(transform, false);
            preview = lineObject.AddComponent<LineRenderer>();
            preview.positionCount = 2;
            preview.useWorldSpace = true;
            preview.enabled = false;
            preview.numCapVertices = 6;
        }

        public void SetWeapon(WeaponMode mode)
        {
            Mode = mode;
            RefreshPreview();
        }

        public void BeginAim()
        {
            IsAiming = true;
            if (ShardstepClock.Instance != null)
            {
                ShardstepClock.Instance.IsAiming = true;
            }

            preview.enabled = true;
            RefreshPreview();
        }

        public void SetAimDirection(Vector3 direction)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f)
            {
                return;
            }

            AimDirection = direction.normalized;
            transform.forward = AimDirection;
            RefreshPreview();
        }

        public void CancelAim()
        {
            IsAiming = false;
            preview.enabled = false;
            if (ShardstepClock.Instance != null)
            {
                ShardstepClock.Instance.IsAiming = false;
            }
        }

        public void ReleaseAim(float dragMagnitude)
        {
            if (!IsAiming)
            {
                return;
            }

            CancelAim();
            if (dragMagnitude < 28f)
            {
                return;
            }

            if (Mode == WeaponMode.Rail)
            {
                FireRail();
            }
            else
            {
                StartCoroutine(ExecuteBlade());
            }
        }

        private void RefreshPreview()
        {
            if (preview == null || !preview.enabled)
            {
                return;
            }

            float distance = Mode == WeaponMode.Rail ? 18f : 5.2f;
            Vector3 origin = transform.position + Vector3.up * 0.7f;
            Vector3 endpoint = origin + AimDirection * distance;

            RaycastHit[] hits = Physics.RaycastAll(origin + AimDirection * 0.45f, AimDirection, distance - 0.45f);
            Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
            foreach (RaycastHit hit in hits)
            {
                if (hit.collider.transform.IsChildOf(transform))
                {
                    continue;
                }

                endpoint = hit.point;
                break;
            }

            preview.material = Mode == WeaponMode.Rail ? railMaterial : bladeMaterial;
            preview.startWidth = Mode == WeaponMode.Rail ? 0.055f : 0.24f;
            preview.endWidth = preview.startWidth;
            preview.SetPosition(0, origin);
            preview.SetPosition(1, endpoint);
        }

        private void FireRail()
        {
            if (ShardstepClock.Instance != null)
            {
                ShardstepClock.Instance.ExecuteFor(0.12f);
            }

            Vector3 origin = transform.position + Vector3.up * 0.7f + AimDirection * 0.45f;
            float distance = 18f;
            Vector3 endpoint = origin + AimDirection * distance;

            RaycastHit[] hits = Physics.RaycastAll(origin, AimDirection, distance);
            Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
            foreach (RaycastHit hit in hits)
            {
                if (hit.collider.transform.IsChildOf(transform))
                {
                    continue;
                }

                endpoint = hit.point;
                Health health = hit.collider.GetComponentInParent<Health>();
                if (health != null && health.gameObject != gameObject)
                {
                    health.Damage(2);
                }
                break;
            }

            StartCoroutine(FlashBeam(origin, endpoint, railMaterial, 0.11f));
        }

        private IEnumerator ExecuteBlade()
        {
            if (ShardstepClock.Instance != null)
            {
                ShardstepClock.Instance.ExecuteFor(0.35f);
            }

            bladeHits.Clear();
            float duration = 0.24f;
            float elapsed = 0f;
            Vector3 start = transform.position;
            float travel = 5.2f;

            while (elapsed < duration)
            {
                float delta = Time.deltaTime;
                elapsed += delta;
                if (controller != null)
                {
                    controller.Move(AimDirection * (travel / duration) * delta);
                }

                Collider[] overlaps = Physics.OverlapSphere(transform.position + Vector3.up * 0.5f, 0.85f);
                foreach (Collider overlap in overlaps)
                {
                    Health health = overlap.GetComponentInParent<Health>();
                    if (health != null && health.gameObject != gameObject && bladeHits.Add(health))
                    {
                        health.Damage(3);
                    }
                }

                yield return null;
            }

            StartCoroutine(FlashBeam(start + Vector3.up * 0.45f, transform.position + Vector3.up * 0.45f, bladeMaterial, 0.14f));
        }

        private IEnumerator FlashBeam(Vector3 from, Vector3 to, Material material, float duration)
        {
            GameObject flashObject = new GameObject("Attack Flash");
            LineRenderer line = flashObject.AddComponent<LineRenderer>();
            line.material = material;
            line.positionCount = 2;
            line.startWidth = material == bladeMaterial ? 0.42f : 0.13f;
            line.endWidth = line.startWidth;
            line.numCapVertices = 8;
            line.SetPosition(0, from);
            line.SetPosition(1, to);
            yield return new WaitForSecondsRealtime(duration);
            Destroy(flashObject);
        }
    }

    public sealed class EnemyAgent : MonoBehaviour
    {
        public EnemyKind kind;
        public ArenaDirector director;

        private Transform player;
        private CharacterController controller;
        private Health health;
        private LineRenderer telegraph;
        private Vector3 lockedDirection;
        private float cooldown;
        private float telegraphRemaining;
        private float attackRemaining;
        private bool attacking;
        private bool dealtContactDamage;
        private float beamDamageCooldown;

        public void Configure(EnemyKind enemyKind, ArenaDirector arenaDirector)
        {
            kind = enemyKind;
            director = arenaDirector;
        }

        private void Start()
        {
            player = GameObject.FindGameObjectWithTag("Player")?.transform;
            controller = GetComponent<CharacterController>();
            health = GetComponent<Health>();
            if (health != null)
            {
                health.Died += OnDeath;
            }

            telegraph = new GameObject("Attack Telegraph").AddComponent<LineRenderer>();
            telegraph.transform.SetParent(transform, false);
            telegraph.useWorldSpace = true;
            telegraph.positionCount = 2;
            telegraph.numCapVertices = 6;
            telegraph.enabled = false;
            telegraph.material = ShardstepVisuals.CreateMaterial(TelegraphColor(), true);
            cooldown = UnityEngine.Random.Range(0.35f, 0.9f);
        }

        private void Update()
        {
            if (player == null || Time.deltaTime <= 0f || (health != null && health.IsDead))
            {
                return;
            }

            if (attacking)
            {
                UpdateAttack();
                return;
            }

            if (telegraphRemaining > 0f)
            {
                telegraphRemaining -= Time.deltaTime;
                UpdateTelegraph();
                if (telegraphRemaining <= 0f)
                {
                    BeginAttack();
                }
                return;
            }

            MoveTactically();
            cooldown -= Time.deltaTime;
            if (cooldown <= 0f)
            {
                StartTelegraph();
            }
        }

        private void MoveTactically()
        {
            Vector3 toPlayer = player.position - transform.position;
            toPlayer.y = 0f;
            float distance = toPlayer.magnitude;
            if (distance < 0.01f)
            {
                return;
            }

            Vector3 direction = toPlayer / distance;
            float speed;
            if (kind == EnemyKind.Gunner)
            {
                direction = distance < 6f ? -direction : direction;
                speed = 1.35f;
            }
            else if (kind == EnemyKind.Hound)
            {
                speed = 2.35f;
            }
            else
            {
                direction = Vector3.Cross(Vector3.up, direction);
                speed = 0.75f;
            }

            transform.forward = Vector3.Slerp(transform.forward, direction, 8f * Time.deltaTime);
            if (!Physics.Raycast(transform.position + Vector3.up * 0.5f, direction, 0.7f))
            {
                controller?.Move(direction * speed * Time.deltaTime);
            }
        }

        private void StartTelegraph()
        {
            Vector3 toPlayer = player.position - transform.position;
            toPlayer.y = 0f;
            lockedDirection = toPlayer.sqrMagnitude > 0.001f ? toPlayer.normalized : transform.forward;
            transform.forward = lockedDirection;
            dealtContactDamage = false;
            beamDamageCooldown = 0f;

            telegraphRemaining = kind == EnemyKind.Gunner ? 0.72f : kind == EnemyKind.Hound ? 0.62f : 1.02f;
            telegraph.enabled = true;
            UpdateTelegraph();
        }

        private void UpdateTelegraph()
        {
            float length = kind == EnemyKind.Gunner ? 15f : kind == EnemyKind.Hound ? 7f : 13f;
            Vector3 origin = transform.position + Vector3.up * 0.65f;
            Vector3 endpoint = origin + lockedDirection * length;
            if (Physics.Raycast(origin + lockedDirection * 0.45f, lockedDirection, out RaycastHit hit, length - 0.45f))
            {
                endpoint = hit.point;
            }

            telegraph.startWidth = kind == EnemyKind.Gunner ? 0.055f : kind == EnemyKind.Hound ? 0.38f : 1.25f;
            telegraph.endWidth = telegraph.startWidth;
            telegraph.SetPosition(0, origin);
            telegraph.SetPosition(1, endpoint);
        }

        private void BeginAttack()
        {
            attacking = true;
            if (kind == EnemyKind.Gunner)
            {
                attackRemaining = 0.13f;
                FireGunner();
            }
            else if (kind == EnemyKind.Hound)
            {
                attackRemaining = 0.34f;
            }
            else
            {
                attackRemaining = 0.55f;
            }
        }

        private void UpdateAttack()
        {
            attackRemaining -= Time.deltaTime;

            if (kind == EnemyKind.Hound)
            {
                controller?.Move(lockedDirection * 9.2f * Time.deltaTime);
                if (!dealtContactDamage && Vector3.Distance(transform.position, player.position) < 1.15f)
                {
                    DamagePlayer(1);
                    dealtContactDamage = true;
                }
            }
            else if (kind == EnemyKind.Sweeper)
            {
                beamDamageCooldown -= Time.deltaTime;
                if (beamDamageCooldown <= 0f)
                {
                    float distance = DistancePointToSegmentXZ(player.position, telegraph.GetPosition(0), telegraph.GetPosition(1));
                    if (distance < 0.82f)
                    {
                        DamagePlayer(1);
                    }
                    beamDamageCooldown = 0.26f;
                }
            }

            if (attackRemaining <= 0f)
            {
                attacking = false;
                telegraph.enabled = false;
                cooldown = kind == EnemyKind.Gunner ? 1.15f : kind == EnemyKind.Hound ? 1.45f : 2.05f;
            }
        }

        private void FireGunner()
        {
            Vector3 origin = transform.position + Vector3.up * 0.65f + lockedDirection * 0.4f;
            if (Physics.Raycast(origin, lockedDirection, out RaycastHit hit, 15f))
            {
                Health targetHealth = hit.collider.GetComponentInParent<Health>();
                if (targetHealth != null && hit.collider.transform.IsChildOf(player))
                {
                    targetHealth.Damage(1);
                }
            }
        }

        private void DamagePlayer(int amount)
        {
            Health target = player.GetComponent<Health>();
            target?.Damage(amount);
        }

        private Color TelegraphColor()
        {
            return kind == EnemyKind.Gunner
                ? new Color(1f, 0.65f, 0.12f)
                : kind == EnemyKind.Hound
                    ? new Color(1f, 0.1f, 0.08f)
                    : new Color(0.78f, 0.15f, 1f);
        }

        private static float DistancePointToSegmentXZ(Vector3 point, Vector3 start, Vector3 end)
        {
            Vector2 p = new Vector2(point.x, point.z);
            Vector2 a = new Vector2(start.x, start.z);
            Vector2 b = new Vector2(end.x, end.z);
            Vector2 segment = b - a;
            float denominator = segment.sqrMagnitude;
            if (denominator < 0.0001f)
            {
                return Vector2.Distance(p, a);
            }

            float t = Mathf.Clamp01(Vector2.Dot(p - a, segment) / denominator);
            return Vector2.Distance(p, a + segment * t);
        }

        private void OnDeath(Health deadHealth)
        {
            telegraph.enabled = false;
            director?.ReportEnemyDeath(this);
            Destroy(gameObject, 0.06f);
        }
    }

    public sealed class ArenaDirector : MonoBehaviour
    {
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
            if (!ReinforcementsDeployed && (kills >= 3 || (ShardstepClock.Instance != null && ShardstepClock.Instance.ActiveSeconds >= 3.2f)))
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
            if (RemainingEnemies > 0 || Victory)
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
            PrimitiveType primitive = kind == EnemyKind.Sweeper ? PrimitiveType.Cylinder : PrimitiveType.Capsule;
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
            controller.center = new Vector3(0f, controller.height * 0.5f, 0f);

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
            CreatePrimitiveEnvironment("Ground", PrimitiveType.Cube, new Vector3(0f, -0.15f, 0f), new Vector3(18f, 0.3f, 22f), new Color(0.08f, 0.1f, 0.12f));

            CreateAssetOrFallback("SecurityWall", "North Wall", new Vector3(0f, 1.4f, 11f), new Vector3(18f, 2.8f, 0.65f));
            CreateAssetOrFallback("SecurityWall", "South Wall", new Vector3(0f, 1.4f, -11f), new Vector3(18f, 2.8f, 0.65f));
            CreateAssetOrFallback("SecurityWall", "West Wall", new Vector3(-9f, 1.4f, 0f), new Vector3(0.65f, 2.8f, 22f));
            CreateAssetOrFallback("SecurityWall", "East Wall", new Vector3(9f, 1.4f, 0f), new Vector3(0.65f, 2.8f, 22f));

            CreateAssetOrFallback("ShippingContainer", "Container A", new Vector3(-4.8f, 1.05f, -3.5f), new Vector3(2.4f, 2.1f, 4.6f));
            CreateAssetOrFallback("ShippingContainer", "Container B", new Vector3(5.1f, 1.05f, 2.5f), new Vector3(2.4f, 2.1f, 4.6f));
            CreateAssetOrFallback("Barrier", "Barrier A", new Vector3(0f, 0.65f, -2.4f), new Vector3(4f, 1.3f, 0.7f));
            CreateAssetOrFallback("Barrier", "Barrier B", new Vector3(-2.8f, 0.65f, 6.3f), new Vector3(3f, 1.3f, 0.7f));
            CreateAssetOrFallback("Rubble", "Rubble A", new Vector3(6.4f, 0.3f, -6.8f), new Vector3(1.8f, 0.6f, 1.8f));
            CreateAssetOrFallback("AmmoCrate", "Ammo Crate", new Vector3(-6.5f, 0.45f, 4.7f), new Vector3(1.1f, 0.9f, 1.1f));

            exitObject = CreateAssetOrFallback("SecurityGate", "Extraction Gate", new Vector3(0f, 1.45f, 10.3f), new Vector3(4.5f, 2.9f, 0.7f));
            Renderer exitRenderer = exitObject.GetComponentInChildren<Renderer>();
            if (exitRenderer != null)
            {
                exitRenderer.material = ShardstepVisuals.CreateMaterial(new Color(0.9f, 0.12f, 0.08f), true);
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

            GameObject glow = CreatePrimitiveEnvironment("Exit Glow", PrimitiveType.Cube, new Vector3(0f, 0.04f, 9.6f), new Vector3(4f, 0.08f, 2f), new Color(0.1f, 1f, 0.95f));
            glow.GetComponent<Renderer>().material = ShardstepVisuals.CreateMaterial(new Color(0.1f, 1f, 0.95f), true);
        }

        private GameObject CreateAssetOrFallback(string key, string objectName, Vector3 position, Vector3 scale)
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
                instance = CreatePrimitiveEnvironment(objectName, PrimitiveType.Cube, position, scale, new Color(0.24f, 0.28f, 0.3f));
            }

            instance.name = objectName;
            return instance;
        }

        private static GameObject CreatePrimitiveEnvironment(string objectName, PrimitiveType type, Vector3 position, Vector3 scale, Color color)
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
        private PlayerMotor motor;
        private PlayerCombat combat;
        private ArenaDirector director;
        private Health playerHealth;

        private int movementFinger = -1;
        private int aimFinger = -1;
        private Vector2 movementStart;
        private Vector2 aimStart;
        private Vector2 aimCurrent;

        private void Start()
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            motor = player.GetComponent<PlayerMotor>();
            combat = player.GetComponent<PlayerCombat>();
            playerHealth = player.GetComponent<Health>();
            director = FindObjectOfType<ArenaDirector>();
        }

        private void Update()
        {
            Vector2 movement = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
            HandleTouches(ref movement);
            HandleDesktopAim();

            movement = Vector2.ClampMagnitude(movement, 1f);
            motor.MoveInput = movement;
            if (ShardstepClock.Instance != null)
            {
                ShardstepClock.Instance.MovementMagnitude = movement.magnitude;
            }

            if (Input.GetKeyDown(KeyCode.Q))
            {
                combat.SetWeapon(combat.Mode == WeaponMode.Rail ? WeaponMode.Blade : WeaponMode.Rail);
            }
        }

        private void HandleTouches(ref Vector2 movement)
        {
            for (int index = 0; index < Input.touchCount; index++)
            {
                Touch touch = Input.GetTouch(index);
                if (touch.phase == TouchPhase.Began)
                {
                    if (touch.position.x < Screen.width * 0.55f && movementFinger < 0)
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
                        Vector2 delta = touch.position - movementStart;
                        movement = Vector2.ClampMagnitude(delta / 75f, 1f);
                    }
                    else if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled)
                    {
                        movementFinger = -1;
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

        private void OnGUI()
        {
            float scale = Mathf.Clamp(Screen.width / 390f, 0.85f, 1.35f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            float width = Screen.width / scale;

            GUI.Box(new Rect(10f, 10f, 210f, 82f), "SHARDSTEP UNITY SLICE");
            GUI.Label(new Rect(20f, 35f, 195f, 24f), ShardstepClock.Instance != null && Time.timeScale <= 0.001f ? "TIME: FROZEN" : "TIME: MOVING");
            GUI.Label(new Rect(20f, 57f, 195f, 24f), $"HP {playerHealth?.Current}/{playerHealth?.Maximum}  ENEMIES {director?.RemainingEnemies}");

            if (GUI.Button(new Rect(width - 180f, 12f, 78f, 48f), "RAIL"))
            {
                combat.SetWeapon(WeaponMode.Rail);
            }

            if (GUI.Button(new Rect(width - 94f, 12f, 78f, 48f), "BLADE"))
            {
                combat.SetWeapon(WeaponMode.Blade);
            }

            GUI.Label(new Rect(width - 180f, 66f, 170f, 24f), $"EQUIPPED: {combat?.Mode}");
            GUI.Label(new Rect(12f, Screen.height / scale - 54f, 340f, 24f), "LEFT DRAG: MOVE   RIGHT DRAG: AIM / RELEASE");

            if (director != null && (director.Victory || director.Defeat))
            {
                string result = director.Victory ? "EXTRACTION COMPLETE" : "TIMELINE COLLAPSED";
                GUI.Box(new Rect(width * 0.5f - 125f, 115f, 250f, 92f), result);
                if (GUI.Button(new Rect(width * 0.5f - 75f, 155f, 150f, 38f), "RESTART"))
                {
                    UnityEngine.SceneManagement.SceneManager.LoadScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
                }
            }
        }
    }

    public sealed class CameraRig : MonoBehaviour
    {
        public Transform target;
        private Vector3 offset = new Vector3(0f, 15.5f, -12.5f);

        private void LateUpdate()
        {
            if (target == null)
            {
                return;
            }

            Vector3 desired = target.position + offset;
            transform.position = Vector3.Lerp(transform.position, desired, 7f * Time.unscaledDeltaTime);
        }
    }

    public static class ShardstepBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreatePrototype()
        {
            if (UnityEngine.Object.FindObjectOfType<ShardstepClock>() != null)
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
                UnityEngine.Object.Destroy(primitiveCollider);
            }

            CharacterController controller = player.AddComponent<CharacterController>();
            controller.height = 1.8f;
            controller.radius = 0.42f;
            controller.center = new Vector3(0f, 0.9f, 0f);
            player.GetComponent<Renderer>().material = ShardstepVisuals.CreateMaterial(new Color(0.12f, 0.95f, 1f), true);
            player.AddComponent<PlayerMotor>();
            player.AddComponent<PlayerCombat>();
            Health playerHealth = player.AddComponent<Health>();
            playerHealth.Configure(4);

            GameObject cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.025f, 0.035f, 0.05f);
            camera.fieldOfView = 52f;
            cameraObject.transform.position = player.transform.position + new Vector3(0f, 15.5f, -12.5f);
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
            root.AddComponent<ShardstepInput>();
        }
    }
}
