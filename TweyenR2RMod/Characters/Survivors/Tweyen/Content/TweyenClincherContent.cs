using R2API;
using RoR2;
using RoR2.Projectile;
using TweyenR2RMod.Modules;
using TweyenR2RMod.Survivors.Tweyen.Projectiles;
using UnityEngine;
using UnityEngine.AddressableAssets;
using GameAssetPaths = RoR2BepInExPack.GameAssetPathsBetter;

namespace TweyenR2RMod.Survivors.Tweyen
{
    /// <summary>
    /// What Clincher (secondary) needs besides its state:
    /// - its beam: the Railgunner's Supercharged Railgun tracer (beam and the rings it leaves behind), recoloured to a
    ///   violet-tinged blue, with a large arrow and its fire bolt (same colour) riding the tracer's head down the line;
    /// - its hit effect: the Railgunner's railgun impact, recoloured.
    /// (Her armor while drawing is <see cref="TweyenBuffs.skillArmor"/>.)
    /// </summary>
    internal static class TweyenClincherContent
    {
        /// <summary>Size of the arrow riding the beam, relative to a normal arrow.</summary>
        public const float BEAM_ARROW_SCALE = 3f;

        public static GameObject tracerEffect;
        public static GameObject impactEffect;

        /// <summary>Clincher's colour: bluer than the arrows' violet.</summary>
        private static readonly Color blue = new Color(0.55f, 0.65f, 1f, 1f);

        /// <summary>Call after <see cref="TweyenArrows.Init"/> (the beam carries the arrow's look).</summary>
        public static void Init()
        {
            GameObject tracer = Addressables.LoadAssetAsync<GameObject>(GameAssetPaths.RoR2_DLC1_Railgunner.TracerRailgunSuper_prefab).WaitForCompletion();
            if (tracer)
            {
                tracerEffect = PrefabAPI.InstantiateClone(tracer, "TweyenClincherTracer", false);
                AddBeamArrow(tracerEffect);
                TweyenEffectRecolour.RecolourEffect(tracerEffect, blue); // the beam's arrow and fire bolt too
                Content.CreateAndAddEffectDef(tracerEffect);
            }
            else
            {
                Log.Warning("Railgunner's supercharged tracer not found; Clincher fires without its beam");
            }

            GameObject impact = Addressables.LoadAssetAsync<GameObject>(GameAssetPaths.RoR2_DLC1_Railgunner.ImpactRailgun_prefab).WaitForCompletion();
            if (impact)
            {
                impactEffect = PrefabAPI.InstantiateClone(impact, "TweyenClincherImpact", false);
                TweyenEffectRecolour.RecolourEffect(impactEffect, blue);
                Content.CreateAndAddEffectDef(impactEffect);
            }
        }

        /// <summary>
        /// The tracer turns to face down the beam and moves its head from the bow to the end of the line: a large copy
        /// of the arrow (with its fire bolt) on the head streaks down the beam.
        /// </summary>
        private static void AddBeamArrow(GameObject tracerObject)
        {
            Tracer tracer = tracerObject.GetComponent<Tracer>();
            GameObject arrowLook = TweyenArrows.Ghost;
            if (!tracer || !tracer.headTransform || !arrowLook) return;

            GameObject arrow = Object.Instantiate(arrowLook, tracer.headTransform, false);
            arrow.name = "ClincherArrow";
            arrow.transform.localPosition = Vector3.zero;
            arrow.transform.localRotation = Quaternion.identity;
            arrow.transform.localScale = Vector3.one * BEAM_ARROW_SCALE;
            // only its look: the ghost's own bookkeeping belongs to projectiles
            foreach (ProjectileGhostController ghost in arrow.GetComponentsInChildren<ProjectileGhostController>(true)) Object.DestroyImmediate(ghost);
            foreach (VFXAttributes vfx in arrow.GetComponentsInChildren<VFXAttributes>(true)) Object.DestroyImmediate(vfx);
        }
    }
}
