using R2API;
using RoR2;
using RoR2.Projectile;
using TweyenR2RMod.Modules;
using UnityEngine;
using UnityEngine.AddressableAssets;
using GameAssetPaths = RoR2BepInExPack.GameAssetPathsBetter;

namespace TweyenR2RMod.Survivors.Tweyen.Projectiles
{
    /// <summary>
    /// Builds Tweyen's arrow projectiles. All arrows share one body:
    /// - flight and homing: a clone of the Railgunner's "XQR Smart Round System" projectile
    ///   (120 m/s, 20 m / 90° target cone, 1080°/s steering; locking on resets the lifetime to 2 s),
    ///   20% faster and with a 2 s lifetime from launch (the Railgunner round only gets 0.4 s before locking on);
    /// - look: the arrow from Granblue Fantasy Relink (TweyenArrowGhost in the asset bundle), releasing a violet fire
    ///   bolt from its tip (<see cref="TweyenArrowBolt"/>) so it is easy to see;
    /// - impact: the Huntress's arrow hit effect;
    /// and differ only in their <see cref="TweyenArrowImpact"/> subclass.
    /// </summary>
    public static class TweyenArrows
    {
        public const float SPEED_MULTIPLIER = 1.2f;
        /// <summary>Lifetime before locking onto a target (seconds). Locking on still resets it to 2 s, as for the Railgunner.</summary>
        public const float LIFETIME = 2f;

        /// <summary>The default arrow (damage only).</summary>
        public static GameObject basicArrow;
        /// <summary>
        /// Wheel of Death's arrows: they fly where she faces as she spins, slightly upwards, rising over terrain in their
        /// way (<see cref="ArrowTerrainHover"/>) instead of flying into it; light homing lets them dive onto enemies below.
        /// </summary>
        public static GameObject wheelArrow;
        /// <summary>Valiant Weave's arrow: no homing, flies straight and flat, stops at the first enemy or terrain.</summary>
        public static GameObject valiantArrow;
        /// <summary>270 deg/s: enough to dive onto a grounded enemy they pass over, not enough to bunch up the ring.</summary>
        public const float WHEEL_HOMING = 0.25f;

        /// <summary>
        /// Two-Crown's Strife's arrows: the primary's homing, but each keeps the enemy it is fired at (the skill spreads
        /// them over different enemies) instead of switching to whatever its own finder sees first.
        /// </summary>
        public static GameObject twoCrownArrow;
        public const float TWO_CROWN_PROC_COEFFICIENT = 0.2f;
        /// <summary>They fly straight this long (s) before homing, so their circle visibly widens first.</summary>
        public const float TWO_CROWN_HOMING_DELAY = 0.1f;

        private static GameObject ghostPrefab;
        /// <summary>The arrow's look (mesh and fire bolt), for effects that show an arrow.</summary>
        internal static GameObject Ghost => ghostPrefab;
        private static GameObject impactEffect;

        public static void Init(AssetBundle assetBundle)
        {
            ghostPrefab = CreateGhost(assetBundle);
            impactEffect = Addressables.LoadAssetAsync<GameObject>(GameAssetPaths.RoR2_Base_Huntress.OmniImpactVFXHuntress_prefab).WaitForCompletion();

            basicArrow = Create<BasicArrowImpact>("TweyenBasicArrow");
            wheelArrow = Create<WheelOfDeathArrowImpact>("TweyenWheelArrow", WHEEL_HOMING);
            valiantArrow = Create<BasicArrowImpact>("TweyenValiantArrow");
            foreach (ProjectileSteerTowardTarget steer in valiantArrow.GetComponents<ProjectileSteerTowardTarget>()) Object.DestroyImmediate(steer);
            foreach (ProjectileDirectionalTargetFinder finder in valiantArrow.GetComponents<ProjectileDirectionalTargetFinder>()) Object.DestroyImmediate(finder);
            foreach (ProjectileSphereTargetFinder finder in valiantArrow.GetComponents<ProjectileSphereTargetFinder>()) Object.DestroyImmediate(finder);
            valiantArrow.GetComponent<ProjectileSimple>().oscillate = false;
            Rigidbody valiantBody = valiantArrow.GetComponent<Rigidbody>();
            if (valiantBody) valiantBody.useGravity = false;
            valiantArrow.GetComponent<ProjectileDamage>().damageType = DamageTypeCombo.GenericUtility;

            // each skill's arrows are tagged with its slot (Peerless Eye tells skills' hits apart by it)
            basicArrow.GetComponent<ProjectileDamage>().damageType = DamageTypeCombo.GenericPrimary;
            wheelArrow.GetComponent<ProjectileDamage>().damageType = DamageTypeCombo.GenericUtility;

            twoCrownArrow = Create<TwoCrownArrowImpact>("TweyenTwoCrownArrow");
            twoCrownArrow.GetComponent<ProjectileDamage>().damageType = DamageTypeCombo.GenericSpecial;
            twoCrownArrow.GetComponent<ProjectileController>().procCoefficient = TWO_CROWN_PROC_COEFFICIENT;
            twoCrownArrow.AddComponent<ProjectileHomingDelay>().delay = TWO_CROWN_HOMING_DELAY;
            foreach (ProjectileDirectionalTargetFinder finder in twoCrownArrow.GetComponents<ProjectileDirectionalTargetFinder>())
            {
                finder.onlySearchIfNoTarget = true; // keep the assigned enemy; look for another only if it dies
                finder.allowTargetLoss = false;     // even when it is beyond the finder's own (short) range
            }

            // fired flat in all directions, they would mostly hit uneven ground: they rise over it instead
            wheelArrow.AddComponent<ArrowTerrainHover>();
            // their homing only picks enemies it can see (not ones below the floor she stands on), and lets go of them
            // when they go out of sight
            foreach (ProjectileDirectionalTargetFinder finder in wheelArrow.GetComponents<ProjectileDirectionalTargetFinder>())
            {
                finder.testLoS = true;
                finder.allowTargetLoss = true;
            }
            foreach (ProjectileSphereTargetFinder finder in wheelArrow.GetComponents<ProjectileSphereTargetFinder>())
            {
                finder.testLoS = true;
                finder.allowTargetLoss = true;
            }
            // and nothing borrowed from the Railgunner round may tilt them out of their launch line
            ProjectileSimple wheelSimple = wheelArrow.GetComponent<ProjectileSimple>();
            wheelSimple.oscillate = false;
            Rigidbody wheelBody = wheelArrow.GetComponent<Rigidbody>();
            if (wheelBody) wheelBody.useGravity = false;
        }

