using RoR2;
using UnityEngine;

namespace TweyenR2RMod.Survivors.Tweyen.Components
{
    /// <summary>
    /// Plays a short chime (the game's own "cooldown refreshed" UI sound) when Tweyen reaches the maximum Ultrasight
    /// stacks (<see cref="TweyenBuffs.ULTRASIGHT_MAX_STACKS"/>). Only her player hears it (it's a notification for them).
    /// Watches the buff count, which every client receives, so it needs no networking of its own.
    /// </summary>
    public class UltrasightNotifier : MonoBehaviour
    {
        public const string SOUND = "Play_UI_cooldownRefresh";

        private CharacterBody body;
        private int lastCount;

        private void Awake()
        {
            body = GetComponent<CharacterBody>();
        }

        private void Update()
        {
            if (!body || !TweyenBuffs.ultrasight) return;
            int count = body.GetBuffCount(TweyenBuffs.ultrasight);
            if (count >= TweyenBuffs.ULTRASIGHT_MAX_STACKS && lastCount < TweyenBuffs.ULTRASIGHT_MAX_STACKS && body.hasEffectiveAuthority)
            {
                Util.PlaySound(SOUND, gameObject);
            }
            lastCount = count;
        }
    }
}
