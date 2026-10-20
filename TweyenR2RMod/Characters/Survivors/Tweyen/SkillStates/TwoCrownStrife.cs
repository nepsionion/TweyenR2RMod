using EntityStates;
using RoR2;
using System.Collections.Generic;
using System.Linq;
using TweyenR2RMod.Survivors.Tweyen.Projectiles;
using UnityEngine;
using UnityEngine.Networking;

namespace TweyenR2RMod.Survivors.Tweyen.SkillStates
{
    /// <summary>
    /// Two-Crown's Strife (special): Tweyen's Two-Crown's Strife from Granblue Fantasy Relink (action 1700, motion
    /// 3460, up to where she starts to fall). She leaps back and up, hangs in the air drawing her bow while arrows
    /// appear one by one on a turning circle around it (extra arrows on a wider ring turning the other way), then
    /// fires them all at once: 10 homing arrows (+1 per full 40% attack
    /// speed above base, +10 per extra charge used: every charge is spent at once, see
    /// <see cref="charges"/>; the animation is never sped up), spread over different enemies within 200 m and 60 degrees
    /// of her aim (several per enemy when there are fewer enemies than arrows; straight ahead when there are none).
    /// They leave forwards and slightly outwards (the circle widens), then home in.
    /// Each hit: 400% damage (proc coefficient 0.2), debuffs +10 s and one more stack (see TwoCrownArrowImpact).
    /// - Unstoppable: only death ends it; no stun, freeze, shock or knockback while it lasts; hidden +10 armor.
    /// - The skill ends in the air (she falls normally from there).
    /// </summary>
    public class TwoCrownStrife : BaseSkillState, IBlocksPrimary
    {
        /// <summary>400%: enough to trigger the Runald's and Kjaro's Bands.</summary>
        public static float damageCoefficient = 4f;
        public static int baseArrowCount = 10;
        /// <summary>
        /// Arrows leave this much outwards from the circle (relative to forward), so the circle seems to grow, and fly
        /// straight for the homing delay before turning to their targets.
        /// </summary>
        public static float launchSpread = 0.25f;
        /// <summary>One more arrow for every full step of this much attack speed above base.</summary>
        public static float attackSpeedPerArrow = 0.4f;
        /// <summary>More arrows for every charge used above one.</summary>
        public static int arrowsPerExtraCharge = 10;
        public static float targetRange = 200f;
        public static float targetHalfAngle = 60f;
        /// <summary>Radius of the circle of arrows around the bow (about half the bow's length).</summary>
        public static float circleRadius = 0.8f;
        /// <summary>
        /// Arrows beyond the base count (from attack speed and extra charges) go on an outer ring this much wider, around the same centre.
        /// </summary>
        public static float outerRingScale = 1.5f;
        /// <summary>The rings turn while she draws (degrees per second): the inner one this way, the outer one the other.</summary>
        public static float ringSpinSpeed = 120f;
        /// <summary>The leap goes at least this far back (Relink's: 3.3 m) and further if an enemy in front needs it.</summary>
        public static float minLeapBack = 3f;
        public static float maxLeapBack = 10f;

        // animation times (s): arrows appear during the draw, all leave at the shot, control returns as she would fall
        public static float drawStart = 0.8f;
        public static float fireTime = 2.0f;
        public static float duration = 2.55f;

        /// <summary>
        /// Relink's root path for 3460 every 0.1 s: (up, back) in metres. The leap is 3.28 m back and 2.2 m up at
        /// 0.8 s; the shot's recoil hop takes her further back and up after 2.0 s.
        /// </summary>
        private static readonly float[,] relinkPath =
        {
            { 0.00f, 0.00f }, { 0.00f, -0.04f }, { 0.00f, -0.09f }, { 0.01f, 0.02f }, { 0.00f, 1.19f },
            { 0.10f, 2.67f }, { 1.33f, 3.08f }, { 2.00f, 3.28f }, { 2.22f, 3.28f }, { 2.35f, 3.25f },
            { 2.39f, 3.24f }, { 2.39f, 3.24f }, { 2.37f, 3.28f }, { 2.32f, 3.32f }, { 2.26f, 3.36f },
            { 2.19f, 3.39f }, { 2.13f, 3.42f }, { 2.07f, 3.44f }, { 2.00f, 3.46f }, { 1.97f, 3.46f },
            { 1.95f, 3.46f }, { 2.07f, 4.24f }, { 2.29f, 4.84f }, { 2.68f, 5.34f }, { 2.93f, 5.59f },
            { 3.12f, 5.78f }, { 3.07f, 5.85f },
        };
        private const float RelinkLeapBack = 3.28f;

