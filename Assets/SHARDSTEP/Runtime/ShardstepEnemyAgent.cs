using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Shardstep
{
    public sealed class EnemyAgent : MonoBehaviour
    {
        public const float GunnerProjectileSpeed = 12.5f;

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

            int phase = Mathf.Abs(
                Mathf.RoundToInt(transform.position.x * 7f) +
                Mathf.RoundToInt(transform.position.z * 11f)) % 3;
            cooldown = (2f + phase) * ShardstepClock.StandardActionDuration;
        }

        private void Update()
        {
            if (player == null || Time.deltaTime <= 0f ||
                (health != null && health.IsDead) ||
                (director != null && (director.Victory || director.Defeat)))
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
                speed = 0.78f;
            }

            transform.forward = Vector3.Slerp(transform.forward, direction, 8f * Time.deltaTime);
            Vector3 origin = transform.position + Vector3.up * 0.55f;
            if (!Physics.SphereCast(
                    origin,
                    controller != null ? controller.radius * 0.7f : 0.3f,
                    direction,
                    out RaycastHit hit,
                    0.72f,
                    ~0,
                    QueryTriggerInteraction.Ignore) ||
                hit.collider == null ||
                hit.collider.transform.IsChildOf(transform))
            {
                controller?.Move(direction * speed * Time.deltaTime);
            }
        }

        private void StartTelegraph()
        {
            Vector3 toPlayer = player.position - transform.position;
            toPlayer.y = 0f;
            lockedDirection = toPlayer.sqrMagnitude > 0.001f
                ? toPlayer.normalized
                : transform.forward;
            transform.forward = lockedDirection;
            dealtContactDamage = false;
            beamDamageCooldown = 0f;

            telegraphRemaining = kind == EnemyKind.Gunner
                ? 2f * ShardstepClock.StandardActionDuration
                : kind == EnemyKind.Hound
                    ? 2f * ShardstepClock.StandardActionDuration
                    : 3f * ShardstepClock.StandardActionDuration;
            telegraph.enabled = true;
            UpdateTelegraph();
        }

        private void UpdateTelegraph()
        {
            float length = kind == EnemyKind.Gunner ? 15f : kind == EnemyKind.Hound ? 7f : 13f;
            Vector3 origin = transform.position + Vector3.up * 0.65f;
            Vector3 endpoint = origin + lockedDirection * length;
            if (Physics.Raycast(
                    origin + lockedDirection * 0.45f,
                    lockedDirection,
                    out RaycastHit hit,
                    length - 0.45f,
                    ~0,
                    QueryTriggerInteraction.Ignore))
            {
                endpoint = hit.point;
            }

            telegraph.startWidth = kind == EnemyKind.Gunner
                ? 0.08f
                : kind == EnemyKind.Hound
                    ? 0.38f
                    : 1.25f;
            telegraph.endWidth = telegraph.startWidth;
            telegraph.SetPosition(0, origin);
            telegraph.SetPosition(1, endpoint);
        }

        private void BeginAttack()
        {
            attacking = true;
            if (kind == EnemyKind.Gunner)
            {
                attackRemaining = ShardstepClock.StandardActionDuration;
                FireGunner();
            }
            else if (kind == EnemyKind.Hound)
            {
                attackRemaining = 2f * ShardstepClock.StandardActionDuration;
            }
            else
            {
                attackRemaining = 3f * ShardstepClock.StandardActionDuration;
            }
        }

        private void UpdateAttack()
        {
            attackRemaining -= Time.deltaTime;

            if (kind == EnemyKind.Hound)
            {
                controller?.Move(lockedDirection * 8.6f * Time.deltaTime);
                if (!dealtContactDamage &&
                    Vector3.Distance(transform.position, player.position) < 1.15f)
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
                    float distance = DistancePointToSegmentXZ(
                        player.position,
                        telegraph.GetPosition(0),
                        telegraph.GetPosition(1));
                    if (distance < 0.82f)
                    {
                        DamagePlayer(1);
                    }

                    beamDamageCooldown = ShardstepClock.StandardActionDuration;
                }
            }

            if (attackRemaining <= 0f)
            {
                attacking = false;
                telegraph.enabled = false;
                cooldown = kind == EnemyKind.Gunner
                    ? 4f * ShardstepClock.StandardActionDuration
                    : kind == EnemyKind.Hound
                        ? 5f * ShardstepClock.StandardActionDuration
                        : 7f * ShardstepClock.StandardActionDuration;
            }
        }

        private void FireGunner()
        {
            Vector3 origin = transform.position + Vector3.up * 0.65f + lockedDirection * 0.58f;
            ChronosphereProjectile.Spawn(
                origin,
                lockedDirection,
                GunnerProjectileSpeed,
                1,
                ProjectileFaction.Enemy,
                gameObject,
                new Color(1f, 0.55f, 0.08f),
                0.22f,
                3.2f);
        }

        private void DamagePlayer(int amount)
        {
            Health target = player != null ? player.GetComponent<Health>() : null;
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
            if (telegraph != null)
            {
                telegraph.enabled = false;
            }

            director?.ReportEnemyDeath(this);
            Destroy(gameObject, 0.06f);
        }
    }

}
