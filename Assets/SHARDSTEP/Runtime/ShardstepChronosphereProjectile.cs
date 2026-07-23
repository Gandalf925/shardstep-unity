using System;
using System.Collections;
using UnityEngine;

namespace Shardstep
{
    public enum ProjectileFaction
    {
        Player,
        Enemy
    }

    public enum PickupKind
    {
        Ammo,
        Health
    }

    public static class ChronosphereProjectilePolicy
    {
        public static float DistancePerAction(float speed)
        {
            return Mathf.Max(0f, speed) * ShardstepClock.StandardActionDuration;
        }
    }

    public sealed class ChronosphereProjectile : MonoBehaviour
    {
        private const float TailLength = 0.72f;

        private Vector3 direction;
        private float speed;
        private int damage;
        private ProjectileFaction faction;
        private GameObject owner;
        private float radius;
        private float remainingLifetime;
        private LineRenderer tail;
        private Material material;
        private bool resolved;

        public ProjectileFaction Faction => faction;
        public bool IsResolved => resolved;

        public static ChronosphereProjectile Spawn(
            Vector3 origin,
            Vector3 direction,
            float speed,
            int damage,
            ProjectileFaction faction,
            GameObject owner,
            Color color,
            float radius,
            float lifetime)
        {
            GameObject projectileObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            projectileObject.name = faction == ProjectileFaction.Player
                ? "Player Chronosphere Round"
                : "Enemy Chronosphere Round";
            projectileObject.transform.position = origin;
            projectileObject.transform.localScale = Vector3.one * Mathf.Max(0.1f, radius * 2f);

            Collider primitiveCollider = projectileObject.GetComponent<Collider>();
            if (primitiveCollider != null)
            {
                UnityEngine.Object.Destroy(primitiveCollider);
            }

            ChronosphereProjectile projectile =
                projectileObject.AddComponent<ChronosphereProjectile>();
            projectile.Configure(
                direction,
                speed,
                damage,
                faction,
                owner,
                color,
                radius,
                lifetime);
            return projectile;
        }

        private void Configure(
            Vector3 travelDirection,
            float travelSpeed,
            int hitDamage,
            ProjectileFaction projectileFaction,
            GameObject sourceOwner,
            Color color,
            float collisionRadius,
            float lifetime)
        {
            travelDirection.y = 0f;
            direction = travelDirection.sqrMagnitude > 0.0001f
                ? travelDirection.normalized
                : Vector3.forward;
            speed = Mathf.Max(0.1f, travelSpeed);
            damage = Mathf.Max(1, hitDamage);
            faction = projectileFaction;
            owner = sourceOwner;
            radius = Mathf.Max(0.05f, collisionRadius);
            remainingLifetime = Mathf.Max(0.1f, lifetime);
            transform.forward = direction;

            Renderer renderer = GetComponent<Renderer>();
            material = ShardstepVisuals.CreateMaterial(color, true);
            if (renderer != null)
            {
                renderer.material = material;
            }

            GameObject tailObject = new GameObject("Projectile Tail");
            tailObject.transform.SetParent(transform, false);
            tail = tailObject.AddComponent<LineRenderer>();
            tail.useWorldSpace = true;
            tail.positionCount = 2;
            tail.startWidth = radius * 0.78f;
            tail.endWidth = 0.015f;
            tail.numCapVertices = 5;
            tail.material = material;
            RefreshTail();
        }

        private void Update()
        {
            if (resolved || Time.deltaTime <= 0f)
            {
                RefreshTail();
                return;
            }

            float delta = Time.deltaTime;
            remainingLifetime -= delta;
            if (remainingLifetime <= 0f)
            {
                ResolveImpact(false);
                return;
            }

            float travel = speed * delta;
            Vector3 origin = transform.position;
            RaycastHit[] hits = Physics.SphereCastAll(
                origin,
                radius,
                direction,
                travel,
                ~0,
                QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));

            foreach (RaycastHit hit in hits)
            {
                if (ShouldIgnore(hit.collider))
                {
                    continue;
                }

                transform.position = origin + direction * Mathf.Max(0f, hit.distance);
                Health target = hit.collider.GetComponentInParent<Health>();
                if (target != null && CanDamage(target))
                {
                    target.Damage(damage);
                }

                ResolveImpact(true);
                return;
            }

