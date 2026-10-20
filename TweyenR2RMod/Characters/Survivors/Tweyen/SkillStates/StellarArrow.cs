using EntityStates;
using RoR2;
using RoR2.Projectile;
using TweyenR2RMod.Survivors.Tweyen.Projectiles;
using UnityEngine;

namespace TweyenR2RMod.Survivors.Tweyen.SkillStates
{
    /// <summary>
    /// Stellar Arrow (primary): Tweyen's first light attack from Granblue Fantasy Relink (Attack 1, motion 3600:
    /// draw and shoot, then the 3630 follow-through), firing one basic arrow.
    /// 200% damage (proc coefficient 1), one arrow per second at base attack speed: 200% DPS, below the
    /// Huntress's Strafe (2 x 150% = 300%) to pay for the homing and longer range.
    /// </summary>
    public class StellarArrow : BaseSkillState
    {
        public static float damageCoefficient = 2f;
        public static float baseDuration = 1f; // one arrow per second at base attack speed
        /// <summary>The arrow leaves at this fraction of the shot's duration (end of the draw animation).</summary>
        public static float fireFraction = 0.35f;
        /// <summary>
        /// Where the arrow leaves, from her aim origin along the aim (m): ahead, up and to her left. A fixed point
        /// rather than the drawn arrow's tip, which isn't where it should be at high attack speed (the sped-up draw
        /// is already past its end, lowering the bow, when the shot fires). Placed where the tip is at base attack
        /// speed aiming level (measured in game: 1.85 m ahead, 0.05 m to her right).
        /// </summary>
        public static float muzzleForward = 1.85f;
        public static float muzzleUp = 0f;
        public static float muzzleLeft = -0.05f;

        private const string Layer = "FullBody, Override";
        private const string ShootState = "StellarArrow";
        private const string HoldState = "StellarArrowHold";
        private const string PlaybackRate = "StellarArrow.playbackRate";
        /// <summary>Length of the GBFR draw-and-shoot motion (3600), and the point where the arrow is already nocked.</summary>
        private const float DrawClipLength = 25f / 60f;
        private const float NockedTime = 0.12f;
        private const float MaxAimDistance = 1000f;

        private float duration;
        private float fireTime;
        private bool hasFired;

        public override void OnEnter()
        {
            base.OnEnter();
            duration = baseDuration / attackSpeedStat;
            fireTime = duration * fireFraction;
            StartAimMode(GetAimRay(), 2f);
            PlayShootAnimation();
            Util.PlaySound(TweyenSounds.Draw, gameObject);
        }

        /// <summary>
        /// Plays the draw so the arrow leaves at fireTime. If she is still aiming from the previous shot, the part
        /// where she pulls the bow off her back is skipped.
        /// </summary>
        private void PlayShootAnimation()
        {
            Animator animator = GetModelAnimator();
            if (!animator) return;
            int layer = animator.GetLayerIndex(Layer);
            if (layer < 0) return;

            AnimatorStateInfo current = animator.GetCurrentAnimatorStateInfo(layer);
            bool alreadyAiming = current.IsName(ShootState) || current.IsName(HoldState);
            float start = alreadyAiming ? NockedTime : 0f;

            animator.SetFloat(PlaybackRate, (DrawClipLength - start) / fireTime);
            animator.CrossFadeInFixedTime(ShootState, alreadyAiming ? 0.05f : 0.1f, layer, start);
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            // firing stopped her sprint (cancelSprintingOnActivation); she doesn't start running again mid-shot
            if (isAuthority && characterBody.isSprinting) characterBody.isSprinting = false;
            if (!hasFired && fixedAge >= fireTime)
            {
                Fire();
            }
            if (isAuthority && fixedAge >= duration)
            {
                outer.SetNextStateToMain();
            }
        }

        private void Fire()
        {
            hasFired = true;
            Util.PlaySound(TweyenSounds.Shot, gameObject);
            if (!isAuthority) return;

            Ray aimRay = GetAimRay();
            // leave from a fixed point by the bow, aimed at whatever the crosshair points at
            Vector3 forward = aimRay.direction;
            Vector3 left = Vector3.Cross(forward, Vector3.up);
            if (left.sqrMagnitude < 0.0001f) left = -transform.right;
            left.Normalize();
            Vector3 up = Vector3.Cross(left, forward);
            Vector3 origin = aimRay.origin + forward * muzzleForward + up * muzzleUp + left * muzzleLeft;
            Vector3 target = Util.CharacterRaycast(gameObject, aimRay, out RaycastHit hit, MaxAimDistance, LayerIndex.CommonMasks.bullet, QueryTriggerInteraction.Ignore)
                ? hit.point
                : aimRay.GetPoint(MaxAimDistance);
            Vector3 toTarget = target - origin;
            // a target closer than the launch point (point blank): straight along the aim instead of backwards
            Vector3 direction = Vector3.Dot(toTarget, forward) > 0.1f ? toTarget.normalized : forward;

            ProjectileManager.instance.FireProjectile(new FireProjectileInfo
            {
                projectilePrefab = TweyenArrows.basicArrow,
                position = origin,
                rotation = Util.QuaternionSafeLookRotation(direction),
                owner = gameObject,
                damage = damageCoefficient * damageStat,
                crit = RollCrit(),
                force = 0f,
            });
        }

        public override InterruptPriority GetMinimumInterruptPriority()
        {
            return hasFired ? InterruptPriority.Any : InterruptPriority.Skill;
        }
    }
}
