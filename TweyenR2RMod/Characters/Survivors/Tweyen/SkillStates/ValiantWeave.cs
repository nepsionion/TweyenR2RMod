using EntityStates;
using RoR2;
using RoR2.Projectile;
using TweyenR2RMod.Survivors.Tweyen.Projectiles;
using UnityEngine;

namespace TweyenR2RMod.Survivors.Tweyen.SkillStates
{
    /// <summary>
    /// Valiant Weave (utility): Relink's forward dash shot (the strong attack after a light one, motion 310b, its dash
    /// only, sped up to <see cref="animationDuration"/>). She dashes where she aims (flattened) at a steady speed
    /// for <see cref="dashDuration"/>, covering the ground distance of the Commando's Tactical Slide (scaled by her
    /// movement speed); in the air it is an air-dash: no hop, no gravity while she dashes, the same distance. She
    /// fires one arrow straight along the dash: 100% damage, no homing, flat, stopped by the first enemy (and by terrain).
    /// - Ending in the air, she keeps the dash's speed (<see cref="CarryMomentum"/>): she goes on forward as she falls.
    /// - Until she stops, only skills other than the primary can cut it short (and the primary can't be used), or a
    ///   jump, which keeps the dash's speed the same way (like jumping out of the Commando's Tactical Slide).
    /// - She is sprinting when it ends, as after a Tactical Slide.
    /// </summary>
    public class ValiantWeave : BaseSkillState, IBlocksPrimary
    {
        public static float damageCoefficient = 1f;
        /// <summary>The (sped-up) animation; never scaled by stats.</summary>
        public static float animationDuration = 0.4f;
        /// <summary>The arrow leaves at this point of the animation (Relink's 0.3 s, sped up 2.5x).</summary>
        public static float fireTime = 0.12f;

        /// <summary>The dash's length (s), as the Tactical Slide's.</summary>
        public static float dashDuration = 1f;
        /// <summary>
        /// Distance (m per point of movement speed): the Tactical Slide's on the ground (the integral of its speed
        /// curve, 6 -> 0 over 1 s with tangents -7.99 and -0.59: 3 + (-7.99 + 0.59) / 12).
        /// </summary>
        public static float dashDistance = 2.38f;

        private const string Layer = "FullBody, Override";
        private const string State = "ValiantWeave";
        private const string Muzzle = "ArrowTip";

        private Vector3 dashDirection;
        private bool antiGravity;
        private bool hasFired;

        public bool BlocksPrimary => true;

        public override void OnEnter()
        {
            base.OnEnter();
            Vector3 aim = inputBank ? inputBank.aimDirection : transform.forward;
            dashDirection = new Vector3(aim.x, 0f, aim.z);
            if (dashDirection.sqrMagnitude < 0.0001f) dashDirection = new Vector3(transform.forward.x, 0f, transform.forward.z);
            dashDirection.Normalize();
            if (characterDirection) characterDirection.forward = dashDirection;

            if (isAuthority)
            {
                EntityStateMachine weapon = EntityStateMachine.FindByCustomName(gameObject, "Weapon");
                if (weapon && weapon.state is StellarArrow) weapon.SetNextStateToMain();
            }
            if (characterMotor && !characterMotor.isGrounded)
            {
                // air-dash: no gravity while she dashes (and no rising or falling from before)
                CharacterGravityParameters gravity = characterMotor.gravityParameters;
                gravity.channeledAntiGravityGranterCount++;
                characterMotor.gravityParameters = gravity;
                antiGravity = true;
                if (isAuthority) characterMotor.velocity = new Vector3(characterMotor.velocity.x, 0f, characterMotor.velocity.z);
            }
            PlayCrossfade(Layer, State, 0.05f);
            Util.PlaySound(TweyenSounds.Draw, gameObject);
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            if (isAuthority && TryJumpCancel())
            {
                if (!hasFired) Fire();
                return;
            }
            if (isAuthority && characterMotor)
            {
                characterMotor.rootMotion += CurrentSpeed() * dashDirection * GetDeltaTime();
            }
            if (characterDirection) characterDirection.forward = dashDirection;

            if (!hasFired && fixedAge >= fireTime) Fire();
            if (isAuthority && fixedAge >= dashDuration)
            {
                if (characterMotor && !characterMotor.isGrounded) CarryMomentum(0f);
                outer.SetNextStateToMain();
            }
        }