            transform.position = origin + direction * travel;
            RefreshTail();
        }

        private bool ShouldIgnore(Collider collider)
        {
            if (collider == null)
            {
                return true;
            }

            Transform hitTransform = collider.transform;
            if (owner != null &&
                (hitTransform == owner.transform || hitTransform.IsChildOf(owner.transform)))
            {
                return true;
            }

            if (hitTransform == transform || hitTransform.IsChildOf(transform))
            {
                return true;
            }

            return collider.GetComponentInParent<ChronosphereProjectile>() != null;
        }

        private bool CanDamage(Health target)
        {
            if (target == null || target.gameObject == owner)
            {
                return false;
            }

            bool targetIsPlayer = target.CompareTag("Player");
            return faction == ProjectileFaction.Enemy
                ? targetIsPlayer
                : !targetIsPlayer;
        }

        public bool BreakByBlade()
        {
            if (resolved || faction != ProjectileFaction.Enemy)
            {
                return false;
            }

            ResolveImpact(true);
            return true;
        }

        private void RefreshTail()
        {
            if (tail == null)
            {
                return;
            }

            Vector3 current = transform.position;
            tail.SetPosition(0, current);
            tail.SetPosition(1, current - direction * TailLength);
        }

        private void ResolveImpact(bool flash)
        {
            if (resolved)
            {
                return;
            }

            resolved = true;
            if (tail != null)
            {
                tail.enabled = false;
            }

            if (flash)
            {
                StartCoroutine(ImpactFlash());
            }
            else
            {
                Destroy(gameObject);
            }
        }

        private IEnumerator ImpactFlash()
        {
            transform.localScale *= 1.8f;
            yield return new WaitForSecondsRealtime(0.05f);
            Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (material != null)
            {
                Destroy(material);
            }
        }
    }

    public sealed class ChronospherePickup : MonoBehaviour
    {
        private PickupKind kind;
        private Material material;
        private float baseY;

        public static ChronospherePickup Spawn(PickupKind kind, Vector3 position)
        {
            GameObject pickupObject = GameObject.CreatePrimitive(
                kind == PickupKind.Ammo ? PrimitiveType.Cube : PrimitiveType.Sphere);
            pickupObject.name = kind == PickupKind.Ammo ? "Rail Ammo Pickup" : "Health Pickup";
            pickupObject.transform.position = position;
            pickupObject.transform.localScale = kind == PickupKind.Ammo
                ? new Vector3(0.7f, 0.45f, 0.7f)
                : Vector3.one * 0.62f;

            Collider collider = pickupObject.GetComponent<Collider>();
            if (collider != null)
            {
                collider.isTrigger = true;
            }

            ChronospherePickup pickup = pickupObject.AddComponent<ChronospherePickup>();
            pickup.Configure(kind);
            return pickup;
        }

        private void Configure(PickupKind pickupKind)
        {
            kind = pickupKind;
            baseY = transform.position.y;
            Color color = kind == PickupKind.Ammo
                ? new Color(0.15f, 0.9f, 1f)
                : new Color(0.18f, 1f, 0.35f);
            material = ShardstepVisuals.CreateMaterial(color, true);
            Renderer renderer = GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.material = material;
            }
        }

        private void Update()
        {
            float phase = Time.unscaledTime * 2.2f;
            transform.rotation = Quaternion.Euler(0f, phase * 35f, 0f);
            Vector3 position = transform.position;
            position.y = baseY + Mathf.Sin(phase) * 0.08f;
            transform.position = position;
        }

        private void OnTriggerEnter(Collider other)
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player == null ||
                (other.gameObject != player &&
                 !other.transform.IsChildOf(player.transform)))
            {
                return;
            }

            bool consumed;
            if (kind == PickupKind.Ammo)
            {
                PlayerCombat combat = player.GetComponent<PlayerCombat>();
                consumed = combat != null && combat.AddRailAmmo(2) > 0;
            }
            else
            {
                Health health = player.GetComponent<Health>();
                consumed = health != null && health.Heal(1) > 0;
            }

            if (consumed)
            {
                Destroy(gameObject);
            }
        }

        private void OnDestroy()
        {
            if (material != null)
            {
                Destroy(material);
            }
        }
    }
}
