using Mono.Cecil.Cil;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Helpers;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Natives;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;
using SwiftlyS2.Shared.SteamAPI;
using static AdvancedWeaponSystem.AdvancedWeaponSystem;
using static AdvancedWeaponSystem.Config;

namespace AdvancedWeaponSystem;

public static class Weapon
{
    public static string GetDesignerName(this CBasePlayerWeapon weapon)
    {
        return Instance.Helpers.GetClassnameByDefinitionIndex(weapon.AttributeManager.Item.ItemDefinitionIndex) ?? "weapon_unknown";
    }

    public static bool IsRestricted(IPlayer player, string weaponName, WeaponData weaponData, AcquireMethod acquireMethod)
    {
        if (weaponData.AdminFlagsToIgnoreBlockUsing.Length > 0 &&
            !player.IsFakeClient &&
            Instance.Permission.PlayerHasPermissions(player.SteamID, weaponData.AdminFlagsToIgnoreBlockUsing))
        {
            return false;
        }

        if (weaponData.BlockUsing == true && (weaponData.IgnorePickUpFromBlockUsing != false || acquireMethod != AcquireMethod.PickUp))
            return true;

        if (weaponData.WeaponQuota.Count == 0 && weaponData.MapSpecificQuota.Count == 0)
            return false;

        if (Instance.Helpers.GetDefinitionIndexByClassname(weaponName) is not { } defIndex)
            return false;

        int weaponsCount = 0;
        int playerCount = 0;
        var playerTeam = player.Controller.Team;

        foreach (var p in Instance.PlayerManager.GetAllPlayers())
        {
            if (p.Controller.Team != playerTeam)
                continue;

            playerCount++;

            if (p.PlayerPawn?.WeaponServices?.MyWeapons is { } myWeapons)
            {
                foreach (var weaponHandle in myWeapons)
                    weaponsCount += Count(defIndex, weaponHandle, p);
            }
        }

        if (weaponData.MapSpecificQuota.Count > 0)
        {
            string currentMap = Instance.Engine.GlobalVars.MapName.Value.ToLower();

            if (weaponData.MapSpecificQuota.TryGetValue(currentMap, out Dictionary<int, int>? mapQuota) && mapQuota.Count > 0)
            {
                int maxWeapons = mapQuota
                    .Where(kvp => playerCount >= kvp.Key)
                    .Select(kvp => kvp.Value)
                    .DefaultIfEmpty(0)
                    .Max();

                return weaponsCount >= maxWeapons;
            }
        }

        if (weaponData.WeaponQuota.Count > 0)
        {
            int maxGlobal = weaponData.WeaponQuota
               .Where(kvp => playerCount >= kvp.Key)
               .Select(kvp => kvp.Value)
               .DefaultIfEmpty(0)
               .Max();

            return weaponsCount >= maxGlobal;
        }

        return false;
    }

    private static int Count(int defIndex, CHandle<CBasePlayerWeapon> weapon, IPlayer player)
    {
        if (weapon.Value?.AttributeManager.Item.ItemDefinitionIndex != defIndex)
            return 0;

        if (player.PlayerPawn?.WeaponServices is not { } weaponServices)
            return 0;

        const int HE_SLOT = 13;
        const int FLASH_SLOT = 14;
        const int SMOKE_SLOT = 15;
        const int MOLOTOV_SLOT = 16;
        const int DECOY_SLOT = 17;

        return defIndex switch
        {
            (int)ItemDefinitionIndex.Hegrenade => Math.Max((int)weaponServices.Ammo[HE_SLOT], 1),
            (int)ItemDefinitionIndex.Flashbang => Math.Max((int)weaponServices.Ammo[FLASH_SLOT], 1),
            (int)ItemDefinitionIndex.Smokegrenade => Math.Max((int)weaponServices.Ammo[SMOKE_SLOT], 1),
            (int)ItemDefinitionIndex.Molotov or (int)ItemDefinitionIndex.Incgrenade => Math.Max((int)weaponServices.Ammo[MOLOTOV_SLOT], 1),
            (int)ItemDefinitionIndex.Decoy => Math.Max((int)weaponServices.Ammo[DECOY_SLOT], 1),
            _ => 1
        };
    }
}