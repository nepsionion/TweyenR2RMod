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
            Vector3 origin = aimRay.origin;
            Vector3 direction = aimRay.direction;
            Transform tip = FindModelChild("ArrowTip");
            if (tip)
            {
                // leave from the drawn arrow's tip, aimed at whatever the crosshair points at
                Vector3 target = Util.CharacterRaycast(gameObject, aimRay, out RaycastHit hit, MaxAimDistance, LayerIndex.CommonMasks.bullet, QueryTriggerInteraction.Ignore)
                    ? hit.point
                    : aimRay.GetPoint(MaxAimDistance);
                origin = tip.position;
                direction = (target - origin).normalized;
            }

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
