using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using Microsoft.Extensions.Logging;

namespace Warmup1v1;

public class WarmupManager
{
    private readonly Warmup1v1Plugin _plugin;

    public bool IsWarmupActive { get; private set; }
    public string CurrentMap => Server.MapName;

    public bool MapSupports1v1()
    {
        return _plugin.Config.AllowedMaps.Contains(CurrentMap, StringComparer.OrdinalIgnoreCase);
    }

    private readonly Dictionary<int, bool> _inDuel = new();
    private readonly HashSet<int> _gaveGrenadesThisLife = new();

    private bool _isFFAActive;

    public WarmupManager(Warmup1v1Plugin plugin)
    {
        _plugin = plugin;
    }

    // ===== Warmup State =====

    public void CheckWarmupState()
    {
        if (!IsWarmupActive)
        {
            var gameRulesEntities = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules");
            var gameRulesProxy = gameRulesEntities.FirstOrDefault();
            if (gameRulesProxy?.GameRules?.WarmupPeriod == true)
            {
                OnWarmupStart();
            }
        }
        else
        {
            // Re-check: has warmup ended?
            var gameRulesEntities = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules");
            var gameRulesProxy = gameRulesEntities.FirstOrDefault();
            if (gameRulesProxy?.GameRules?.WarmupPeriod == false)
            {
                OnWarmupEnd();
            }
        }
    }

    public void OnWarmupStart()
    {
        if (IsWarmupActive) return;
        IsWarmupActive = true;
        _plugin.Logger.LogInformation("[Warmup1v1] Warmup started");

        EnableFFA();

        if (_plugin.Config.EnableBunnyHop)
        {
            Server.ExecuteCommand("sv_enablebunnyhopping 1");
            Server.ExecuteCommand("sv_autobunnyhopping 1");
        }

        _inDuel.Clear();
        _gaveGrenadesThisLife.Clear();
        _plugin.DuelManager.IsDuelLocked = false;

        // Give grenades and god mode to all existing players
        foreach (var player in Utilities.GetPlayers())
        {
            if (Warmup1v1Plugin.IsPlayerValid(player))
            {
                _plugin.SetGodMode(player, true);
                GiveWarmupGrenades(player);
            }
        }
    }

    public void OnWarmupEnd()
    {
        if (!IsWarmupActive) return;
        IsWarmupActive = false;
        _plugin.Logger.LogInformation("[Warmup1v1] Warmup ended");

        _plugin.DuelManager.ForceEndAllDuels();
        _plugin.VoteManager.CancelAllVotes();
        RestoreFFA();

        Server.ExecuteCommand("sv_enablebunnyhopping 0");
        Server.ExecuteCommand("sv_autobunnyhopping 0");

        foreach (var player in Utilities.GetPlayers())
        {
            if (Warmup1v1Plugin.IsPlayerValid(player))
            {
                _plugin.SetGodMode(player, false);
            }
        }

        _inDuel.Clear();
        _gaveGrenadesThisLife.Clear();
    }

    // ===== FFA =====

    private void EnableFFA()
    {
        _isFFAActive = true;
        Server.ExecuteCommand("mp_friendlyfire 1");
        Server.ExecuteCommand("mp_teammates_are_enemies 1");
        // Remove damage reduction so grenades actually hurt
        Server.ExecuteCommand("ff_damage_reduction_bullets 0");
        Server.ExecuteCommand("ff_damage_reduction_grenade 0");
        Server.ExecuteCommand("ff_damage_reduction_other 0");
    }

    public void RestoreFFA()
    {
        if (!_isFFAActive) return;
        _isFFAActive = false;
        // Only restore teammates_are_enemies, keep friendlyfire as-is (server default)
        Server.ExecuteCommand("mp_teammates_are_enemies 0");
        // Restore damage reduction to normal values
        Server.ExecuteCommand("ff_damage_reduction_bullets 0.1");
        Server.ExecuteCommand("ff_damage_reduction_grenade 0.25");
        Server.ExecuteCommand("ff_damage_reduction_other 0.25");
    }

