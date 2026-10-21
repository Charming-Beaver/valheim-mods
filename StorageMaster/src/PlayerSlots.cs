using UnityEngine;

namespace StorageMaster
{
    /// <summary>
    /// The regular slots of the player inventory. Other mods add slots with fixed purposes below them (ExtraSlots,
    /// SeneaL UI's equipment and quick slots) or slots that can't be used (SeneaL UI's slot count). Sort and the Storage
    /// window leave those alone, like the hotbar.
    /// </summary>
    public static class PlayerSlots
    {
        /// <summary>True when another mod adds slots of its own to the player inventory.</summary>
        public static bool OtherModsSlots => ExtraSlotsCompat.Active || SeneaLCompat.Active;

        /// <summary>How many rows of the player inventory are regular inventory (all of them without those mods).</summary>
        public static int Rows(Inventory inventory) => Mathf.Min(ExtraSlotsCompat.PlayerRows(inventory), SeneaLCompat.PlayerRows(inventory));

        /// <summary>True for a slot of the regular inventory, not one of another mod's slots.</summary>
        public static bool IsRegular(Inventory inventory, Vector2i pos)
        {
            return pos.y < Rows(inventory) && !ExtraSlotsCompat.IsSlot(pos) && !SeneaLCompat.IsDead(inventory, pos);
        }
    }
}
