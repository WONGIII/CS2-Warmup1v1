using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace Warmup1v1;

[MinimumApiVersion(201)]
public class Warmup1v1Plugin : BasePlugin, IPluginConfig<Warmup1v1Config>
{
    public override string ModuleName => "Warmup 1v1";
    public override string ModuleAuthor => "Author";
    public override string ModuleVersion => "1.0.0";

    public Warmup1v1Config Config { get; set; } = new();

    internal DuelManager DuelManager { get; private set; } = null!;
    internal WeaponManager WeaponManager { get; private set; } = null!;
    internal ArenaManager ArenaManager { get; private set; } = null!;
    internal VoteManager VoteManager { get; private set; } = null!;
    internal WarmupManager WarmupManager { get; private set; } = null!;

    // E-key edge detection: previous frame buttons per player
    private Dictionary<int, PlayerButtons> _lastButtons = new();
    // Grenade bounce counter: entity index -> bounce count (for each thrown grenade)
    private Dictionary<ulong, GrenadeBounceData> _grenadeBounces = new();

    private class GrenadeBounceData
    {
        public int Bounces;
        public string GrenadeType = "";
        public float ThrowTime;
    }

    public void OnConfigParsed(Warmup1v1Config config)
    {
        Config = config;
    }

    public override void Load(bool hotReload)
    {
        Logger.LogInformation("[Warmup1v1] Loading...");

        ArenaManager = new ArenaManager(this);
        DuelManager = new DuelManager(this);
        WeaponManager = new WeaponManager(this);
        VoteManager = new VoteManager(this);
        WarmupManager = new WarmupManager(this);

        DuelManager.Init();
        VoteManager.Init();

        // Try loading arenas now
        ArenaManager.LoadArenas();

        // Core events
        RegisterEventHandler<EventRoundEnd>(OnRoundEnd);
        RegisterEventHandler<EventRoundPrestart>(OnRoundPrestart);
        RegisterEventHandler<EventPlayerDeath>(OnPlayerDeath);
        RegisterEventHandler<EventPlayerDisconnect>(OnPlayerDisconnect);
        RegisterEventHandler<EventPlayerSpawn>(OnPlayerSpawn);
        RegisterEventHandler<EventWarmupEnd>(OnWarmupEnd);
        RegisterEventHandler<EventWeaponFire>(OnWeaponFire);
        RegisterEventHandler<EventPlayerHurt>(OnPlayerHurt);
        RegisterEventHandler<EventFlashbangDetonate>(OnFlashbangDetonate);
        RegisterEventHandler<EventHegrenadeDetonate>(OnHegrenadeDetonate);
        RegisterEventHandler<EventSmokegrenadeDetonate>(OnSmokegrenadeDetonate);
        RegisterEventHandler<EventMolotovDetonate>(OnMolotovDetonate);
        RegisterEventHandler<EventPlayerBlind>(OnPlayerBlind);
        RegisterEventHandler<EventGrenadeBounce>(OnGrenadeBounce);

        // Player commands
        AddCommand("css_1v1", "Open 1v1 challenge menu", OnCommand1v1);
        AddCommand("css_duel", "Open 1v1 duel menu", OnCommand1v1);
        AddCommand("css_testvote", "Test vote popup", OnCommandTestVote);
        AddCommand("css_sr", "Surrender current duel", OnCommandSurrender);
        AddCommand("css_accept", "Accept a duel challenge", OnCommandAccept);
        AddCommand("css_decline", "Decline a duel challenge", OnCommandDecline);
        AddCommand("css_no", "Decline a duel challenge", OnCommandDecline);
        AddCommand("no", "Decline a duel challenge", OnCommandDecline);
        // Admin commands
        AddCommand("css_setarena", "Manage arena spawn points", OnCommandSetArena);
        AddCommand("css_arena", "Show arena status", OnCommandArena);
        AddCommand("css_1v1status", "Show 1v1 plugin status", OnCommand1v1Status);

        // OnTick: E-key detection, warmup state check, grenade refill check
        RegisterListener<Listeners.OnTick>(OnTick);

        Logger.LogInformation("[Warmup1v1] Loaded successfully.");
    }

    public override void Unload(bool hotReload)
    {
        DuelManager.ForceEndAllDuels();
        VoteManager.CancelAllVotes();
        WarmupManager.RestoreFFA();
        Logger.LogInformation("[Warmup1v1] Unloaded.");
    }