        private const string Layer = "FullBody, Override";
        private const string State = "TwoCrownStrife";
        private const string BowCentre = "ArrowTip";

        /// <summary>The charges the skill had when used (all spent together; set by OncePerStageSkillDef, sent to every client).</summary>
        public int charges = 1;
        private int arrowCount;
        private bool hasFired;
        /// <summary>Used with 10+ Ultrasight: every arrow crits, and the buff is used up once they are out.</summary>
        private bool ultrasight;
        private Vector3 back;
        private float backScale = 1f;
        private Vector3 lastOffset;
        private bool ungrounded;
        private bool gravityOff;
        private SetStateOnHurt setStateOnHurt;
        private bool couldBeHitStunned, couldBeStunned, couldBeFrozen;
        private readonly List<GameObject> drawnArrows = new List<GameObject>();
        private bool drawSoundPlayed;

        public bool BlocksPrimary => true;

        public override void OnEnter()
        {
            base.OnEnter();
            ultrasight = TweyenBuffs.UltrasightReady(characterBody);
            arrowCount = baseArrowCount + Mathf.Max(0, Mathf.FloorToInt((attackSpeedStat - 1f) / attackSpeedPerArrow + 0.0001f));
            arrowCount += arrowsPerExtraCharge * Mathf.Max(0, charges - 1);

            Vector3 aim = inputBank ? inputBank.aimDirection : transform.forward;
            Vector3 flatAim = new Vector3(aim.x, 0f, aim.z);
            if (flatAim.sqrMagnitude < 0.0001f) flatAim = transform.forward;
            flatAim.Normalize();
            back = -flatAim;
            if (characterDirection) characterDirection.forward = flatAim;

            if (isAuthority)
            {
                TweyenMovement.CancelMomentum(characterMotor); // also ends a Wheel of Death dash
                EntityStateMachine weapon = EntityStateMachine.FindByCustomName(gameObject, "Weapon");
                if (weapon && weapon.state is StellarArrow) weapon.SetNextStateToMain();
                backScale = Mathf.Max(1f, LeapBackNeeded(flatAim) / RelinkLeapBack);
            }
            if (characterBody) characterBody.isSprinting = false;

            // unstoppable: no stun, freeze or shock (knockback is cancelled every step), hidden armor
            setStateOnHurt = GetComponent<SetStateOnHurt>();
            if (setStateOnHurt)
            {
                couldBeHitStunned = setStateOnHurt.canBeHitStunned;
                couldBeStunned = setStateOnHurt.canBeStunned;
                couldBeFrozen = setStateOnHurt.canBeFrozen;
                setStateOnHurt.canBeHitStunned = false;
                setStateOnHurt.canBeStunned = false;
                setStateOnHurt.canBeFrozen = false;
            }
            if (NetworkServer.active && characterBody) characterBody.AddBuff(TweyenBuffs.skillArmor);

            if (characterMotor)
            {
                CharacterGravityParameters gravity = characterMotor.gravityParameters;
                gravity.channeledAntiGravityGranterCount++;
                characterMotor.gravityParameters = gravity;
                gravityOff = true;
            }

            StartAimMode(GetAimRay(), duration);
            PlayCrossfade(Layer, State, 0.1f);
            Util.PlaySound(TweyenSounds.TwoCrown, gameObject);
        }

