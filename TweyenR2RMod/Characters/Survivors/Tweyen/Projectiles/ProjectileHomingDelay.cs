using RoR2.Projectile;
using UnityEngine;

namespace TweyenR2RMod.Survivors.Tweyen.Projectiles
{
    /// <summary>The projectile flies straight for <see cref="delay"/> seconds before its homing starts.</summary>
    [RequireComponent(typeof(ProjectileSteerTowardTarget))]
    public class ProjectileHomingDelay : MonoBehaviour
    {
        public float delay = 0.1f;

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
            if (age < delay) return;
            steer.enabled = true;
            enabled = false;
        }
    }
}