    // ===== OnTick =====
    private float _lastWarmupBroadcast;

    private void OnTick()
    {
        // Refresh vote hint (before warmup check, so testvote works anytime)
        VoteManager.TickHint();

        WarmupManager.CheckWarmupState();

        if (!WarmupManager.IsWarmupActive)
            return;

        float now = Server.CurrentTime;

        // Periodic warmup broadcast
        if (now - _lastWarmupBroadcast >= 15.0f)
        {
            _lastWarmupBroadcast = now;
            foreach (var p in Utilities.GetPlayers())
            {
                if (IsPlayerValid(p) && !DuelManager.IsPlayerInDuel(p))
                    p.PrintToChat($"\u0001[ \u0004WANG\u0001 ] 热身1v1：对准玩家按E发起挑战，或输入 !1v1 <玩家名>");
            }
        }

        foreach (var player in Utilities.GetPlayers())
        {
            if (!IsPlayerValid(player)) continue;

            int slot = (int)player.Index;
            var buttons = player.Buttons;
            _lastButtons.TryGetValue(slot, out var lastButtons);
            _lastButtons[slot] = buttons;

            // R-key = accept vote
            bool rPressed = (buttons & PlayerButtons.Reload) != 0 && (lastButtons & PlayerButtons.Reload) == 0;
            if (rPressed && VoteManager.IsTargetInVote(player.SteamID))
                VoteManager.AcceptVote(player);

            // E-key detection (challenge)
            bool ePressed = (buttons & PlayerButtons.Use) != 0 && (lastButtons & PlayerButtons.Use) == 0;

            if (ePressed && !DuelManager.IsPlayerInDuel(player))
            {
                if (!DuelManager.CanStartDuel(player))
                {
                    float cd = DuelManager.GetCooldownRemaining(player);
                    if (cd > 0)
                        player.PrintToChat($"\u0001[ \u0004WANG\u0001 ] 冷却中，请等待 {cd:F0} 秒。");
                    else if (!VoteManager.MapSupports1v1())
                        player.PrintToChat($"\u0001[ \u0004WANG\u0001 ] 当前地图不支持1v1。");
                    else if (DuelManager.IsDuelLocked)
                        player.PrintToChat($"\u0001[ \u0004WANG\u0001 ] 热身即将结束。");
                    else
                        player.PrintToChat($"\u0001[ \u0004WANG\u0001 ] 没有空闲竞技场。");
                }
                else
                {
                    var target = GetAimTarget(player);
                    if (target != null && target != player && IsPlayerValid(target))
                    {
                        if (!DuelManager.IsPlayerInDuel(target))
                            VoteManager.StartDuelVote(player, target);
                        else
                            player.PrintToChat($"\u0001[ \u0004WANG\u0001 ] {target.PlayerName} 已在决斗中。");
                    }
                }
            }
        }
    }

    // ===== Event Handlers =====

    private HookResult OnRoundEnd(EventRoundEnd e, GameEventInfo info)
    {
        DuelManager.ResetRoundFlags();
        return HookResult.Continue;
    }

    private HookResult OnRoundPrestart(EventRoundPrestart e, GameEventInfo info)
    {
        // Retry loading arenas if not loaded yet (Server.MapName may not be available during Load)
        if (ArenaManager.ArenaCount == 0)
        {
            ArenaManager.LoadArenas();
        }
        // Ensure FFA is enabled during warmup (OnTick CheckWarmupState may miss the first detection)
        WarmupManager.CheckWarmupState();
        return HookResult.Continue;
    }

    private HookResult OnPlayerDeath(EventPlayerDeath e, GameEventInfo info)
    {
        var victim = e.Userid;
        var attacker = e.Attacker;

        if (victim == null || !IsPlayerValid(victim)) return HookResult.Continue;

        // Notify warmup manager (for grenade refill tracking)
        WarmupManager.OnPlayerDeath(victim);

        if (!DuelManager.IsPlayerInDuel(victim)) return HookResult.Continue;

        var duelIndex = DuelManager.GetPlayerDuelIndex(victim);
        if (duelIndex < 0) return HookResult.Continue;

        var duel = DuelManager.GetDuel(duelIndex);
        if (duel == null || duel.State != DuelState.Active || duel.RoundEnded) return HookResult.Continue;

        // Determine winner by SteamID (most reliable)
        var p1 = DuelManager.GetDuelPlayerBySteamID(duel.Player1SteamID);
        var p2 = DuelManager.GetDuelPlayerBySteamID(duel.Player2SteamID);

        CCSPlayerController? winner = null;

        if (attacker != null && IsPlayerValid(attacker))
        {
            // Check if attacker is the opponent
            if (attacker.SteamID == duel.Player1SteamID || attacker.SteamID == duel.Player2SteamID)
            {
                winner = attacker;
            }
        }

        // If attacker wasn't the opponent (suicide, killed by third party), opponent wins
        if (winner == null || winner == victim)
        {
            winner = victim.SteamID == duel.Player1SteamID ? p2 : p1;
        }

        if (winner != null)
            DuelManager.RoundWin(duelIndex, winner);

        return HookResult.Continue;
    }