        /// <summary>
        /// How far back she must leap so the closest enemy in front of her (within 10 m) ends up inside the target
        /// cone, seen from where the leap leaves her (Relink's height, 2.2 m). At least <see cref="minLeapBack"/>.
        /// </summary>
        private float LeapBackNeeded(Vector3 flatAim)
        {
            float needed = minLeapBack;
            Vector3 aim = inputBank ? inputBank.aimDirection : flatAim;
            BullseyeSearch search = new BullseyeSearch
            {
                teamMaskFilter = TeamMask.GetEnemyTeams(GetTeam()),
                searchOrigin = characterBody.corePosition,
                searchDirection = flatAim,
                maxDistanceFilter = 10f,
                maxAngleFilter = 90f,
                sortMode = BullseyeSearch.SortMode.Distance,
                filterByLoS = false,
            };
            search.RefreshCandidates();
            search.FilterOutGameObject(gameObject);
            HurtBox closest = search.GetResults().FirstOrDefault();
            if (!closest) return needed;

            Vector3 start = characterBody.corePosition;
            for (float d = minLeapBack; d <= maxLeapBack; d += 0.25f)
            {
                Vector3 from = start - flatAim * d + Vector3.up * 2.2f;
                if (Vector3.Angle(aim, closest.transform.position - from) <= targetHalfAngle) return d;
            }
            return maxLeapBack;
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            if (characterBody) characterBody.isSprinting = false;

            if (isAuthority && characterMotor)
            {
                // follow Relink's path (back scaled if needed), hovering: no gravity, and no knockback or pull
                Vector3 offset = PathOffset(fixedAge);
                if (!ungrounded && offset.y > 0.01f)
                {
                    characterMotor.Motor.ForceUnground();
                    ungrounded = true;
                }
                characterMotor.rootMotion += offset - lastOffset;
                lastOffset = offset;
                characterMotor.velocity = Vector3.zero;
                characterMotor.moveDirection = Vector3.zero;
            }
            if (characterDirection && inputBank)
            {
                Vector3 aim = inputBank.aimDirection;
                aim.y = 0f;
                if (aim.sqrMagnitude > 0.0001f) characterDirection.forward = aim.normalized;
            }

            if (!hasFired && fixedAge >= fireTime) Fire();
            if (isAuthority && fixedAge >= duration) outer.SetNextStateToMain();
        }

        private Vector3 PathOffset(float t)
        {
            int last = relinkPath.GetLength(0) - 1;
            float f = Mathf.Clamp(t / 0.1f, 0f, last);
            int i = Mathf.Min((int)f, last - 1);
            float k = f - i;
            float up = Mathf.Lerp(relinkPath[i, 0], relinkPath[i + 1, 0], k);
            float away = Mathf.Lerp(relinkPath[i, 1], relinkPath[i + 1, 1], k);
            return Vector3.up * up + back * (away * backScale);
        }

        public override void Update()
        {
            base.Update();
            if (hasFired || age < drawStart) return;
            if (!drawSoundPlayed)
            {
                drawSoundPlayed = true;
                Util.PlaySound(TweyenSounds.Draw, gameObject);
            }
            // the arrows appear one by one while she draws
            int shown = Mathf.Min(arrowCount, Mathf.CeilToInt(arrowCount * Mathf.Clamp01((age - drawStart) / (fireTime - drawStart))));
            while (drawnArrows.Count < shown)
            {
                GameObject arrow = MakeDrawnArrow();
                if (!arrow) break;
                drawnArrows.Add(arrow);
            }
            Ray aim = GetAimRay();
            for (int i = 0; i < drawnArrows.Count; i++)
            {
                if (!drawnArrows[i]) continue;
                drawnArrows[i].transform.SetPositionAndRotation(CirclePoint(i, aim.direction, age), Util.QuaternionSafeLookRotation(aim.direction));
            }
        }

        private Vector3 CircleCentre()
        {
            Transform centre = FindModelChild(BowCentre);
            return centre ? centre.position : characterBody.corePosition;
        }

        /// <summary>
        /// Where arrow i is at time t of the skill, facing her aim: the first <see cref="baseArrowCount"/> on the inner
        /// ring, the rest evenly on the outer ring; the rings turn in opposite directions from the start of the draw.
        /// </summary>
        private Vector3 CirclePoint(int i, Vector3 aimDirection, float t)
        {
            Vector3 c = CircleCentre();
            Vector3 right = Vector3.Cross(Vector3.up, aimDirection);
            if (right.sqrMagnitude < 0.0001f) right = transform.right;
            right.Normalize();
            Vector3 up = Vector3.Cross(aimDirection, right).normalized;

            bool outer = i >= baseArrowCount;
            int index = outer ? i - baseArrowCount : i;
            int count = outer ? arrowCount - baseArrowCount : Mathf.Min(arrowCount, baseArrowCount);
            float spin = Mathf.Max(0f, t - drawStart) * ringSpinSpeed * Mathf.Deg2Rad * (outer ? -1f : 1f);
            float a = 2f * Mathf.PI * index / count + Mathf.PI * 0.5f + spin; // first arrow of each ring starts on top
            float radius = circleRadius * (outer ? outerRingScale : 1f);
            return c + (right * Mathf.Cos(a) + up * Mathf.Sin(a)) * radius;
        }

