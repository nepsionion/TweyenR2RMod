using RoR2;
using RoR2.Skills;
using UnityEngine;
using UnityEngine.Networking;

namespace TweyenR2RMod.Survivors.Tweyen
{
    /// <summary>
    /// An AI-controlled Tweyen (e.g. the Artifact of Vengeance's copy, which uses the Commando's AI and the player's
    /// loadout) always has Clincher as her secondary: the AI can't hold Asteroid Jaeger's aim and fire Multilock Hail.
    /// Server only (the AI's skills run there; clients see the states it plays).
    /// </summary>
    public class TweyenAILoadout : MonoBehaviour
    {
        public static SkillDef clincherDef;

        private void Start()
        {
            if (!NetworkServer.active || !clincherDef) return;
            CharacterBody body = GetComponent<CharacterBody>();
            CharacterMaster master = body ? body.master : null;
            if (!master || master.playerCharacterMasterController) return;
            GenericSkill secondary = body.skillLocator ? body.skillLocator.secondary : null;
            if (secondary && secondary.baseSkill is AsteroidJaegerSkillDef) secondary.SetBaseSkill(clincherDef);
        }
    }
}
