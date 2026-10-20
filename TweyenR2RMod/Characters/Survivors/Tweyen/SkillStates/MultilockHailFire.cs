using EntityStates;
using RoR2;
using RoR2.Projectile;
using System.Collections.Generic;
using UnityEngine;

namespace TweyenR2RMod.Survivors.Tweyen.SkillStates
{
    /// <summary>
    /// Multilock Hail (the primary while aiming Asteroid Jaeger): Relink's Multilock Hail shot. From the release
    /// (<see cref="fireTime"/>) one homing arrow per mark (an enemy marked twice gets two) leaves the bow, one after
    /// the other over <see cref="volleyDuration"/>. Each is fired slightly upwards at a random angle in front of her and
    /// homes in on its enemy after a moment: 30% damage each (proc coefficient 1), +10% of that per Ultrasight stack;
    /// with 10+ stacks every arrow crits and the stacks are spent once the last is out (Asteroid Jaeger never grants
    /// any). The secondary's charge is spent with the first arrow.
    /// Recoil grows with the arrows: none for the first, then 10% of Clincher's per arrow.
    /// Can't be cancelled until every arrow is out; then anything ends it.
    /// </summary>
    public class MultilockHailFire : BaseSkillState, IBlocksPrimary
    {
        public static float fireTime = 0.2f;
        public static float duration = 1.5f;
        /// <summary>The arrows leave one after the other over this long (s), however many there are.</summary>
        public static float volleyDuration = 0.3f;
        /// <summary>Fraction of Clincher's recoil each arrow after the first adds.</summary>
        public static float recoilPerArrow = 0.1f;
        /// <summary>Launch angles: up from the horizontal, and to either side of where she faces (90 = her side).</summary>
        public static float minLaunchPitch = 10f;
        public static float maxLaunchPitch = 40f;
        public static float maxLaunchYaw = 40f;

        private const string Layer = "FullBody, Override";
        private const string Muzzle = "ArrowTip";

        private readonly List<HurtBox> targets = new List<HurtBox>();
        private int arrowsFired;
        private bool hasFired;
        private bool ultrasightReady;
        private int ultrasightStacks;
        private float damage;
        private float lastShotSound = float.NegativeInfinity;

        public bool BlocksPrimary => !hasFired;

        public override void OnEnter()
        {
            base.OnEnter();
            AsteroidJaegerMarks marks = GetComponent<AsteroidJaegerMarks>();
            if (marks)
            {
                targets.AddRange(marks.marks);
                marks.Clear();
            }
            ultrasightReady = TweyenBuffs.UltrasightReady(characterBody);
            ultrasightStacks = characterBody && TweyenBuffs.ultrasight ? Mathf.Min(characterBody.GetBuffCount(TweyenBuffs.ultrasight), TweyenBuffs.ULTRASIGHT_MAX_STACKS) : 0;
            damage = TweyenAsteroidJaeger.DAMAGE_COEFFICIENT * damageStat * (1f + TweyenAsteroidJaeger.DAMAGE_PER_ULTRASIGHT * ultrasightStacks);
            if (isAuthority && characterMotor)
            {
                characterMotor.moveDirection = Vector3.zero;
                characterMotor.velocity = new Vector3(0f, characterMotor.velocity.y, 0f);
            }
            StartAimMode(GetAimRay(), duration);
            PlayCrossfade(Layer, "MultilockFire", 0.05f);
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            if (!hasFired)
            {
                if (isAuthority && characterMotor)
                {
                    characterMotor.moveDirection = Vector3.zero;
                    characterMotor.velocity = new Vector3(0f, characterMotor.velocity.y, 0f);
                }
                // every arrow due by now (the i-th leaves at fireTime + i * the interval)
                int count = Mathf.Max(1, targets.Count);
                float interval = count > 1 ? volleyDuration / (count - 1) : 0f;
                while (!hasFired && fixedAge >= fireTime + arrowsFired * interval) FireArrow(count);
                return;
            }
            if (isAuthority && (TryJumpCancel() || ShouldCancel() || fixedAge >= duration))
            {
                outer.SetNextStateToMain();
            }
        }

