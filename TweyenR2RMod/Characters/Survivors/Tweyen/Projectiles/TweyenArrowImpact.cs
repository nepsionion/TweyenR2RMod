using RoR2;
using RoR2.Projectile;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace TweyenR2RMod.Survivors.Tweyen.Projectiles
{
    /// <summary>
    /// What happens when one of Tweyen's arrows touches something. Every skill fires the same arrow
    /// (see <see cref="TweyenArrows"/>) and differs only in a subclass of this.
    ///
    /// Behaviour (mirrors the game's ProjectileSingleTargetImpact, which the Railgunner's smart rounds use):
    /// - enemy hurtbox: deals the projectile's damage to that one enemy (procs and on-hit items work normally),
    ///   then <see cref="OnEnemyHitServer"/> for the skill's extras (debuffs...), then the impact effect;
    /// - world geometry: impact effect only, no damage, and the arrow is gone.
    /// Runs its damage logic on the server only, like the vanilla component.
    /// </summary>
    [RequireComponent(typeof(ProjectileController), typeof(ProjectileDamage))]
    public abstract class TweyenArrowImpact : MonoBehaviour, IProjectileImpactBehavior
    {
        public GameObject impactEffect;
        public bool destroyOnWorld = true;

        protected ProjectileController projectileController;
        protected ProjectileDamage projectileDamage;
        private HealthComponent target;
        private bool alive = true;

        private static GameObject hitBoxObject;
        private static HitBoxGroup hitBoxGroup;

        protected virtual void Awake()
        {
            projectileController = GetComponent<ProjectileController>();
            projectileDamage = GetComponent<ProjectileDamage>();
        }

        /// <summary>Change the attack before it is fired (damage type, damage colour...). Server only.</summary>
        protected virtual void ModifyAttack(OverlapAttack attack) { }

        /// <summary>Called after the arrow damaged <paramref name="victim"/>: apply debuffs etc. here. Server only.</summary>
        protected virtual void OnEnemyHitServer(HealthComponent victim, Vector3 hitPoint) { }

        public void OnProjectileImpact(ProjectileImpactInfo impactInfo)
        {
            if (!alive || !impactInfo.collider || impactInfo.collider.gameObject == projectileController.owner)
            {
                return;
            }

            HurtBox hurtBox = impactInfo.collider.GetComponent<HurtBox>();
            if (hurtBox)
            {
                HealthComponent healthComponent = hurtBox.healthComponent;
                if (!healthComponent || healthComponent.gameObject == projectileController.owner)
                {
                    return;
                }
                target = healthComponent;
                if (NetworkServer.active)
                {
                    HitTarget(impactInfo.estimatedPointOfImpact);
                }
                alive = false;
            }
            else if (destroyOnWorld)
            {
                alive = false;
            }

            if (!alive)
            {
                if (NetworkServer.active && impactEffect)
                {
                    EffectManager.SimpleImpactEffect(impactEffect, impactInfo.estimatedPointOfImpact, -transform.forward, !projectileController.isPrediction);
                }
                Destroy(gameObject);
            }
        }

        private void HitTarget(Vector3 point)
        {
            EnsureHitBox();
            hitBoxObject.transform.position = point;
            OverlapAttack attack = new OverlapAttack
            {
                attacker = projectileController.owner,
                inflictor = gameObject,
                damage = projectileDamage.damage,
                damageColorIndex = projectileDamage.damageColorIndex,
                damageType = projectileDamage.damageType,
                forceVector = projectileDamage.force * transform.forward,
                hitBoxGroup = hitBoxGroup,
                isCrit = projectileDamage.crit,
                procChainMask = projectileController.procChainMask,
                procCoefficient = projectileController.procCoefficient,
                teamIndex = projectileController.teamFilter.teamIndex,
                attackOriginPreference = AttackOriginPreference.Attacker,
                attackerFiltering = AttackerFiltering.CustomFilter,
                filterCallback = s => (object)s.healthComponent == target, // only the enemy the arrow touched
                modifyOutgoingOverlapInfoCallback = hits =>
                {
                    for (int i = 0; i < hits.Count; i++)
                    {
                        OverlapAttack.OverlapInfo info = hits[i];
                        info.hitPosition = point;
                        hits[i] = info;
                    }
                },
            };
            ModifyAttack(attack);
            if (attack.Fire())
            {
                OnEnemyHitServer(target, point);
            }
        }

        private static void EnsureHitBox()
        {
            if (hitBoxObject && hitBoxGroup) return;
            if (hitBoxObject) Destroy(hitBoxObject);
            hitBoxObject = new GameObject("TweyenArrowImpactHitBox", typeof(HitBox), typeof(HitBoxGroup));
            hitBoxGroup = hitBoxObject.GetComponent<HitBoxGroup>();
            hitBoxGroup.hitBoxes = new HitBox[] { hitBoxObject.GetComponent<HitBox>() };
        }
    }

    /// <summary>The default arrow: damage only, no extra effects.</summary>
    public class BasicArrowImpact : TweyenArrowImpact { }

    /// <summary>Wheel of Death's arrows: damage only for now (their low homing is set where the projectile is created).</summary>
    public class WheelOfDeathArrowImpact : TweyenArrowImpact { }

    /// <summary>
    /// Two-Crown's Strife's arrows: each hit makes the enemy's debuffs (damage over time included) last
    /// <see cref="DEBUFF_EXTENSION"/> s longer and adds one stack to one of its stackable debuffs, picked at random.
    /// </summary>
    public class TwoCrownArrowImpact : TweyenArrowImpact
    {
        public const float DEBUFF_EXTENSION = 10f;

        protected override void OnEnemyHitServer(HealthComponent victim, Vector3 hitPoint)
        {
            if (victim) TweyenDebuffs.ExtendAndAddStack(victim.body, projectileController.owner, DEBUFF_EXTENSION);
        }
    }
}

