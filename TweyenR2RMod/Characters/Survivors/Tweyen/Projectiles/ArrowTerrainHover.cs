using RoR2;
using RoR2.Projectile;
using UnityEngine;
using UnityEngine.Networking;

namespace TweyenR2RMod.Survivors.Tweyen.Projectiles
{
    /// <summary>
    /// Keeps an arrow from flying into the terrain (anything it could collide with: ground, buildings, props...). It follows its launch line (the height and slope it was fired at,
    /// e.g. slightly upwards); every physics step it looks ahead along its path and pitches up just steeply enough to
    /// pass every bit of ground it sees there with <see cref="clearance"/> to spare, then gently glides back to its
    /// launch line (never below it) once the ground falls away again.
    /// - While its homing has a target in clear view, it lets the homing steer (so it can dive onto grounded enemies);
    ///   a target behind a floor or wall doesn't make it stop avoiding the ground.
    /// - Only its pitch changes: its heading is left alone.
    /// - Walls too steep to climb in time still stop it, as they should.
    /// Runs where the projectile is simulated (the server, or the owner's client for predicted projectiles).
    /// </summary>
    [RequireComponent(typeof(ProjectileController), typeof(Rigidbody))]
    public class ArrowTerrainHover : MonoBehaviour
    {
        /// <summary>Height it keeps above the ground when the ground rises up to it (at most its launch height above the
        /// ground, so arrows fired low over flat ground don't climb).</summary>
        public float clearance = 1.5f;
        public float minClearance = 0.5f;
        /// <summary>How far ahead it looks for ground, in seconds of flight.</summary>
        public float lookAheadTime = 0.2f;
        public int lookAheadSamples = 12;
        /// <summary>Ground is searched from up to this far above the arrow (less under a ceiling)...</summary>
        public float probeUp = 25f;
        /// <summary>...down to this far below it.</summary>
        public float probeDown = 30f;
        /// <summary>Distance (m) over which it glides back to its launch line after passing over something.</summary>
        public float returnDistance = 40f;
        public float maxClimbAngle = 80f;
        public float maxDiveAngle = 25f;

        /// <summary>
        /// What counts as ground: everything the arrow can collide with (the physics collision matrix for its layer:
        /// terrain, but also buildings and props, which are often on other layers), except enemies, players and other
        /// projectiles. Worked out once, from the first arrow.
        /// </summary>
        private static int groundMask = 0;

        internal static int GroundMask(int arrowLayer)
        {
            if (groundMask != 0) return groundMask;
            int[] notGround =
            {
                LayerIndex.entityPrecise.intVal, LayerIndex.enemyBody.intVal, LayerIndex.fakeActor.intVal,
                LayerIndex.noCollision.intVal, LayerIndex.pickups.intVal, LayerIndex.ragdoll.intVal,
                LayerIndex.debris.intVal, LayerIndex.projectile.intVal, LayerIndex.projectileWorldOnly.intVal,
                LayerIndex.ignoreRaycast.intVal, // (trigger colliders are skipped by the raycasts themselves)
            };
            int mask = LayerIndex.world.mask;
            for (int layer = 0; layer < 32; layer++)
            {
                if (!Physics.GetIgnoreLayerCollision(arrowLayer, layer) && System.Array.IndexOf(notGround, layer) < 0)
                {
                    mask |= 1 << layer;
                }
            }
            groundMask = mask;
            return mask;
        }

        private ProjectileController controller;
        private ProjectileSimple simple;
        private ProjectileTargetComponent targetComponent;
        private Rigidbody body;
        private Vector3 launchPosition;
        private float launchSlope;
        private float launchClearance;
        private bool launched;
        private int ground;

        private void Awake()
        {
            controller = GetComponent<ProjectileController>();
            simple = GetComponent<ProjectileSimple>();
            targetComponent = GetComponent<ProjectileTargetComponent>();
            body = GetComponent<Rigidbody>();
        }

        /// <summary>Records where and how it was fired. Done on its first physics step: Start can come after that.</summary>
        private void Launch()
        {
            launched = true;
            ground = GroundMask(gameObject.layer);
            launchPosition = body.position;
            Vector3 forward = transform.forward;
            launchSlope = forward.y / Mathf.Max(new Vector2(forward.x, forward.z).magnitude, 0.01f);
            launchClearance = Physics.Raycast(body.position, Vector3.down, out RaycastHit hit, clearance, ground, QueryTriggerInteraction.Ignore)
                ? Mathf.Max(hit.distance, minClearance)
                : clearance;
        }

        private void FixedUpdate()
        {
            if (!NetworkServer.active && !controller.isPrediction) return;
            if (!launched) Launch();
            // homing in on an enemy it can see: the homing steers (an enemy behind a floor or wall doesn't count)
            if (targetComponent && targetComponent.target &&
                !Physics.Linecast(body.position, targetComponent.target.position, ground, QueryTriggerInteraction.Ignore))
            {
                return;
            }

            Vector3 forward = transform.forward;
            Vector3 heading = new Vector3(forward.x, 0f, forward.z);
            if (heading.sqrMagnitude < 0.0001f) return;
            heading.Normalize();

            float speed = simple ? simple.desiredForwardSpeed : body.velocity.magnitude;
            Vector3 position = body.position;
            float ahead = speed * lookAheadTime;
            float step = speed * Time.fixedDeltaTime; // ground nearer than one step is reached next step anyway

            // pitch: along the launch line, gliding back to it if above it, but at least as steep as needed to pass every
            // bit of ground in view ahead with clearance to spare (the angle to it from here, at its distance)
            float travelled = new Vector2(position.x - launchPosition.x, position.z - launchPosition.z).magnitude;
            float lineHeight = launchPosition.y + travelled * launchSlope;
            float pitch = Mathf.Atan(launchSlope + Mathf.Min(lineHeight - position.y, 0f) / returnDistance);
            for (int i = 0; i <= lookAheadSamples; i++)
            {
                float distance = ahead * i / lookAheadSamples;
                Vector3 point = position + heading * distance;
                // start the probe below any ceiling, so a roof is never taken for the ground
                float up = Physics.Raycast(point, Vector3.up, out RaycastHit roof, probeUp, ground, QueryTriggerInteraction.Ignore)
                    ? Mathf.Max(roof.distance - 0.1f, 0f)
                    : probeUp;
                if (Physics.Raycast(point + Vector3.up * up, Vector3.down, out RaycastHit floor, up + probeDown, ground, QueryTriggerInteraction.Ignore))
                {
                    pitch = Mathf.Max(pitch, Mathf.Atan2(floor.point.y + launchClearance - position.y, Mathf.Max(distance, step)));
                }
            }
            pitch = Mathf.Clamp(pitch, -maxDiveAngle * Mathf.Deg2Rad, maxClimbAngle * Mathf.Deg2Rad);
            Vector3 direction = heading * Mathf.Cos(pitch) + Vector3.up * Mathf.Sin(pitch);

            Quaternion rotation = Quaternion.LookRotation(direction);
            transform.rotation = rotation;
            body.rotation = rotation;
            body.velocity = direction * speed;
        }

    }
}
