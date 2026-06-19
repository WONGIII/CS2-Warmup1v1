using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace Warmup1v1;

public enum DuelState { None, Active }

public class DuelInfo
{
    public ulong Player1SteamID { get; set; }
    public ulong Player2SteamID { get; set; }
    public int Score1 { get; set; }
    public int Score2 { get; set; }
    public int CurrentRound { get; set; } = 1;
    public int ArenaIndex { get; set; } = -1;
    public DuelState State { get; set; } = DuelState.None;
    public bool RoundEnded { get; set; }
    public bool SpawnSwapped { get; set; }
}

public class PlayerDuelData
{
    public int DuelIndex { get; set; } = -1;
    public bool InDuel { get; set; }
    public float LastDuelTime { get; set; }
    public Vector? Origin { get; set; }
    public QAngle? Angles { get; set; }
}

public class DuelManager
{
    private readonly Warmup1v1Plugin _plugin;
    private readonly DuelInfo[] _duels;
    private readonly Dictionary<ulong, PlayerDuelData> _playerData;
    public bool IsDuelLocked { get; set; }
    public int ActiveDuelCount => _duels.Count(d => d.State == DuelState.Active);
    private const int MaxDuels = 5;

    public DuelManager(Warmup1v1Plugin plugin)
    {
        _plugin = plugin;
        _duels = new DuelInfo[MaxDuels];
        for (int i = 0; i < MaxDuels; i++) _duels[i] = new DuelInfo();
        _playerData = new Dictionary<ulong, PlayerDuelData>();
    }

    public void Init()
    {
        for (int i = 0; i < MaxDuels; i++) _duels[i] = new DuelInfo();
        _playerData.Clear();
    }

    private PlayerDuelData GetOrCreate(CCSPlayerController p)
    {
        var sid = p.SteamID;
        if (!_playerData.TryGetValue(sid, out var d)) { d = new PlayerDuelData(); _playerData[sid] = d; }
        return d;
    }

    public bool IsPlayerInDuel(CCSPlayerController p) => GetOrCreate(p).InDuel;
    public int GetPlayerDuelIndex(CCSPlayerController p) => GetOrCreate(p).DuelIndex;
    public float GetCooldownRemaining(CCSPlayerController p) => Math.Max(0, _plugin.Config.DuelCooldown - (Server.CurrentTime - GetOrCreate(p).LastDuelTime));
    public bool CheckCooldown(CCSPlayerController p) => GetCooldownRemaining(p) <= 0;

    public bool CanStartDuel(CCSPlayerController p)
    {
        if (!_plugin.WarmupManager.MapSupports1v1()) return false;
        if (!_plugin.WarmupManager.IsWarmupActive) return false;
        if (IsDuelLocked) return false;
        if (IsPlayerInDuel(p)) return false;
        if (!CheckCooldown(p)) return false;
        if (_plugin.ArenaManager.GetFreeArena() < 0) return false;
        return true;
    }

    public DuelInfo? GetDuel(int i) => (i >= 0 && i < MaxDuels) ? _duels[i] : null;

    public CCSPlayerController? GetDuelPlayerBySteamID(ulong sid)
    {
        return Utilities.GetPlayers().FirstOrDefault(p => p.SteamID == sid && Warmup1v1Plugin.IsPlayerValid(p));
    }

    private int FindFreeSlot()
    {
        for (int i = 0; i < MaxDuels; i++) if (_duels[i].State == DuelState.None) return i;
        return -1;
    }

