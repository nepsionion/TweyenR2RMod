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
    /// the other over <see cref="volleyDuration"/>. Each is fired slightly upwards at a random angle in front of her
    /// (clear of nearby terrain, see <see cref="FindLaunchArea"/>) and homes in on its enemy after a moment: 30% damage
    /// each (proc coefficient 1), +25% of that per Ultrasight stack;
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
        /// <summary>Spacing (deg) of the directions checked for terrain when the volley starts.</summary>
        private const float LaunchStep = 5f;

        /// <summary>Where the arrows may go (see <see cref="FindLaunchArea"/>), fixed by the first arrow.</summary>
        private Vector3 launchForward;
        private float launchYawMin, launchYawMax, launchPitchMin, launchPitchMax;
        private bool launchBlocked;

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
                if (index == 0) FindLaunchArea(origin, aimRay.direction);
                Vector3 direction = LaunchDirection(origin, target, aimRay.direction);
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

        /// <summary>
        /// The launch angles' range (a rectangle: yaw to either side, pitch up) shrunk to keep clear of terrain: the
        /// directions <see cref="LaunchStep"/> apart across the full range are checked for terrain within
        /// <see cref="TweyenAsteroidJaeger.WALL_LOOK_AHEAD"/> m, and the range becomes the largest rectangle with no
        /// blocked direction (of equal ones, the nearest the middle). Each side shrinks on its own: a wall to her left
        /// only lowers the left limit; an obstacle in the middle leaves the range on one side of it (or above it).
        /// No clear direction at all: <see cref="launchBlocked"/>.
        /// </summary>
        private void FindLaunchArea(Vector3 origin, Vector3 aim)
        {
            launchForward = new Vector3(aim.x, 0f, aim.z);
            if (launchForward.sqrMagnitude < 0.0001f) launchForward = characterDirection ? characterDirection.forward : transform.forward;
            launchForward.y = 0f;
            launchForward.Normalize();

            int yawCount = Mathf.RoundToInt(2f * maxLaunchYaw / LaunchStep) + 1;
            int pitchCount = Mathf.RoundToInt((maxLaunchPitch - minLaunchPitch) / LaunchStep) + 1;
            // blocked directions, summed (blocked[y, p] = how many with yaw index < y and pitch index < p)
            int[,] blocked = new int[yawCount + 1, pitchCount + 1];
            for (int y = 0; y < yawCount; y++)
            {
                for (int p = 0; p < pitchCount; p++)
                {
                    Vector3 direction = Direction(Yaw(y, yawCount), Pitch(p, pitchCount));
                    int hit = Physics.Raycast(origin, direction, TweyenAsteroidJaeger.WALL_LOOK_AHEAD, LayerIndex.world.mask, QueryTriggerInteraction.Ignore) ? 1 : 0;
                    blocked[y + 1, p + 1] = hit + blocked[y, p + 1] + blocked[y + 1, p] - blocked[y, p];
                }
            }

            int bestArea = 0;
            float bestOffset = float.MaxValue;
            float middleYaw = (yawCount - 1) / 2f, middlePitch = (pitchCount - 1) / 2f;
            for (int y0 = 0; y0 < yawCount; y0++)
            for (int y1 = y0; y1 < yawCount; y1++)
            for (int p0 = 0; p0 < pitchCount; p0++)
            for (int p1 = p0; p1 < pitchCount; p1++)
            {
                if (blocked[y1 + 1, p1 + 1] - blocked[y0, p1 + 1] - blocked[y1 + 1, p0] + blocked[y0, p0] > 0) continue;
                int area = (y1 - y0 + 1) * (p1 - p0 + 1);
                float offset = Mathf.Abs((y0 + y1) / 2f - middleYaw) + Mathf.Abs((p0 + p1) / 2f - middlePitch);
                if (area < bestArea || (area == bestArea && offset >= bestOffset)) continue;
                bestArea = area;
                bestOffset = offset;
                launchYawMin = Yaw(y0, yawCount);
                launchYawMax = Yaw(y1, yawCount);
                launchPitchMin = Pitch(p0, pitchCount);
                launchPitchMax = Pitch(p1, pitchCount);
            }
            launchBlocked = bestArea == 0;
        }

        private static float Yaw(int index, int count) => Mathf.Lerp(-maxLaunchYaw, maxLaunchYaw, index / (float)(count - 1));
        private static float Pitch(int index, int count) => Mathf.Lerp(minLaunchPitch, maxLaunchPitch, index / (float)(count - 1));

        private Vector3 Direction(float yaw, float pitch)
        {
            Vector3 right = Vector3.Cross(Vector3.up, launchForward);
            return Quaternion.AngleAxis(yaw, Vector3.up) * Quaternion.AngleAxis(-pitch, right) * launchForward;
        }

        /// <summary>
        /// Slightly upwards, at a random angle in the launch range (<see cref="FindLaunchArea"/>). With no clear
        /// direction at all: straight at its enemy (or along the aim).
        /// </summary>
        private Vector3 LaunchDirection(Vector3 origin, HurtBox target, Vector3 aim)
        {
            if (launchBlocked) return target ? (target.transform.position - origin).normalized : aim;
            return Direction(Random.Range(launchYawMin, launchYawMax), Random.Range(launchPitchMin, launchPitchMax));
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