        private void FireArrow(int count)
        {
            int index = arrowsFired++;
            TweyenSounds.PlayShot(TweyenSounds.Shot, gameObject, fixedAge, ref lastShotSound);
            Ray aimRay = GetAimRay();
            if (index > 0)
            {
                AddRecoil(-1f * Clincher.recoilAmplitudeY * recoilPerArrow, -1.5f * Clincher.recoilAmplitudeY * recoilPerArrow,
                    -1f * Clincher.recoilAmplitudeX * recoilPerArrow, 1f * Clincher.recoilAmplitudeX * recoilPerArrow);
            }

            if (isAuthority)
            {
                if (index == 0 && skillLocator && skillLocator.secondary)
                {
                    // the secondary's charge (all of it: extra charges were arrows) is spent by the shot
                    skillLocator.secondary.stock = 0;
                    skillLocator.secondary.rechargeStopwatch = 0f;
                }
                HurtBox target = index < targets.Count ? targets[index] : null;
                Transform tip = FindModelChild(Muzzle);
                Vector3 origin = tip ? tip.position : aimRay.origin;
                Vector3 direction = LaunchDirection(aimRay.direction);
                ProjectileManager.instance.FireProjectile(new FireProjectileInfo
                {
                    projectilePrefab = TweyenAsteroidJaeger.multilockArrow,
                    position = origin,
                    // random roll too: every angle of the arrow is random
                    rotation = Quaternion.AngleAxis(Random.Range(0f, 360f), direction) * Util.QuaternionSafeLookRotation(direction),
                    owner = gameObject,
                    target = target ? target.gameObject : null,
                    damage = damage,
                    crit = ultrasightReady || RollCrit(),
                    force = 0f,
                });
                if (index > 0 && characterMotor) characterMotor.ApplyForce(-Clincher.selfKnockbackForce * recoilPerArrow * aimRay.direction);
            }

            if (arrowsFired >= count)
            {
                hasFired = true;
                // every arrow's damage and crit is set: only now are the stacks spent
                if (ultrasightReady) TweyenBuffs.ConsumeUltrasight(characterBody);
            }
        }

        /// <summary>Slightly upwards, at a random angle in the half in front of her (never behind).</summary>
        private Vector3 LaunchDirection(Vector3 aim)
        {
            Vector3 forward = new Vector3(aim.x, 0f, aim.z);
            if (forward.sqrMagnitude < 0.0001f) forward = characterDirection ? characterDirection.forward : transform.forward;
            forward.y = 0f;
            forward.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            float yaw = Random.Range(-maxLaunchYaw, maxLaunchYaw);
            float pitch = Random.Range(minLaunchPitch, maxLaunchPitch);
            return Quaternion.AngleAxis(yaw, Vector3.up) * Quaternion.AngleAxis(-pitch, right) * forward;
        }

        /// <summary>After the shot: moving, sprinting or another skill ends it.</summary>
        private bool ShouldCancel()
        {
            if (inputBank)
            {
                if (inputBank.moveVector.sqrMagnitude > 0.01f) return true;
                if (inputBank.sprint.justPressed) return true;
            }
            EntityStateMachine weapon = EntityStateMachine.FindByCustomName(gameObject, "Weapon");
            return weapon && weapon.state is StellarArrow;
        }

        /// <summary>After the shot, a jump cancels it into a normal jump (keeping the recoil's momentum).</summary>
        private bool TryJumpCancel()
        {
            if (!inputBank || !characterMotor || !characterBody) return false;
            if (!inputBank.jump.justPressed || inputBank.jump.hasPressBeenClaimed) return false;
            if (characterMotor.jumpCount >= characterBody.maxJumpCount) return false;

            inputBank.jump.hasPressBeenClaimed = true;
            Vector3 velocity = characterMotor.velocity;
            velocity.y = characterBody.jumpPower * characterMotor.jumpPowerPenaltyCoefficient;
            characterMotor.velocity = velocity;
            characterMotor.Motor.ForceUnground();
            characterMotor.jumpCount++;
            characterBody.TriggerJumpEventGlobally();
            if (sfxLocator && !string.IsNullOrEmpty(sfxLocator.jumpSound)) Util.PlaySound(sfxLocator.jumpSound, gameObject);
            PlayCrossfade("Body", "Jump", 0.05f);
            return true;
        }

        public override void OnExit()
        {
            Animator animator = GetModelAnimator();
            if (animator)
            {
                int layer = animator.GetLayerIndex(Layer);
                if (layer >= 0 && (animator.GetCurrentAnimatorStateInfo(layer).IsName("MultilockFire") || animator.GetNextAnimatorStateInfo(layer).IsName("MultilockFire")))
                {
                    animator.CrossFadeInFixedTime("BufferEmpty", 0.15f, layer);
                }
            }
            base.OnExit();
        }

        /// <summary>Nothing interrupts it until every arrow is out; then anything can.</summary>
        public override InterruptPriority GetMinimumInterruptPriority()
        {
            return hasFired ? InterruptPriority.Any : InterruptPriority.Frozen;
        }
    }
}