    private HookResult OnPlayerDisconnect(EventPlayerDisconnect e, GameEventInfo info)
    {
        var player = e.Userid;
        if (player == null) return HookResult.Continue;

        DuelManager.ForceEndDuel(player);
        VoteManager.CancelPlayerVotes(player);
        return HookResult.Continue;
    }

    private HookResult OnPlayerSpawn(EventPlayerSpawn e, GameEventInfo info)
    {
        var player = e.Userid;
        if (player == null || !IsPlayerValid(player)) return HookResult.Continue;

        // If player is in an active duel, skip (NextRound handles it with Respawn+Teleport)
        if (DuelManager.IsPlayerInDuel(player))
        {
            // Dueler - NextRound in DuelManager handles spawn, teleport, weapons
        }
        else if (WarmupManager.IsWarmupActive)
        {
            // Set god mode for non-duelers during warmup
            AddTimer(0.1f, () => SetGodMode(player, true));
        }

        // Give grenades on spawn during warmup
        WarmupManager.OnPlayerSpawn(player);

        return HookResult.Continue;
    }

    private HookResult OnWeaponFire(EventWeaponFire e, GameEventInfo info)
    {
        var player = e.Userid;
        if (player == null || !IsPlayerValid(player)) return HookResult.Continue;

        string weaponName = e.Weapon;
        if (weaponName.Contains("grenade") || weaponName.Contains("flashbang") || weaponName.Contains("molotov") || weaponName.Contains("incendiary"))
        {
            WarmupManager.OnGrenadeThrown(player);

            string grenadeType = weaponName.Contains("flashbang") ? "闪光弹" :
                                 weaponName.Contains("hegrenade") ? "高爆手雷" :
                                 weaponName.Contains("smoke") ? "烟雾弹" :
                                 weaponName.Contains("molotov") ? "燃烧弹" :
                                 weaponName.Contains("incendiary") ? "燃烧弹" : "投掷物";

            // Start tracking bounces for this throw
            _grenadeBounces[player.SteamID] = new GrenadeBounceData { Bounces = 0, GrenadeType = grenadeType, ThrowTime = Server.CurrentTime };
        }

        return HookResult.Continue;
    }

    private HookResult OnGrenadeBounce(EventGrenadeBounce e, GameEventInfo info)
    {
        var thrower = e.Userid;
        if (thrower != null && _grenadeBounces.TryGetValue(thrower.SteamID, out var data))
        {
            data.Bounces++;
        }
        return HookResult.Continue;
    }

    private HookResult OnHegrenadeDetonate(EventHegrenadeDetonate e, GameEventInfo info)
    {
        if (!WarmupManager.IsWarmupActive) return HookResult.Continue;
        var kv = _grenadeBounces.FirstOrDefault(g => g.Value.GrenadeType == "高爆手雷");
        if (kv.Value != null)
        {
            float flightTime = Server.CurrentTime - kv.Value.ThrowTime;
            var thrower = Utilities.GetPlayers().FirstOrDefault(p => p.SteamID == kv.Key);
            if (thrower != null && Warmup1v1Plugin.IsPlayerValid(thrower))
                thrower.PrintToChat($"\u0001[ \u0004WANG\u0001 ] 高爆手雷 飞行{flightTime:F1}s 弹了{kv.Value.Bounces}下后爆炸");
            _grenadeBounces.Remove(kv.Key);
        }
        return HookResult.Continue;
    }

