using RoR2;
using RoR2.Projectile;
using UnityEngine;

namespace TweyenR2RMod.Survivors.Tweyen.Projectiles
{
    /// <summary>
    /// The projectile flies straight for <see cref="delay"/> seconds before its homing starts, or until terrain is
    /// less than <see cref="wallLookAhead"/> m ahead (0: never), so it turns toward its target instead of flying into it.
    /// </summary>
    [RequireComponent(typeof(ProjectileSteerTowardTarget))]
    public class ProjectileHomingDelay : MonoBehaviour
    {
        public float delay = 0.1f;
        public float wallLookAhead = 0f;

        private ProjectileSteerTowardTarget steer;
        private float age;

        private void Awake()
        {
            steer = GetComponent<ProjectileSteerTowardTarget>();
            steer.enabled = false;
        }

        private void FixedUpdate()
        {
            age += Time.fixedDeltaTime;
            if (age < delay && !WallAhead()) return;
            steer.enabled = true;
            enabled = false;
        }

        private bool WallAhead()
        {
            return wallLookAhead > 0f
                && Physics.Raycast(transform.position, transform.forward, wallLookAhead, LayerIndex.world.mask, QueryTriggerInteraction.Ignore);
        }
    }
}