namespace TweyenR2RMod.Survivors.Tweyen.Projectiles
{
    /// <summary>
    /// Multilock Hail's arrows: damage, and a count of how many arrows of one cast hit each enemy (for "Tweyen:
    /// Sagacity"). A cast's arrows all leave within a fraction of a second, so arrows of one player spawned less than
    /// <see cref="CAST_GAP"/> s apart belong to the same cast. Server only.
    /// </summary>
    public class MultilockArrowImpact : TweyenArrowImpact
    {
        public const float CAST_GAP = 0.6f;

        /// <summary>An enemy was hit by this many arrows of one cast (raised on every hit). Server only.</summary>
        public static event System.Action<GameObject, int> onCastHits;

        private class Cast
        {
            public int id;
            public float lastSpawn;
            public readonly Dictionary<HealthComponent, int> hits = new Dictionary<HealthComponent, int>();
        }

        private static readonly Dictionary<GameObject, Cast> casts = new Dictionary<GameObject, Cast>();
        private static int nextCast;
        private int cast = -1;

        private void Start()
        {
            GameObject owner = projectileController ? projectileController.owner : null;
            if (!NetworkServer.active || !owner) return;
            if (!casts.TryGetValue(owner, out Cast c) || Time.time - c.lastSpawn > CAST_GAP)
            {
                c = new Cast { id = ++nextCast };
                casts[owner] = c;
            }
            c.lastSpawn = Time.time;
            cast = c.id;
        }

        protected override void OnEnemyHitServer(HealthComponent victim, Vector3 hitPoint)
        {
            GameObject owner = projectileController.owner;
            if (!victim || !owner || !casts.TryGetValue(owner, out Cast c) || c.id != cast) return;
            c.hits.TryGetValue(victim, out int n);
            c.hits[victim] = ++n;
            onCastHits?.Invoke(owner, n);
        }
    }
}
