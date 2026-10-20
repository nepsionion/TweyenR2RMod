using RoR2;
using RoR2.Projectile;
using UnityEngine;

namespace TweyenR2RMod.Survivors.Tweyen.Projectiles
{
    /// <summary>
    /// Lets go of the projectile's target once that enemy is dead (its body can linger a moment), so a homing arrow
    /// goes on in a straight line instead of diving at the corpse. Without a target finder, it never takes another.
    /// </summary>
    [RequireComponent(typeof(ProjectileTargetComponent))]
    public class ProjectileDropDeadTarget : MonoBehaviour
    {
        private ProjectileTargetComponent targetComponent;
        private HealthComponent targetHealth;
        private Transform lastTarget;

        private void Awake()
        {
            targetComponent = GetComponent<ProjectileTargetComponent>();
        }

        private void FixedUpdate()
        {
            Transform target = targetComponent.target;
            if (!target) return;
            if (target != lastTarget)
            {
                lastTarget = target;
                HurtBox hurtBox = target.GetComponent<HurtBox>();
                targetHealth = hurtBox ? hurtBox.healthComponent : target.GetComponent<HealthComponent>();
            }
            if (targetHealth && !targetHealth.alive)
            {
                targetComponent.target = null;
                lastTarget = null;
                targetHealth = null;
            }
        }
    }
}