    public bool StartDuel(CCSPlayerController c, CCSPlayerController t)
    {
        int slot = FindFreeSlot();
        if (slot < 0) return false;
        int ai = _plugin.ArenaManager.GetFreeArena();
        if (ai < 0) return false;

        var d = _duels[slot];
        d.Player1SteamID = c.SteamID; d.Player2SteamID = t.SteamID;
        d.Score1 = 0; d.Score2 = 0; d.CurrentRound = 1;
        d.ArenaIndex = ai; d.State = DuelState.Active; d.RoundEnded = false; d.SpawnSwapped = false;

        SavePos(c); SavePos(t);
        _plugin.ArenaManager.MarkArenaInUse(ai, true);

        GetOrCreate(c).DuelIndex = slot; GetOrCreate(c).InDuel = true;
        GetOrCreate(t).DuelIndex = slot; GetOrCreate(t).InDuel = true;

        _plugin.ArenaManager.TeleportPlayerToArena(c, ai, true);
        _plugin.ArenaManager.TeleportPlayerToArena(t, ai, false);

        if (c.InGameMoneyServices != null) c.InGameMoneyServices.Account = 0;
        if (t.InGameMoneyServices != null) t.InGameMoneyServices.Account = 0;

        _plugin.WarmupManager.SetPlayerInDuel(c, true); _plugin.WarmupManager.SetPlayerInDuel(t, true);

        int dlI = slot;
        _plugin.AddTimer(0.5f, () =>
        {
            var dd = GetDuel(dlI);
            if (dd?.State != DuelState.Active) return;
            var p1 = GetDuelPlayerBySteamID(dd.Player1SteamID);
            var p2 = GetDuelPlayerBySteamID(dd.Player2SteamID);
            if (p1 == null || p2 == null) return;

            if (p1.Pawn?.Value != null) p1.Pawn.Value.TakesDamage = true;
            if (p2.Pawn?.Value != null) p2.Pawn.Value.TakesDamage = true;

            bool fa1 = _plugin.WeaponManager.NeedsFullArmor(dd.CurrentRound);
            _plugin.WeaponManager.GiveDuelWeapons(p1, p2, dd.CurrentRound, fa1);
        });

        Server.PrintToChatAll($"\u0001[ \u0004WANG\u0001 ] {c.PlayerName} vs {t.PlayerName} - 1v1开始! [竞技场#{ai + 1}]");
        return true;
    }

    public void RoundWin(int duelIndex, CCSPlayerController winner)
    {
        var d = GetDuel(duelIndex);
        if (d == null || d.State != DuelState.Active || d.RoundEnded) return;

        d.RoundEnded = true;
        GetOrCreate(winner).LastDuelTime = Server.CurrentTime;

        ulong loserSid = winner.SteamID == d.Player1SteamID ? d.Player2SteamID : d.Player1SteamID;
        var loser = GetDuelPlayerBySteamID(loserSid);
        if (loser != null) GetOrCreate(loser).LastDuelTime = Server.CurrentTime;

        if (winner.SteamID == d.Player1SteamID) d.Score1++; else d.Score2++;

        var p1 = GetDuelPlayerBySteamID(d.Player1SteamID);
        var p2 = GetDuelPlayerBySteamID(d.Player2SteamID);
        string wn = _plugin.WeaponManager.GetWeaponName(d.CurrentRound);
        Server.PrintToChatAll($"\u0001[ \u0004WANG\u0001 ] {p1?.PlayerName} [{d.Score1}-{d.Score2}] {p2?.PlayerName}  [{wn}]");

        int ws = _plugin.Config.WinScore;
        if (d.Score1 >= ws) { if (p1 != null && p2 != null) MatchWin(duelIndex, p1, p2); return; }
        if (d.Score2 >= ws) { if (p1 != null && p2 != null) MatchWin(duelIndex, p2, p1); return; }

        d.CurrentRound++;
        d.SpawnSwapped = !d.SpawnSwapped;
        int dlI = duelIndex;
        _plugin.AddTimer(2.0f, () => NextRound(dlI));
    }

    public void MatchWin(int duelIndex, CCSPlayerController winner, CCSPlayerController loser)
    {
        var d = GetDuel(duelIndex);
        if (d == null) return;
        Server.PrintToChatAll($"\u0001[ \u0004WANG\u0001 ] {winner.PlayerName} 赢得1v1! {d.Score1}-{d.Score2}");
        winner.PrintToCenter($"Victory! {d.Score1}-{d.Score2}");
        loser.PrintToCenter($"Defeat! {d.Score1}-{d.Score2}");
        int dlI = duelIndex;
        _plugin.AddTimer(2.0f, () => EndDuel(dlI));
    }

