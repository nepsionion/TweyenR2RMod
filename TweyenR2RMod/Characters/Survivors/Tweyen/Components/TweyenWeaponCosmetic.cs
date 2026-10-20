using RoR2;
using UnityEngine;

namespace TweyenR2RMod.Survivors.Tweyen.Components
{
    /// <summary>
    /// Shows the bow chosen in the loadout's "Weapon Cosmetic" row on Tweyen's model (<see cref="TweyenWeapons"/>).
    /// Reads the choice from her master's loadout, which the game syncs to every player, so everyone sees the same bow.
    /// Checked every frame: the loadout can reach a client a little after the body.
    /// </summary>
    public class TweyenWeaponCosmetic : MonoBehaviour
    {
        private CharacterBody body;
        private int slot = -1;
        private int shown = -1;
        private Transform shownOn;

        private void Awake()
        {
            body = GetComponent<CharacterBody>();
            slot = TweyenWeapons.LoadoutSlot(gameObject);
        }

        private void Update()
        {
            CharacterMaster master = body ? body.master : null;
            int chosen = master ? TweyenWeapons.Chosen(master.loadout, body.bodyIndex, slot) : 0;
            Transform model = body && body.modelLocator ? body.modelLocator.modelTransform : null;
            if (!model || (chosen == shown && model == shownOn)) return;

            TweyenWeapons.Show(model.GetComponent<ChildLocator>(), chosen);
            shown = chosen;
            shownOn = model;
        }
    }

    /// <summary>The character-select display shows the bow the player picks, as soon as they pick it.</summary>
    public class TweyenWeaponCosmeticDisplay : MonoBehaviour
    {
        private CharacterSelectSurvivorPreviewDisplayController preview;
        private ChildLocator locator;
        private NetworkUser user;

        private void Awake()
        {
            preview = GetComponent<CharacterSelectSurvivorPreviewDisplayController>();
            locator = GetComponent<ChildLocator>();
            NetworkUser.onLoadoutChangedGlobal += OnLoadoutChanged;
        }

        private void OnDestroy()
        {
            NetworkUser.onLoadoutChangedGlobal -= OnLoadoutChanged;
        }

        private void Update()
        {
            // the character select screen assigns the player after the display is created
            NetworkUser current = preview ? preview.networkUser : null;
            if (current == user) return;
            user = current;
            OnLoadoutChanged(user);
        }

        private void OnLoadoutChanged(NetworkUser changed)
        {
            if (!changed || changed != user) return;
            Loadout loadout = Loadout.RequestInstance();
            changed.networkLoadout.CopyLoadout(loadout);
            TweyenWeapons.Show(locator, TweyenWeapons.Chosen(loadout, TweyenWeapons.BodyIndex, TweyenWeapons.LoadoutSlot()));
            Loadout.ReturnInstance(loadout);
        }
    }
}