    private HookResult OnSmokegrenadeDetonate(EventSmokegrenadeDetonate e, GameEventInfo info)
    {
        if (!WarmupManager.IsWarmupActive) return HookResult.Continue;
        var kv = _grenadeBounces.FirstOrDefault(g => g.Value.GrenadeType == "烟雾弹");
        if (kv.Value != null)
        {
            float flightTime = Server.CurrentTime - kv.Value.ThrowTime;
            var thrower = Utilities.GetPlayers().FirstOrDefault(p => p.SteamID == kv.Key);
            if (thrower != null && Warmup1v1Plugin.IsPlayerValid(thrower))
                thrower.PrintToChat($"\u0001[ \u0004WANG\u0001 ] 烟雾弹 飞行{flightTime:F1}s 弹了{kv.Value.Bounces}下后爆开");
            _grenadeBounces.Remove(kv.Key);
        }
        return HookResult.Continue;
    }

    private HookResult OnMolotovDetonate(EventMolotovDetonate e, GameEventInfo info)
    {
        if (!WarmupManager.IsWarmupActive) return HookResult.Continue;
        var thrower = e.Userid;
        if (thrower != null && _grenadeBounces.TryGetValue(thrower.SteamID, out var data))
        {
            float flightTime = Server.CurrentTime - data.ThrowTime;
            thrower.PrintToChat($"\u0001[ \u0004WANG\u0001 ] 燃烧弹 飞行{flightTime:F1}s 弹了{data.Bounces}下后爆开");
            _grenadeBounces.Remove(thrower.SteamID);
        }
        return HookResult.Continue;
    }

    private Dictionary<ulong, List<string>> _flashBlinds = new();

    private HookResult OnPlayerBlind(EventPlayerBlind e, GameEventInfo info)
    {
        if (!WarmupManager.IsWarmupActive) return HookResult.Continue;
        var victim = e.Userid;
        var attacker = e.Attacker;
        if (attacker != null && victim != null && _grenadeBounces.TryGetValue(attacker.SteamID, out var data) && data.GrenadeType == "闪光弹")
        {
            float duration = e.BlindDuration;
            if (duration > 0)
            {
                if (!_flashBlinds.ContainsKey(attacker.SteamID))
                    _flashBlinds[attacker.SteamID] = new List<string>();
                _flashBlinds[attacker.SteamID].Add($"致盲 {victim.PlayerName} {duration:F1}秒");
            }
        }
        return HookResult.Continue;
    }

    private HookResult OnFlashbangDetonate(EventFlashbangDetonate e, GameEventInfo info)
    {
        if (!WarmupManager.IsWarmupActive) return HookResult.Continue;
        // Collect blind results and report once
        AddTimer(0.3f, () =>
        {
            foreach (var kv in _grenadeBounces.Where(g => g.Value.GrenadeType == "闪光弹").ToList())
            {
                var thrower = Utilities.GetPlayers().FirstOrDefault(p => p.SteamID == kv.Key);
                if (thrower != null && Warmup1v1Plugin.IsPlayerValid(thrower))
                {
                    float ft = Server.CurrentTime - kv.Value.ThrowTime;
                    var bounceMsg = $"闪光弹 飞行{ft:F1}s 弹了{kv.Value.Bounces}下后爆开";
                    if (_flashBlinds.TryGetValue(kv.Key, out var blinds) && blinds.Count > 0)
                    {
                        // Split into multiple messages if too many targets
                        thrower.PrintToChat($"\u0001[ \u0004WANG\u0001 ] {bounceMsg}，共致盲{blinds.Count}人:");
                        foreach (var b in blinds)
                            thrower.PrintToChat($"\u0001[ \u0004WANG\u0001 ]   {b}");
                    }
                    else
                    {
                        thrower.PrintToChat($"\u0001[ \u0004WANG\u0001 ] {bounceMsg}，未致盲任何敌人");
                    }
                    _flashBlinds.Remove(kv.Key);
                }
                _grenadeBounces.Remove(kv.Key);
            }
        });
        return HookResult.Continue;
    }

    private HookResult OnPlayerHurt(EventPlayerHurt e, GameEventInfo info)
    {
        var attacker = e.Attacker;
        var victim = e.Userid;

        if (attacker == null || victim == null) return HookResult.Continue;
        if (victim == attacker) return HookResult.Continue;

        // If victim is in a duel but attacker is NOT, block the damage
        if (DuelManager.IsPlayerInDuel(victim) && !DuelManager.IsPlayerInDuel(attacker))
        {
            e.DmgHealth = 0;
            e.DmgArmor = 0;
            return HookResult.Changed;
        }

        return HookResult.Continue;
    }

