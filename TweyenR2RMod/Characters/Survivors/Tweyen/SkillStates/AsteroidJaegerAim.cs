using EntityStates;
using RoR2;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TweyenR2RMod.Survivors.Tweyen.SkillStates
{
    /// <summary>
    /// Asteroid Jaeger (secondary), held: Tweyen aims (Relink's Multilock Hail charge: raise the bow, then hold) with the
    /// camera moved in to first person (about half the Railgunner's scope zoom; with the first-person aim turned off in
    /// the config, it only zooms in behind her). While aiming:
    /// - she can't move (gravity still applies);
    /// - marks charge up, one per <see cref="secondsPerMark"/> (faster with attack speed), up to 11 (+5 per extra
    ///   secondary charge). They go to enemies on screen within <see cref="range"/> m, those with the most max health +
    ///   shield + barrier first, cycling through them again when there are fewer enemies than marks. The list of
    ///   enemies is taken when aiming starts and only taken again when a marked enemy dies (its marks then go to the
    ///   new list) or while it is still empty. Each marked enemy shows the Huntress's reticle;
    /// - her primary is Multilock Hail (<see cref="MultilockHailFire"/>), with one charge per mark, which fires them all.
    /// Releasing the secondary or using another skill (not the primary) ends it; nothing is spent then.
    /// </summary>
    public class AsteroidJaegerAim : BaseSkillState
    {
        public static float secondsPerMark = 0.2f;
        public static float range = 200f;
        public static float cameraFov = 40f;

        private const string Layer = "FullBody, Override";

        private AsteroidJaegerMarks marks;
        private CameraTargetParams.CameraParamsOverrideHandle cameraHandle;
        private float charge;
        private List<HurtBox> targets;
        private int assigned = -1;
        private bool overrideSet;
        private bool endedByMultilock;
        private int soundedMarks;

        public override void OnEnter()
        {
            base.OnEnter();
            marks = GetComponent<AsteroidJaegerMarks>();
            if (marks) marks.Clear();

            if (isAuthority)
            {
                if (characterMotor)
                {
                    characterMotor.moveDirection = Vector3.zero;
                    characterMotor.velocity = new Vector3(0f, characterMotor.velocity.y, 0f);
                }
                if (cameraTargetParams)
                {
                    CharacterCameraParamsData data = cameraTargetParams.cameraParams.data;
                    data.fov = cameraFov;
                    if (TweyenConfig.firstPersonAim.Value)
                    {
                        data.isFirstPerson = true;
                        data.idealLocalCameraPos = Vector3.zero;
                        data.pivotVerticalOffset = 0f;
                    }
                    cameraHandle = cameraTargetParams.AddParamsOverride(new CameraTargetParams.CameraParamsOverrideRequest
                    {
                        cameraParamsData = data,
                        priority = 0.5f,
                    }, 0.2f);
                }
            }
            if (skillLocator && skillLocator.primary)
            {
                skillLocator.primary.SetSkillOverride(this, TweyenAsteroidJaeger.multilockHailDef, GenericSkill.SkillOverridePriority.Contextual);
                overrideSet = true;
                skillLocator.primary.stock = 0; // no marks yet
            }
            PlayCrossfade(Layer, "MultilockStart", 0.1f);
            Util.PlaySound(TweyenSounds.JaegerCharge, gameObject);
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            if (!isAuthority) return;

            // rooted while aiming, still falling if in the air
            if (characterMotor)
            {
                characterMotor.moveDirection = Vector3.zero;
                characterMotor.velocity = new Vector3(0f, characterMotor.velocity.y, 0f);
            }
            Ray aim = GetAimRay();
            StartAimMode(aim, 0.5f);
            if (characterDirection)
            {
                Vector3 flat = new Vector3(aim.direction.x, 0f, aim.direction.z);
                if (flat.sqrMagnitude > 0.0001f) characterDirection.forward = flat.normalized;
            }

            if (!inputBank || !inputBank.skill2.down)
            {
                outer.SetNextStateToMain(); // released without firing: nothing spent
                return;
            }

            int maxArrows = TweyenAsteroidJaeger.MaxArrows(characterBody);
            charge = Mathf.Min(maxArrows, charge + GetDeltaTime() * characterBody.attackSpeed / secondsPerMark);
            UpdateMarks(Mathf.FloorToInt(charge), aim);
            HandleSkills();
        }

        /// <summary>
        /// The game reads the skill buttons only in the "Body" machine's main state, which this replaces: so it reads
        /// them here (the primary, now Multilock Hail, and the utility and special, which end the aim). Like
        /// GenericCharacterMain.HandleSkill.
        /// </summary>
        private void HandleSkills()
        {
            if (!skillLocator) return;
            HandleSkill(skillLocator.primary, ref inputBank.skill1);
            HandleSkill(skillLocator.utility, ref inputBank.skill3);
            HandleSkill(skillLocator.special, ref inputBank.skill4);
        }

        private void HandleSkill(GenericSkill skill, ref InputBankTest.ButtonState button)
        {
            if (!skill || !button.down || (skill.mustKeyPress && button.hasPressBeenClaimed)) return;
            if (skill.ExecuteIfReady()) button.hasPressBeenClaimed = true;
        }

        /// <summary>
        /// Assigns the charged marks (enemies on screen, toughest first, cycling). The enemy list is taken again only
        /// when a marked enemy has died, or while it has no one in it.
        /// </summary>
        private void UpdateMarks(int count, Ray aim)
        {
            if (!marks || count <= 0) return;
            bool died = marks.marks.Any(h => !h || !h.healthComponent || !h.healthComponent.alive);
            if (died || targets == null || targets.Count == 0)
            {
                targets = FindTargets(aim);
                assigned = -1; // reassign every mark to the new list
            }
            if (count == assigned || targets.Count == 0) return;
            var newMarks = new List<HurtBox>();
            for (int i = 0; i < count; i++) newMarks.Add(targets[i % targets.Count]);
            marks.SetMarks(newMarks);
            // the lock-on sound for each new mark (not when marks only move to a new list)
            if (count > soundedMarks)
            {
                Util.PlaySound(TweyenSounds.Mark, gameObject);
                soundedMarks = count;
            }
            assigned = count;
            // one Multilock Hail charge per mark
            if (skillLocator && skillLocator.primary && skillLocator.primary.skillDef == TweyenAsteroidJaeger.multilockHailDef)
            {
                skillLocator.primary.stock = marks.marks.Count;
            }
        }

        private List<HurtBox> FindTargets(Ray aim)
        {
            BullseyeSearch search = new BullseyeSearch
            {
                teamMaskFilter = TeamMask.GetEnemyTeams(GetTeam()),
                searchOrigin = aim.origin,
                searchDirection = aim.direction,
                maxDistanceFilter = range,
                maxAngleFilter = 180f,
                sortMode = BullseyeSearch.SortMode.None,
                filterByLoS = true,
            };
            search.RefreshCandidates();
            search.FilterOutGameObject(gameObject);
            Camera camera = PlayerCamera();
            return search.GetResults()
                .Where(h => h && h.healthComponent && h.healthComponent.alive && OnScreen(camera, h.transform.position))
                .GroupBy(h => h.healthComponent)
                .Select(g => g.First())
                .OrderByDescending(h => h.healthComponent.fullHealth + h.healthComponent.fullShield + h.healthComponent.barrier)
                .ToList();
        }

        /// <summary>The camera of the player controlling her (this runs on that player's game).</summary>
        private Camera PlayerCamera()
        {
            foreach (CameraRigController rig in CameraRigController.readOnlyInstancesList)
            {
                if (rig && rig.target == gameObject && rig.sceneCam) return rig.sceneCam;
            }
            return null;
        }

        private static bool OnScreen(Camera camera, Vector3 position)
        {
            if (!camera) return true;
            Vector3 v = camera.WorldToViewportPoint(position);
            return v.z > 0f && v.x >= 0f && v.x <= 1f && v.y >= 0f && v.y <= 1f;
        }

        /// <summary>Called just before <see cref="OnExit"/> with the state that ends it (outer.nextState is already cleared then).</summary>
        public override void ModifyNextState(EntityState nextState)
        {
            base.ModifyNextState(nextState);
            endedByMultilock = nextState is MultilockHailFire;
        }

        public override void OnExit()
        {
            Util.PlaySound(TweyenSounds.JaegerChargeStop, gameObject);
            if (overrideSet && skillLocator && skillLocator.primary)
            {
                skillLocator.primary.UnsetSkillOverride(this, TweyenAsteroidJaeger.multilockHailDef, GenericSkill.SkillOverridePriority.Contextual);
            }
            if (cameraHandle.isValid && cameraTargetParams)
            {
                cameraTargetParams.RemoveParamsOverride(cameraHandle, 0.2f);
            }
            // reticles go; the marks stay for Multilock Hail if it is what ended the aim (it clears them)
            if (marks) marks.ShowReticles(false);
            if (!endedByMultilock) marks?.Clear();

            Animator animator = GetModelAnimator();
            if (animator && !endedByMultilock)
            {
                int layer = animator.GetLayerIndex(Layer);
                if (layer >= 0) animator.CrossFadeInFixedTime("BufferEmpty", 0.15f, layer);
            }
            base.OnExit();
        }

        /// <summary>Any other skill (or Multilock Hail) can end it.</summary>
        public override InterruptPriority GetMinimumInterruptPriority()
        {
            return InterruptPriority.Any;
        }
    }
}
