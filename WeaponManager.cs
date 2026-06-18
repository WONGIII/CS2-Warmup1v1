using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;

namespace Warmup1v1;

public class WeaponManager
{
    private readonly Warmup1v1Plugin _plugin;

    private static readonly string[] WeaponList =
    {
        "weapon_glock", "weapon_usp_silencer", "weapon_deagle", "weapon_deagle",
        "weapon_m4a1_silencer", "weapon_ak47", "weapon_ak47", "weapon_m4a1_silencer",
        "weapon_awp", "weapon_awp", "weapon_deagle", "weapon_ak47", "weapon_m4a1_silencer"
    };

    private static readonly string[] WeaponNames =
    {
        "Glock", "USP-S", "Deagle", "Deagle",
        "M4A1-S", "AK-47", "AK-47", "M4A1-S",
        "AWP", "AWP", "Deagle", "AK-47", "M4A1-S"
    };

    public WeaponManager(Warmup1v1Plugin plugin) { _plugin = plugin; }

    public string GetWeaponName(int round) => WeaponNames[(round - 1) % 13];
    public string GetWeaponClass(int round) => WeaponList[(round - 1) % 13];

    public void GiveDuelWeapons(CCSPlayerController p1, CCSPlayerController p2, int round)
    {
        string wc = GetWeaponClass(round);
        GiveWeapon(p1, wc);
        GiveWeapon(p2, wc);
    }

    public void GiveWeapon(CCSPlayerController player, string weaponClass)
    {
        if (!Warmup1v1Plugin.IsPlayerValid(player)) return;

        // CS2 safe: RemoveWeapons removes all, then re-give knife + duel weapon
        player.RemoveWeapons();

        _plugin.AddTimer(0.1f, () =>
        {
            if (!Warmup1v1Plugin.IsPlayerValid(player)) return;
            // Re-give knife
            player.GiveNamedItem("weapon_knife");
            // Give duel weapon
            player.GiveNamedItem(weaponClass);
            // Fill ammo
            FillAmmo(player, weaponClass);
        });
    }

    public void RemoveAllWeapons(CCSPlayerController player)
    {
        // Safe way: use the built-in RemoveWeapons
        player.RemoveWeapons();
    }

    public void RemoveGrenades(CCSPlayerController player)
    {
        // Handled by RemoveWeapons above - no separate removal needed
    }

    private void FillAmmo(CCSPlayerController player, string weaponClass)
    {
        var weapons = player.PlayerPawn?.Value?.WeaponServices?.MyWeapons;
        if (weapons == null) return;

        foreach (var wh in weapons)
        {
            var w = wh.Value;
            if (w == null) continue;
            if (!w.DesignerName.Contains(weaponClass.Replace("weapon_", ""))) continue;

            (int clip, int reserve) = weaponClass switch
            {
                "weapon_awp" => (5, 25),
                "weapon_ak47" => (30, 90),
                "weapon_m4a1_silencer" => (20, 60),
                "weapon_m4a1" => (30, 90),
                "weapon_glock" => (20, 120),
                "weapon_usp_silencer" => (12, 24),
                "weapon_deagle" => (7, 35),
                _ => (30, 90)
            };
            w.Clip1 = clip;
            w.ReserveAmmo[0] = reserve;
            break;
        }
    }
}
