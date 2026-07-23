using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Shardstep
{
    public sealed class PlayerCombat : MonoBehaviour
    {
        public const int RailMagazineSize = 4;
        public const float RailProjectileSpeed = 24f;
        public const float BladeTravelDistance = 4.1f;

        public WeaponMode Mode { get; private set; } = WeaponMode.Rail;
        public bool IsAiming { get; private set; }
        public Vector3 AimDirection { get; private set; } = Vector3.forward;
        public int RailAmmo { get; private set; } = RailMagazineSize;
        public bool IsReloading { get; private set; }
        public string AmmoLabel => $"{RailAmmo}/{RailMagazineSize}";

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

        public bool TryCycleWeapon()
        {
            ShardstepClock clock = ShardstepClock.Instance;
            if (clock == null || !clock.TryBeginAction())
            {
                return false;
            }

            Mode = Mode == WeaponMode.Rail ? WeaponMode.Blade : WeaponMode.Rail;
            CancelAim();
            return true;
        }

        public bool TryReload()
        {
            if (Mode != WeaponMode.Rail || RailAmmo >= RailMagazineSize || IsReloading)
            {
                return false;
            }

            ShardstepClock clock = ShardstepClock.Instance;
            if (clock == null || !clock.TryBeginAction())
            {
                return false;
            }

            CancelAim();
            StartCoroutine(CompleteReload(clock));
            return true;
        }

        private IEnumerator CompleteReload(ShardstepClock clock)
        {
            IsReloading = true;
            while (clock != null && clock.IsExecuting)
            {
                yield return null;
            }

            RailAmmo = RailMagazineSize;
            IsReloading = false;
        }

        public int AddRailAmmo(int amount)
        {
            if (amount <= 0)
            {
                return 0;
            }

            int before = RailAmmo;
            RailAmmo = Mathf.Min(RailMagazineSize, RailAmmo + amount);
            return RailAmmo - before;
        }

        public void BeginAim()
        {
            if (ShardstepClock.Instance != null && ShardstepClock.Instance.IsExecuting)
            {
                return;
            }

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
            if (preview != null)
            {
                preview.enabled = false;
            }

            if (ShardstepClock.Instance != null)
            {
                ShardstepClock.Instance.IsAiming = false;
            }
        }

        public bool ReleaseAim(float dragMagnitude)
        {
            if (!IsAiming)
            {
                return false;
            }

            CancelAim();
            if (dragMagnitude < 1f)
            {
                return false;
            }

            return Mode == WeaponMode.Rail ? FireRail() : BeginBlade();
        }

        private void RefreshPreview()
        {
            if (preview == null || !preview.enabled)
            {
                return;
            }

            float distance = Mode == WeaponMode.Rail ? 18f : BladeTravelDistance;
            Vector3 origin = transform.position + Vector3.up * 0.7f;
            Vector3 endpoint = origin + AimDirection * distance;

            RaycastHit[] hits = Physics.RaycastAll(
                origin + AimDirection * 0.45f,
                AimDirection,
                Mathf.Max(0f, distance - 0.45f),
                ~0,
                QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
            foreach (RaycastHit hit in hits)
            {
                if (hit.collider == null || hit.collider.transform.IsChildOf(transform))
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

        private bool FireRail()
        {
            if (RailAmmo <= 0)
            {
                return false;
            }

            ShardstepClock clock = ShardstepClock.Instance;
            if (clock == null || !clock.TryBeginAction())
            {
                return false;
            }

            RailAmmo--;
            Vector3 origin = transform.position + Vector3.up * 0.68f + AimDirection * 0.58f;
            ChronosphereProjectile.Spawn(
                origin,
                AimDirection,
                RailProjectileSpeed,
                2,
                ProjectileFaction.Player,
                gameObject,
                new Color(0.16f, 0.95f, 1f),
                0.18f,
                2.4f);
            StartCoroutine(FlashBeam(
                origin,
                origin + AimDirection * 1.1f,
                railMaterial,
                0.08f));
            return true;
        }

        private bool BeginBlade()
        {
            ShardstepClock clock = ShardstepClock.Instance;
            if (clock == null || !clock.TryBeginAction())
            {
                return false;
            }

            StartCoroutine(ExecuteBlade(clock));
            return true;
        }

        private IEnumerator ExecuteBlade(ShardstepClock clock)
        {
            bladeHits.Clear();
            Vector3 start = transform.position;
            Vector3 destination = ResolveBladeDestination(start, AimDirection);
            float duration = ShardstepClock.StandardActionDuration;
            float elapsed = 0f;

            while (clock != null && clock.IsExecuting && elapsed < duration)
            {
                float delta = Time.deltaTime;
                if (delta <= 0f)
                {
                    yield return null;
                    continue;
                }

                elapsed = Mathf.Min(duration, elapsed + delta);
                Vector3 desired = Vector3.Lerp(start, destination, elapsed / duration);
                Vector3 movement = desired - transform.position;
                movement.y = 0f;

                if (controller != null)
                {
                    controller.Move(movement);
                }
                else
                {
                    transform.position += movement;
                }

                Collider[] overlaps = Physics.OverlapSphere(
                    transform.position + Vector3.up * 0.5f,
                    0.88f,
                    ~0,
                    QueryTriggerInteraction.Collide);
                foreach (Collider overlap in overlaps)
                {
                    ChronosphereProjectile projectile =
                        overlap.GetComponentInParent<ChronosphereProjectile>();
                    if (projectile != null && projectile.BreakByBlade())
                    {
                        continue;
                    }

                    Health target = overlap.GetComponentInParent<Health>();
                    if (target != null && target.gameObject != gameObject &&
                        !target.CompareTag("Player") && bladeHits.Add(target))
                    {
                        target.Damage(3);
                    }
                }

                yield return null;
            }

            StartCoroutine(FlashBeam(
                start + Vector3.up * 0.45f,
                transform.position + Vector3.up * 0.45f,
                bladeMaterial,
                0.14f));
        }

        private Vector3 ResolveBladeDestination(Vector3 start, Vector3 direction)
        {
            float radius = controller != null ? controller.radius * 0.82f : 0.34f;
            Vector3 origin = start + Vector3.up * 0.55f;
            float distance = BladeTravelDistance;

            RaycastHit[] hits = Physics.SphereCastAll(
                origin,
                radius,
                direction,
                distance,
                ~0,
                QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
            foreach (RaycastHit hit in hits)
            {
                if (hit.collider == null || hit.collider.transform.IsChildOf(transform))
                {
                    continue;
                }

                Health target = hit.collider.GetComponentInParent<Health>();
                if (target != null && !target.CompareTag("Player"))
                {
                    continue;
                }

                distance = Mathf.Max(0f, hit.distance - 0.08f);
                break;
            }

            return start + direction * distance;
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

        private void OnDestroy()
        {
            if (railMaterial != null)
            {
                Destroy(railMaterial);
            }

            if (bladeMaterial != null)
            {
                Destroy(bladeMaterial);
            }
        }
    }
}
