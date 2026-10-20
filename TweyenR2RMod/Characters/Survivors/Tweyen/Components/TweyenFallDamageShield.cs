using RoR2;
using UnityEngine;

namespace TweyenR2RMod.Survivors.Tweyen.Components
{
    /// <summary>
    /// Makes the body ignore fall damage while a skill asks for it (Begin) and for a short time after (End).
    /// Uses the game's IgnoreFallDamage body flag; fall damage is applied on the server, and skill states run there
    /// too, so the flag is set wherever it matters. Never removes the flag if something else had set it.
    /// </summary>
    public class TweyenFallDamageShield : MonoBehaviour
    {
        private CharacterBody body;
        private int activeCount;
        private float expiresAt = -1f;
        private bool addedFlag;

        private void Awake()
        {
            body = GetComponent<CharacterBody>();
        }

        public void Begin()
        {
            activeCount++;
            expiresAt = -1f;
            if (body && (body.bodyFlags & CharacterBody.BodyFlags.IgnoreFallDamage) == 0)
            {
                body.bodyFlags |= CharacterBody.BodyFlags.IgnoreFallDamage;
                addedFlag = true;
            }
        }

        /// <summary>Stops protecting after <paramref name="linger"/> seconds.</summary>
        public void End(float linger)
        {
            activeCount = Mathf.Max(0, activeCount - 1);
            if (activeCount == 0)
            {
                expiresAt = Time.time + linger;
            }
        }

        private void Update()
        {
            if (activeCount == 0 && expiresAt >= 0f && Time.time >= expiresAt)
            {
                expiresAt = -1f;
                if (addedFlag && body)
                {
                    body.bodyFlags &= ~CharacterBody.BodyFlags.IgnoreFallDamage;
                }
                addedFlag = false;
            }
        }
    }
}