    // ===== Duel State =====

    public void SetPlayerInDuel(CCSPlayerController player, bool inDuel)
    {
        int slot = (int)player.Index;
        _inDuel[slot] = inDuel;

        if (inDuel)
        {
            _plugin.WeaponManager.RemoveGrenades(player);
        }
    }

    // ===== Grenade Management =====

    public void OnPlayerSpawn(CCSPlayerController player)
    {
        if (!IsWarmupActive) return;
        int slot = (int)player.Index;
        if (_inDuel.TryGetValue(slot, out var inDuel) && inDuel) return;

        _gaveGrenadesThisLife.Remove(slot);

        _plugin.AddTimer(0.2f, () =>
        {
            if (!Warmup1v1Plugin.IsPlayerValid(player)) return;
            if (_inDuel.TryGetValue(slot, out var d) && d) return;
            GiveWarmupGrenades(player);
        });
    }

    public void OnPlayerDeath(CCSPlayerController player)
    {
        if (!IsWarmupActive) return;
        int slot = (int)player.Index;
        _gaveGrenadesThisLife.Remove(slot);

        // Re-give grenades on respawn (handled by spawn event)
    }

    public void OnGrenadeThrown(CCSPlayerController player)
    {
        if (!IsWarmupActive) return;
        int slot = (int)player.Index;
        if (_inDuel.TryGetValue(slot, out var inDuel) && inDuel) return;

        // Refill after short cooldown
        _plugin.AddTimer(0.5f, () =>
        {
            if (!Warmup1v1Plugin.IsPlayerValid(player)) return;
            if (!IsWarmupActive) return;
            if (_inDuel.TryGetValue(slot, out var d) && d) return;
            GiveWarmupGrenades(player);
        });
    }

    private void GiveWarmupGrenades(CCSPlayerController player)
    {
        if (!_plugin.Config.EnableGrenadeRefill) return;
        if (!IsWarmupActive) return;

        int slot = (int)player.Index;
        if (_inDuel.TryGetValue(slot, out var inDuel) && inDuel) return;

        if (!HasGrenadeType(player, "flashbang"))
            player.GiveNamedItem("weapon_flashbang");
        if (!HasGrenadeType(player, "smokegrenade"))
            player.GiveNamedItem("weapon_smokegrenade");
        if (!HasGrenadeType(player, "hegrenade"))
            player.GiveNamedItem("weapon_hegrenade");

        bool hasFire = HasGrenadeType(player, "molotov") || HasGrenadeType(player, "incgrenade");
        if (!hasFire)
        {
            if (player.TeamNum == 2)
                player.GiveNamedItem("weapon_molotov");
            else
                player.GiveNamedItem("weapon_incgrenade");
        }

        _gaveGrenadesThisLife.Add(slot);
    }

    private void RemoveAllGrenades(CCSPlayerController player)
    {
        var weapons = player.PlayerPawn?.Value?.WeaponServices?.MyWeapons;
        if (weapons == null) return;
        foreach (var weaponHandle in weapons)
        {
            var weapon = weaponHandle.Value;
            if (weapon == null) continue;
            string name = weapon.DesignerName;
            if (name.Contains("flashbang") || name.Contains("smokegrenade") ||
                name.Contains("hegrenade") || name.Contains("molotov") ||
                name.Contains("incgrenade") || name.Contains("decoy"))
            {
                weapon.Remove();
            }
        }
    }

    private bool HasGrenadeType(CCSPlayerController player, string grenadeType)
    {
        var weapons = player.PlayerPawn?.Value?.WeaponServices?.MyWeapons;
        if (weapons == null) return false;
        foreach (var weaponHandle in weapons)
        {
            if (weaponHandle.Value?.DesignerName.Contains(grenadeType) == true)
                return true;
        }
        return false;
    }
}
