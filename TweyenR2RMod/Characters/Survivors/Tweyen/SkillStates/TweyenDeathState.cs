using RoR2;
using TweyenR2RMod.Survivors.Tweyen.Components;
using UnityEngine;

namespace TweyenR2RMod.Survivors.Tweyen.SkillStates
{
    /// <summary>
    /// Tweyen's death: the Commando's (her model goes limp as a ragdoll, see <see cref="TweyenRagdoll"/>), but her bow,
    /// which the animations move on its own, is first handed to the hand holding it so it falls with her.
    /// </summary>
    public class TweyenDeathState : EntityStates.Commando.DeathState
    {
        public override void OnEnter()
        {
            Transform model = modelLocator ? modelLocator.modelTransform : null;
            if (model)
            {
                ChildLocator locator = model.GetComponent<ChildLocator>();
                Transform bow = locator ? locator.FindChild("BowArmature") : null;
                Transform armature = model.Find("Armature");
                if (bow && armature)
                {
                    Transform bowRoot = bow.childCount > 0 ? bow.GetChild(0) : bow;
                    Transform closest = null;
                    float best = float.MaxValue;
                    foreach (Transform t in armature.GetComponentsInChildren<Transform>(true))
                    {
                        if (System.Array.IndexOf(TweyenRagdoll.hands, t.name) < 0) continue;
                        float d = (t.position - bowRoot.position).sqrMagnitude;
                        if (d < best) { best = d; closest = t; }
                    }
                    if (closest) bow.SetParent(closest, true);
                }
            }
            base.OnEnter();
        }
    }
}