    private HookResult OnWarmupEnd(EventWarmupEnd e, GameEventInfo info)
    {
        WarmupManager.OnWarmupEnd();
        return HookResult.Continue;
    }

    // ===== Commands =====

    private void OnCommand1v1(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null) return;

        if (!Config.AllowedMaps.Contains(WarmupManager.CurrentMap, StringComparer.OrdinalIgnoreCase))
        {
            player.PrintToChat($"\u0001[ \u0004WANG\u0001 ] 当前地图不支持1v1。");
            return;
        }

        if (!WarmupManager.IsWarmupActive)
        {
            player.PrintToChat($"\u0001[ \u0004WANG\u0001 ] 只能在热身时间发起挑战。");
            return;
        }

        if (DuelManager.IsPlayerInDuel(player))
        {
            player.PrintToChat($"\u0001[ \u0004WANG\u0001 ] 你已经在决斗中了！");
            return;
        }

        if (DuelManager.IsDuelLocked)
        {
            player.PrintToChat($"\u0001[ \u0004WANG\u0001 ] 热身即将结束，无法发起新挑战。");
            return;
        }

        if (!DuelManager.CheckCooldown(player))
        {
            float remaining = DuelManager.GetCooldownRemaining(player);
            player.PrintToChat($"\u0001[ \u0004WANG\u0001 ] 请等待 {remaining:F0} 秒后再发起挑战。");
            return;
        }

        // Check for target name in command arguments
        if (command.ArgCount > 1)
        {
            string targetName = command.ArgByIndex(1);
            var target = Utilities.GetPlayers()
                .FirstOrDefault(p => IsPlayerValid(p) && p != player
                    && !DuelManager.IsPlayerInDuel(p)
                    && p.PlayerName.Contains(targetName, StringComparison.OrdinalIgnoreCase));

            if (target == null)
            {
                player.PrintToChat($"\u0001[ \u0004WANG\u0001 ] 找不到玩家: {targetName}");
                return;
            }

            VoteManager.StartDuelVote(player, target);
            return;
        }

        // No args - show target list
        var targets = Utilities.GetPlayers()
            .Where(p => IsPlayerValid(p) && p != player && !DuelManager.IsPlayerInDuel(p))
            .ToList();

        if (targets.Count == 0)
        {
            player.PrintToChat($"\u0001[ \u0004WANG\u0001 ] 没有可挑战的玩家。");
            return;
        }