        private float CurrentSpeed()
        {
            return dashDistance / dashDuration * moveSpeedStat;
        }

        /// <summary>
        /// Authority: the dash's speed becomes her velocity (it moves her by displacement, which stops with it), with
        /// <paramref name="verticalSpeed"/> upwards. Air control would bleed it off within a second (towards her input,
        /// or to a stop with none), so it is off until she lands or hits something, as after Acrid's Leap; a
        /// double jump still redirects her.
        /// </summary>
        private void CarryMomentum(float verticalSpeed)
        {
            Vector3 velocity = dashDirection * CurrentSpeed();
            velocity.y = verticalSpeed;
            characterMotor.velocity = velocity;
            characterMotor.disableAirControlUntilCollision = true;
        }

        /// <summary>
        /// A jump ends the dash and keeps its momentum: a normal jump carrying the dash's speed and direction.
        /// (The game's own jump would replace her horizontal speed with her input, so the jump is done here.)
        /// </summary>
        private bool TryJumpCancel()
        {
            if (!inputBank || !characterMotor || !characterBody) return false;
            if (!inputBank.jump.justPressed || inputBank.jump.hasPressBeenClaimed) return false;
            if (characterMotor.jumpCount >= characterBody.maxJumpCount) return false;

            inputBank.jump.hasPressBeenClaimed = true;
            characterMotor.Motor.ForceUnground();
            CarryMomentum(characterBody.jumpPower * characterMotor.jumpPowerPenaltyCoefficient);
            characterMotor.jumpCount++;
            characterBody.TriggerJumpEventGlobally();
            if (sfxLocator && !string.IsNullOrEmpty(sfxLocator.jumpSound)) Util.PlaySound(sfxLocator.jumpSound, gameObject);
            PlayCrossfade("Body", "Jump", 0.05f);
            outer.SetNextStateToMain();
            return true;
        }

        private void Fire()
        {
            hasFired = true;
            Util.PlaySound(TweyenSounds.Shot, gameObject);
            if (!isAuthority) return;
            Transform tip = FindModelChild(Muzzle);
            Vector3 origin = tip ? tip.position : characterBody.corePosition;
            ProjectileManager.instance.FireProjectile(new FireProjectileInfo
            {
                projectilePrefab = TweyenArrows.valiantArrow,
                position = origin,
                rotation = Util.QuaternionSafeLookRotation(dashDirection),
                owner = gameObject,
                damage = damageCoefficient * damageStat,
                crit = RollCrit(),
                force = 0f,
            });
        }

        public override void OnExit()
        {
            if (antiGravity && characterMotor)
            {
                CharacterGravityParameters gravity = characterMotor.gravityParameters;
                gravity.channeledAntiGravityGranterCount--;
                characterMotor.gravityParameters = gravity;
                antiGravity = false;
            }
            // like after a Tactical Slide: running
            if (isAuthority && characterBody) characterBody.isSprinting = true;
            Animator animator = GetModelAnimator();
            if (animator)
            {
                int layer = animator.GetLayerIndex(Layer);
                if (layer >= 0 && (animator.GetCurrentAnimatorStateInfo(layer).IsName(State) || animator.GetNextAnimatorStateInfo(layer).IsName(State)))
                {
                    animator.CrossFadeInFixedTime("BufferEmpty", 0.15f, layer);
                }
            }
            base.OnExit();
        }

        /// <summary>Until she stops: only other skills (not the primary, which runs elsewhere and is blocked).</summary>
        public override InterruptPriority GetMinimumInterruptPriority()
        {
            return InterruptPriority.Skill;
        }
    }
}
