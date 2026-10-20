using EntityStates;
using RoR2;
using RoR2.Projectile;
using TweyenR2RMod.Survivors.Tweyen.Components;
using TweyenR2RMod.Survivors.Tweyen.Projectiles;
using UnityEngine;

namespace TweyenR2RMod.Survivors.Tweyen.SkillStates
{
    /// <summary>
    /// Wheel of Death (utility): Tweyen's Wheel of Death from Granblue Fantasy Relink (action 1400, motion 3440), a
    /// 720 degree spin firing arrows all around her, 10 per rotation (20 in total, 80% damage each, light homing).
    /// Unlike Relink's version she also dashes forward (20 m at her base speed, over 0.8 s,
    /// scaling with movement speed), but:
    /// - the dash is fast but not instant (decelerates over <see cref="dashDuration"/>), horizontal only,
    ///   and gravity still applies;
    /// - no invincibility;
    /// - no fall damage during the skill and for <see cref="fallDamageImmunityAfter"/> s after.
    /// Each arrow leaves in the direction she faces at that moment of the animation (exported spin events), slightly upwards.
    /// </summary>
    public class WheelOfDeath : BaseSkillState, IBlocksPrimary
    {
        public static float duration = 1.0f; // the edited spin animation is 1.02 s
        public static float damageCoefficient = 0.8f;
        /// <summary>20 m at Tweyen's base speed (7 x 1.15); was the Huntress's Blink distance, 29.4 m.</summary>
        public static float distancePerMoveSpeed = 20f / (7f * 1.15f);
        public static float dashDuration = 0.8f;
        public static float fallDamageImmunityAfter = 0.3f;
        /// <summary>
        /// Clip time (s) at which she starts to stand up after the spin. From then on, sprinting (moving with sprint on)
        /// ends the skill straight into the sprint, firing any arrows still to come at once.
        /// </summary>
        public static float standUpClipTime = 0.75f;
        /// <summary>Arrows spawn this far out from her centre, so they start outside her body.</summary>
        public static float arrowSpawnRadius = 0.6f;
        /// <summary>
        /// Arrows start at least this high above the floor under their spawn point. Her core alone isn't enough: on some
        /// buildings (e.g. in Shattered Abodes) the floor around her is only 0.25 m below it, and the arrow's collision
        /// sphere would touch it the moment it is fired.
        /// </summary>
        public static float arrowMinSpawnHeight = 1f;
        /// <summary>Arrows leave this many degrees above the horizontal, so uneven ground doesn't catch them.</summary>
        public static float arrowLaunchPitch = 6f;

        private const string Layer = "FullBody, Override";
        private const string State = "WheelOfDeath";
        private const string PlaybackRate = "WheelOfDeath.playbackRate";

        private TweyenAnimEvents.AnimEvent[] events = new TweyenAnimEvents.AnimEvent[0];
        private float clipLength = 1f;
        private int nextEvent;
        private Vector3 dashDirection;
        private float dashStartSpeed;
        private TweyenFallDamageShield fallShield;
        private float lastShotSound = float.NegativeInfinity;

        public bool BlocksPrimary => true;

        public override void OnEnter()
        {
            base.OnEnter();

            TweyenAnimEvents.ClipEvents clip = TweyenAnimEvents.Get("WheelOfDeath");
            if (clip != null)
            {
                events = clip.events;
                clipLength = clip.length;
            }

            // dash where she is aiming, flattened to the ground plane
            Vector3 aim = inputBank ? inputBank.aimDirection : transform.forward;
            dashDirection = new Vector3(aim.x, 0f, aim.z);
            if (dashDirection.sqrMagnitude < 0.0001f) dashDirection = new Vector3(transform.forward.x, 0f, transform.forward.z);
            dashDirection.Normalize();
            if (characterDirection) characterDirection.forward = dashDirection;

            // linear deceleration: distance = startSpeed * dashDuration / 2
            float distance = moveSpeedStat * distancePerMoveSpeed;
            dashStartSpeed = 2f * distance / dashDuration;

            // a Stellar Arrow still being drawn would fight the spin for the full-body animation: cancel it
            if (isAuthority)
            {
                EntityStateMachine weapon = EntityStateMachine.FindByCustomName(gameObject, "Weapon");
                if (weapon && weapon.state is StellarArrow) weapon.SetNextStateToMain();
            }

            fallShield = GetComponent<TweyenFallDamageShield>();
            if (fallShield) fallShield.Begin();

            Animator animator = GetModelAnimator();
            if (animator)
            {
                int layer = animator.GetLayerIndex(Layer);
                if (layer >= 0)
                {
                    animator.SetFloat(PlaybackRate, clipLength / duration);
                    animator.CrossFadeInFixedTime(State, 0.1f, layer, 0f);
                }
            }
            Util.PlaySound(TweyenSounds.Draw, gameObject);
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();

            if (isAuthority && TryJumpCancel())
            {
                return;
            }

            if (isAuthority && characterMotor && fixedAge < dashDuration)
            {
                // horizontal displacement only: gravity keeps acting on the motor's velocity. Being displacement,
                // the dash leaves no momentum behind when another skill interrupts it.
                characterMotor.rootMotion += dashDirection * (CurrentDashSpeed() * GetDeltaTime());
            }
            if (characterDirection) characterDirection.forward = dashDirection;

            // fire on the animation's spin events (event times are in clip seconds)
            float clipTime = fixedAge * clipLength / duration;
            while (nextEvent < events.Length && clipTime >= events[nextEvent].time)
            {
                FireArrow(events[nextEvent].yaw);
                nextEvent++;
            }

            if (isAuthority && clipTime >= standUpClipTime && WantsToSprint())
            {
                // she is getting up anyway: go straight into the sprint instead of finishing the stand-up
                while (nextEvent < events.Length)
                {
                    FireArrow(events[nextEvent].yaw);
                    nextEvent++;
                }
                if (characterBody) characterBody.isSprinting = true;
                outer.SetNextStateToMain();
                return;
            }

            if (isAuthority && fixedAge >= duration)
            {
                outer.SetNextStateToMain();
            }
        }