        player.PrintToChat($"\u0001[ \u0004WANG\u0001 ] 可挑战的玩家:");
        foreach (var t in targets.Take(5))
        {
            player.PrintToChat($"\u0001 - {t.PlayerName}");
        }
        player.PrintToChat($"\u0001[ \u0004WANG\u0001 ] 输入 !1v1 <玩家名> 发起挑战");
    }

    private void OnCommandSurrender(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null) return;

        if (!DuelManager.IsPlayerInDuel(player))
        {
            player.PrintToChat($"\u0001[ \u0004WANG\u0001 ] 你没有在决斗中。");
            return;
        }

        var duelIndex = DuelManager.GetPlayerDuelIndex(player);
        var duel = DuelManager.GetDuel(duelIndex);
        if (duel == null) return;

        var opponentSteamID = player.SteamID == duel.Player1SteamID ? duel.Player2SteamID : duel.Player1SteamID;
        var opponent = DuelManager.GetDuelPlayerBySteamID(opponentSteamID);

        if (opponent != null)
        {
            Server.PrintToChatAll($"\u0001[ \u0004WANG\u0001 ] {player.PlayerName} 投降了！{opponent.PlayerName} 获胜！");
            DuelManager.MatchWin(duelIndex, opponent, player);
        }
    }

    private void OnCommandTestVote(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null) return;
        VoteManager.StartTestVote(player);
    }

    private void OnCommandAccept(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null) return;
        VoteManager.AcceptVote(player);
    }

    private void OnCommandDecline(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null) return;
        VoteManager.DeclineVote(player);
    }

    private void OnCommandSetArena(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null) return;

        if (!AdminManager.PlayerHasPermissions(player, "@css/root"))
        {
            player.PrintToChat($"\u0001[ \u0004WANG\u0001 ] 你没有权限使用此指令。");
            return;
        }

        var subCmd = command.ArgCount > 1 ? command.ArgByIndex(1) : "";
        switch (subCmd.ToLower())
        {
            case "add":
            case "new":
                ArenaManager.BeginArenaEdit(player);
                break;
            case "spawn1":
                ArenaManager.SetSpawn1(player);
                break;
            case "spawn2":
                ArenaManager.SetSpawn2(player);
                break;
            case "save":
                ArenaManager.SaveArena(player);
                break;
            case "clear":
                ArenaManager.ClearArenas(player);
                break;
            default:
                player.PrintToChat($"\u0001[ \u0004WANG\u0001 ] 用法: css_setarena <add|spawn1|spawn2|save|clear>");
                break;
        }
    }

    private void OnCommandArena(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null) return;
        if (!AdminManager.PlayerHasPermissions(player, "@css/generic"))
        {
            player.PrintToChat($"\u0001[ \u0004WANG\u0001 ] 你没有权限。");
            return;
        }
        ArenaManager.ShowArenaStatus(player);
    }

    private void OnCommand1v1Status(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null) return;
        if (!AdminManager.PlayerHasPermissions(player, "@css/generic"))
        {
            player.PrintToChat($"\u0001[ \u0004WANG\u0001 ] 你没有权限。");
            return;
        }

        player.PrintToChat($"\u0001[ \u0004WANG\u0001 ] 状态:");
        player.PrintToChat($"  地图: {WarmupManager.CurrentMap}");
        player.PrintToChat($"  支持1v1: {Config.AllowedMaps.Contains(WarmupManager.CurrentMap, StringComparer.OrdinalIgnoreCase)}");
        player.PrintToChat($"  热身状态: {WarmupManager.IsWarmupActive}");
        player.PrintToChat($"  决斗锁定: {DuelManager.IsDuelLocked}");
        player.PrintToChat($"  竞技场数量: {ArenaManager.ArenaCount}");
        player.PrintToChat($"  活跃决斗: {DuelManager.ActiveDuelCount}");
    }

    // ===== Helpers =====

    public static bool IsPlayerValid(CCSPlayerController? player)
    {
        return player != null
            && player.IsValid
            && !player.IsBot
            && !player.IsHLTV;
    }

    private CCSPlayerController? GetAimTarget(CCSPlayerController player)
    {
        if (player.PlayerPawn?.Value == null) return null;

        var eyePos = player.PlayerPawn.Value.AbsOrigin ?? player.Pawn?.Value?.AbsOrigin;
        if (eyePos == null) return null;

        var eyeAngles = player.PlayerPawn.Value.EyeAngles;

        // Find the closest player in the crosshair direction
        CCSPlayerController? bestTarget = null;
        float bestScore = float.MaxValue;

        foreach (var target in Utilities.GetPlayers())
        {
            if (!IsPlayerValid(target)) continue;
            if (target == player) continue;

            var targetPos = target.PlayerPawn?.Value?.AbsOrigin;
            if (targetPos == null) continue;

            var toTarget = new Vector(
                targetPos.X - eyePos.X,
                targetPos.Y - eyePos.Y,
                targetPos.Z - eyePos.Z
            );

            float distance = (float)Math.Sqrt(toTarget.X * toTarget.X + toTarget.Y * toTarget.Y + toTarget.Z * toTarget.Z);
            if (distance > 2000) continue; // too far
            if (distance < 50) continue; // too close (same position)

            // Normalize direction vector
            float len = (float)Math.Sqrt(toTarget.X * toTarget.X + toTarget.Y * toTarget.Y);
            if (len < 0.01f) continue;
            var dirX = toTarget.X / len;
            var dirY = toTarget.Y / len;

            // Get eye direction (horizontal only)
            float yawRad = (float)(eyeAngles.Y * Math.PI / 180.0);
            var eyeDirX = (float)Math.Cos(yawRad);
            var eyeDirY = (float)Math.Sin(yawRad);

            // Dot product for angle similarity
            float dot = dirX * eyeDirX + dirY * eyeDirY;
            float angle = (float)Math.Acos(Math.Clamp(dot, -1, 1));

            // Score = angle * distance (prefer close + centered targets)
            float score = angle * 1000 + distance;

            if (angle < 0.15f && score < bestScore) // ~8.5 degrees tolerance
            {
                bestScore = score;
                bestTarget = target;
            }
        }

        return bestTarget;
    }

    public void SetGodMode(CCSPlayerController player, bool enabled)
    {
        if (player.Pawn?.Value == null) return;
        player.Pawn.Value.TakesDamage = !enabled;
    }

}