        /// <summary>
        /// Creates and registers an arrow projectile whose hit behaviour is <typeparamref name="TImpact"/>.
        /// <paramref name="homingMultiplier"/> scales how fast it turns toward its target (1 = the Railgunner's 1080 deg/s).
        /// </summary>
        public static GameObject Create<TImpact>(string name, float homingMultiplier = 1f) where TImpact : TweyenArrowImpact
        {
            GameObject railgunnerRound = Addressables.LoadAssetAsync<GameObject>(GameAssetPaths.RoR2_DLC1_Railgunner.RailgunnerPistolProjectile_prefab).WaitForCompletion();
            GameObject arrow = PrefabAPI.InstantiateClone(railgunnerRound, name, true);

            ProjectileSimple simple = arrow.GetComponent<ProjectileSimple>();
            simple.desiredForwardSpeed *= SPEED_MULTIPLIER;
            // the Railgunner round only lives 0.4 s until it locks on (then 2 s); arrows get 2 s either way
            simple.lifetime = LIFETIME;
            simple.lifetimeExpiredEffect = null; // arrows that run out of time just disappear

            ProjectileSteerTowardTarget steer = arrow.GetComponent<ProjectileSteerTowardTarget>();
            if (steer) steer.rotationSpeed *= homingMultiplier;

            ProjectileController controller = arrow.GetComponent<ProjectileController>();
            // the Railgunner's ghost is referenced by address, which takes priority over ghostPrefab: clear it
            controller.ghostPrefabAddress = new AssetReferenceT<GameObject>(string.Empty);
#pragma warning disable CS0618 // assetbundle ghosts can only go in ghostPrefab
            controller.ghostPrefab = ghostPrefab;
#pragma warning restore CS0618

            ProjectileSingleTargetImpact vanillaImpact = arrow.GetComponent<ProjectileSingleTargetImpact>();
            bool destroyOnWorld = !vanillaImpact || vanillaImpact.destroyOnWorld;
            if (vanillaImpact) UnityEngine.Object.DestroyImmediate(vanillaImpact);

            TImpact impact = arrow.AddComponent<TImpact>();
            impact.impactEffect = impactEffect;
            impact.destroyOnWorld = destroyOnWorld;

            Content.AddProjectilePrefab(arrow);
            return arrow;
        }

        /// <summary>Blue-violet of the arrow's texture: the colour of its fire bolt.</summary>
        private static readonly Color effectTint = new Color(0.78f, 0.62f, 1f, 1f);

        private static GameObject CreateGhost(AssetBundle assetBundle)
        {
            GameObject bundleGhost = assetBundle.LoadAsset<GameObject>("TweyenArrowGhost");
            if (!bundleGhost)
            {
                Log.Error("TweyenArrowGhost missing from the asset bundle");
                return null;
            }
            GameObject ghost = PrefabAPI.InstantiateClone(bundleGhost, "TweyenArrowGhost", false);
            if (!ghost.GetComponent<ProjectileGhostController>()) ghost.AddComponent<ProjectileGhostController>();
            if (!ghost.GetComponent<VFXAttributes>())
            {
                VFXAttributes vfx = ghost.AddComponent<VFXAttributes>();
                vfx.vfxPriority = VFXAttributes.VFXPriority.Always;
            }
            foreach (Renderer renderer in ghost.GetComponentsInChildren<Renderer>())
            {
                renderer.sharedMaterial = renderer.sharedMaterial.ConvertDefaultShaderToHopoo().SetCull(false);
            }
            TweyenArrowBolt.Attach(ghost, effectTint);
            return ghost;
        }
    }
}