        private bool WantsToSprint()
        {
            return inputBank && inputBank.sprint.down && inputBank.moveVector.sqrMagnitude > 0.01f;
        }

        private float CurrentDashSpeed()
        {
            return fixedAge < dashDuration ? dashStartSpeed * (1f - fixedAge / dashDuration) : 0f;
        }

        /// <summary>
        /// Jumping cancels the spin and keeps the dash's momentum: she jumps with her normal jump power, carrying the
        /// dash's current speed and direction horizontally. (The game's own jump would replace her horizontal speed
        /// with input direction x move speed, so the jump is done here.)
        /// </summary>
        private bool TryJumpCancel()
        {
            if (!inputBank || !characterMotor || !characterBody) return false;
            if (!inputBank.jump.justPressed || inputBank.jump.hasPressBeenClaimed) return false;
            if (characterMotor.jumpCount >= characterBody.maxJumpCount) return false;

            inputBank.jump.hasPressBeenClaimed = true;
            Vector3 velocity = dashDirection * CurrentDashSpeed();
            velocity.y = characterBody.jumpPower * characterMotor.jumpPowerPenaltyCoefficient;
            characterMotor.velocity = velocity;
            characterMotor.Motor.ForceUnground();
            characterMotor.jumpCount++;
            characterBody.TriggerJumpEventGlobally();
            if (sfxLocator && !string.IsNullOrEmpty(sfxLocator.jumpSound)) Util.PlaySound(sfxLocator.jumpSound, gameObject);
            PlayCrossfade("Body", "Jump", 0.05f);

            outer.SetNextStateToMain();
            return true;
        }

        private void FireArrow(float yaw)
        {
            TweyenSounds.PlayShot(TweyenSounds.Shot, gameObject, fixedAge, ref lastShotSound);
            if (!isAuthority) return;
            // yaw is relative to the model's forward, which is held on the dash direction
            Vector3 flat = Quaternion.AngleAxis(yaw, Vector3.up) * dashDirection;
            float pitch = arrowLaunchPitch * Mathf.Deg2Rad;
            Vector3 direction = flat * Mathf.Cos(pitch) + Vector3.up * Mathf.Sin(pitch);
            Vector3 origin = characterBody.corePosition + flat * arrowSpawnRadius;
            Vector3 probe = origin + Vector3.up * 0.5f; // low enough not to land on top of a low ceiling
            int ground = ArrowTerrainHover.GroundMask(TweyenArrows.wheelArrow.layer);
            if (Physics.Raycast(probe, Vector3.down, out RaycastHit floor, 3f, ground, QueryTriggerInteraction.Ignore))
            {
                origin.y = Mathf.Max(origin.y, floor.point.y + arrowMinSpawnHeight);
            }
            ProjectileManager.instance.FireProjectile(new FireProjectileInfo
            {
                projectilePrefab = TweyenArrows.wheelArrow,
                position = origin,
                rotation = Util.QuaternionSafeLookRotation(direction),
                owner = gameObject,
                damage = damageCoefficient * damageStat,
                crit = RollCrit(),
                force = 0f,
            });
        }

        public override void OnExit()
        {
            if (fallShield) fallShield.End(fallDamageImmunityAfter);
            // she comes out of it running (whether or not she was before)
            if (isAuthority && characterBody) characterBody.isSprinting = true;

            // cancelled early (jump or another skill): let the spin go instead of finishing it over the next action
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

        /// <summary>
        /// Other skills can cancel it (any Body-state skill with interrupt priority Skill or higher; they should call
        /// <see cref="TweyenMovement.CancelMomentum"/>). The primary can't: it runs on the Weapon machine and is
        /// blocked during the spin (<see cref="IBlocksPrimary"/>).
        /// </summary>
        public override InterruptPriority GetMinimumInterruptPriority()
        {
            return InterruptPriority.Skill;
        }
    }
}