    private void NextRound(int duelIndex)
    {
        var d = GetDuel(duelIndex);
        if (d == null || d.State != DuelState.Active) return;

        var p1 = GetDuelPlayerBySteamID(d.Player1SteamID);
        var p2 = GetDuelPlayerBySteamID(d.Player2SteamID);
        if (p1 == null || p2 == null) { EndDuel(duelIndex); return; }

        if (!p1.PawnIsAlive) p1.Respawn();
        if (!p2.PawnIsAlive) p2.Respawn();

        bool p1Spawn1 = !d.SpawnSwapped;
        _plugin.ArenaManager.TeleportPlayerToArena(p1, d.ArenaIndex, p1Spawn1);
        _plugin.ArenaManager.TeleportPlayerToArena(p2, d.ArenaIndex, !p1Spawn1);

        if (p1.InGameMoneyServices != null) p1.InGameMoneyServices.Account = 0;
        if (p2.InGameMoneyServices != null) p2.InGameMoneyServices.Account = 0;

        d.RoundEnded = false;

        if (p1.Pawn?.Value != null) { p1.Pawn.Value.Health = 100; p1.Pawn.Value.TakesDamage = true; }
        if (p2.Pawn?.Value != null) { p2.Pawn.Value.Health = 100; p2.Pawn.Value.TakesDamage = true; }

        bool fa2 = _plugin.WeaponManager.NeedsFullArmor(d.CurrentRound);
        _plugin.WeaponManager.GiveDuelWeapons(p1, p2, d.CurrentRound, fa2);

        string wn = _plugin.WeaponManager.GetWeaponName(d.CurrentRound);
        p1.PrintToCenter($"R{d.CurrentRound} [{d.Score1}-{d.Score2}] {wn}");
        p2.PrintToCenter($"R{d.CurrentRound} [{d.Score1}-{d.Score2}] {wn}");
    }

    public void EndDuel(int duelIndex)
    {
        var d = GetDuel(duelIndex);
        if (d == null) return;

        var p1 = GetDuelPlayerBySteamID(d.Player1SteamID);
        var p2 = GetDuelPlayerBySteamID(d.Player2SteamID);
        if (p1 != null) Restore(p1);
        if (p2 != null) Restore(p2);

        if (d.ArenaIndex >= 0) _plugin.ArenaManager.MarkArenaInUse(d.ArenaIndex, false);
        d.Player1SteamID = 0; d.Player2SteamID = 0; d.State = DuelState.None;
    }

    public void ForceEndDuel(CCSPlayerController player)
    {
        int di = GetOrCreate(player).DuelIndex;
        if (di < 0) return;
        var d = GetDuel(di);
        if (d == null) return;
        ulong os = player.SteamID == d.Player1SteamID ? d.Player2SteamID : d.Player1SteamID;
        var o = GetDuelPlayerBySteamID(os);
        if (o != null) o.PrintToChat($"\u0001[ \u0004WANG\u0001 ] 对手已离开。");
        EndDuel(di);
    }

    public void ForceEndAllDuels()
    {
        for (int i = 0; i < MaxDuels; i++)
            if (_duels[i].State == DuelState.Active) { Server.PrintToChatAll($"\u0001[ \u0004WANG\u0001 ] 热身结束，决斗终止。"); EndDuel(i); }
    }

    public void ResetRoundFlags()
    {
        for (int i = 0; i < MaxDuels; i++) if (_duels[i].State == DuelState.Active) _duels[i].RoundEnded = false;
    }

    // ===== Helpers =====

    private void SavePos(CCSPlayerController p)
    {
        var d = GetOrCreate(p);
        if (p.Pawn?.Value?.AbsOrigin != null) d.Origin = new Vector(p.Pawn.Value.AbsOrigin.X, p.Pawn.Value.AbsOrigin.Y, p.Pawn.Value.AbsOrigin.Z);
        if (p.Pawn?.Value?.AbsRotation != null) d.Angles = new QAngle(p.Pawn.Value.AbsRotation.X, p.Pawn.Value.AbsRotation.Y, p.Pawn.Value.AbsRotation.Z);
    }

    private void Restore(CCSPlayerController p)
    {
        var d = GetOrCreate(p);
        d.DuelIndex = -1; d.InDuel = false; d.LastDuelTime = Server.CurrentTime;

        _plugin.WarmupManager.SetPlayerInDuel(p, false);

        p.CommitSuicide(false, true);
    }
}
