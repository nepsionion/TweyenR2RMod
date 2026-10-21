using RoR2.Projectile;
using UnityEngine;
using UnityEngine.Networking;

namespace TweyenR2RMod.Survivors.Tweyen.Projectiles
{
    /// <summary>
    /// A projectile fired at a target homes in on that one: its own target finders stay off while it lives, and take
    /// over if it dies. Fired without one, the finders work as usual. Server only (where the finders run).
    /// </summary>
    [RequireComponent(typeof(ProjectileTargetComponent))]
    public class ProjectileKeepFiredTarget : MonoBehaviour
    {
        private ProjectileTargetComponent targetComponent;
        private Behaviour[] finders;

        // the target from FireProjectileInfo is set right after the projectile is made, before Start
        private void Start()
        {
            targetComponent = GetComponent<ProjectileTargetComponent>();
            if (!NetworkServer.active || !targetComponent.target)
            {
                enabled = false;
                return;
            }
            var list = new System.Collections.Generic.List<Behaviour>();
            list.AddRange(GetComponents<ProjectileDirectionalTargetFinder>());
            list.AddRange(GetComponents<ProjectileSphereTargetFinder>());
            finders = list.ToArray();
            SetFinders(false);
        }

        private void FixedUpdate()
        {
            if (targetComponent.target) return;
            SetFinders(true);
            enabled = false;
        }

        private void SetFinders(bool on)
        {
            foreach (Behaviour finder in finders) if (finder) finder.enabled = on;
        }
    }
}
