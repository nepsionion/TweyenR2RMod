using JetBrains.Annotations;
using RoR2;
using RoR2.Skills;

namespace TweyenR2RMod.Survivors.Tweyen.SkillDefs
{
    /// <summary>
    /// A skill usable once per stage (Two-Crown's Strife).
    /// - Each body starts with it empty and on its full cooldown (baseRechargeInterval, which cooldown items shorten as
    ///   usual: Brainstalks, Bandolier, Purity, Alien Head...). Bodies are made anew for every stage, and also when she
    ///   is revived (Dio's Best Friend...), so either way the skill comes back on cooldown (intended).
    /// - Once used, it stays unavailable until then, whatever restocks it and however many charges it has, and shows
    ///   no cooldown (there is none to wait for).
    /// - With the "Restockable Two-Crown's Strife" option, using it changes nothing: it recharges and is restocked
    ///   like any skill (still starting each stage on cooldown).
    /// - Every charge it has is used at once; Two-Crown's Strife fires more arrows for the extra ones.
    /// - <see cref="alwaysRestockable"/> (Stellar Termination): restockable whatever the option says.
    /// </summary>
    public class OncePerStageSkillDef : SkillDef
    {
        public bool alwaysRestockable;

        private bool Restockable => alwaysRestockable || TweyenConfig.RestockableTwoCrown;

        private class InstanceData : BaseSkillInstanceData
        {
            public bool started;
            public bool used;
        }

        public override BaseSkillInstanceData OnAssigned([NotNull] GenericSkill skillSlot)
        {
            return new InstanceData();
        }

        public override bool IsReady([NotNull] GenericSkill skillSlot)
        {
            return base.IsReady(skillSlot) && !((InstanceData)skillSlot.skillInstanceData).used;
        }

        public override EntityStates.EntityState InstantiateNextState([NotNull] GenericSkill skillSlot)
        {
            EntityStates.EntityState state = base.InstantiateNextState(skillSlot);
            if (state is SkillStates.TwoCrownStrife twoCrown) twoCrown.charges = skillSlot.stock;
            return state;
        }

        public override void OnExecute([NotNull] GenericSkill skillSlot)
        {
            base.OnExecute(skillSlot);
            skillSlot.stock = 0; // every charge goes into this use
            if (!Restockable) ((InstanceData)skillSlot.skillInstanceData).used = true;
        }

        public override void OnFixedUpdate([NotNull] GenericSkill skillSlot, float deltaTime)
        {
            InstanceData data = (InstanceData)skillSlot.skillInstanceData;
            if (!data.started)
            {
                // GenericSkill fills its stock in Start(), after OnAssigned: empty it on the first update instead,
                // so the body begins on the full cooldown
                data.started = true;
                skillSlot.stock = 0;
                skillSlot.rechargeStopwatch = 0f;
            }
            if (data.used)
            {
                // spent: no recharge runs (nothing restocks it), and the timer sits at the end so no cooldown shows
                skillSlot.stock = 0;
                skillSlot.rechargeStopwatch = skillSlot.GetFinalRechargeInterval();
                return;
            }
            base.OnFixedUpdate(skillSlot, deltaTime);
        }
    }
}