        private static GameObject MakeDrawnArrow()
        {
            GameObject look = TweyenArrows.Ghost;
            if (!look) return null;
            GameObject arrow = Object.Instantiate(look);
            foreach (RoR2.Projectile.ProjectileGhostController ghost in arrow.GetComponentsInChildren<RoR2.Projectile.ProjectileGhostController>(true)) Object.Destroy(ghost);
            foreach (Light light in arrow.GetComponentsInChildren<Light>(true)) light.enabled = false; // 20+ lights at once: too many
            return arrow;
        }

        private void Fire()
        {
            hasFired = true;
            Util.PlaySound(TweyenSounds.Shot, gameObject); // one for the whole volley
            ClearDrawnArrows();
            if (isAuthority) Shoot();
            // the arrows' damage and crits are set when they leave: only now is the buff used up
            if (ultrasight) TweyenBuffs.ConsumeUltrasight(characterBody);
        }

        private void Shoot()
        {

            Ray aim = GetAimRay();
            List<HurtBox> targets = FindTargets(aim);
            for (int i = 0; i < arrowCount; i++)
            {
                Vector3 origin = CirclePoint(i, aim.direction, fixedAge);
                HurtBox target = targets.Count > 0 ? targets[i % targets.Count] : null;
                // forwards and a little outwards from the circle's centre; the homing takes over after a moment
                Vector3 outwards = origin - CircleCentre();
                Vector3 direction = (aim.direction + outwards.normalized * launchSpread).normalized;
                RoR2.Projectile.ProjectileManager.instance.FireProjectile(new RoR2.Projectile.FireProjectileInfo
                {
                    projectilePrefab = TweyenArrows.twoCrownArrow,
                    position = origin,
                    rotation = Util.QuaternionSafeLookRotation(direction),
                    owner = gameObject,
                    target = target ? target.gameObject : null,
                    damage = damageCoefficient * damageStat,
                    crit = ultrasight || RollCrit(),
                    force = 0f,
                });
            }
        }

        /// <summary>Enemies in range and in the cone, one hurtbox each, closest first.</summary>
        private List<HurtBox> FindTargets(Ray aim)
        {
            BullseyeSearch search = new BullseyeSearch
            {
                teamMaskFilter = TeamMask.GetEnemyTeams(GetTeam()),
                searchOrigin = aim.origin,
                searchDirection = aim.direction,
                maxDistanceFilter = targetRange,
                maxAngleFilter = targetHalfAngle,
                sortMode = BullseyeSearch.SortMode.Distance,
                filterByLoS = true,
            };
            search.RefreshCandidates();
            search.FilterOutGameObject(gameObject);
            return search.GetResults()
                .Where(h => h && h.healthComponent && h.healthComponent.alive)
                .GroupBy(h => h.healthComponent)
                .Select(g => g.First())
                .Take(arrowCount)
                .ToList();
        }

        private void ClearDrawnArrows()
        {
            foreach (GameObject arrow in drawnArrows) if (arrow) Object.Destroy(arrow);
            drawnArrows.Clear();
        }

        public override void OnExit()
        {
            ClearDrawnArrows();
            if (gravityOff && characterMotor)
            {
                CharacterGravityParameters gravity = characterMotor.gravityParameters;
                gravity.channeledAntiGravityGranterCount--;
                characterMotor.gravityParameters = gravity;
            }
            if (setStateOnHurt)
            {
                setStateOnHurt.canBeHitStunned = couldBeHitStunned;
                setStateOnHurt.canBeStunned = couldBeStunned;
                setStateOnHurt.canBeFrozen = couldBeFrozen;
            }
            if (NetworkServer.active && characterBody && characterBody.HasBuff(TweyenBuffs.skillArmor))
            {
                characterBody.RemoveBuff(TweyenBuffs.skillArmor);
            }
            Animator animator = GetModelAnimator();
            if (animator)
            {
                int layer = animator.GetLayerIndex(Layer);
                if (layer >= 0 && (animator.GetCurrentAnimatorStateInfo(layer).IsName(State) || animator.GetNextAnimatorStateInfo(layer).IsName(State)))
                {
                    animator.CrossFadeInFixedTime("BufferEmpty", 0.2f, layer);
                }
            }
            base.OnExit();
        }

        public override void OnSerialize(NetworkWriter writer)
        {
            base.OnSerialize(writer);
            writer.Write(charges);
        }

        public override void OnDeserialize(NetworkReader reader)
        {
            base.OnDeserialize(reader);
            charges = reader.ReadInt32();
        }

        /// <summary>Only death ends it.</summary>
        public override InterruptPriority GetMinimumInterruptPriority()
        {
            return InterruptPriority.Death;
        }
    }
}
