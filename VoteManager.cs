using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Timers;

namespace Warmup1v1;

public class VoteData
{
    public ulong ChallengerSteamID { get; set; }
    public ulong TargetSteamID { get; set; }
    public bool InProgress { get; set; }
    public CounterStrikeSharp.API.Modules.Timers.Timer? TimeoutTimer { get; set; }
}

public class VoteManager
{
    private readonly Warmup1v1Plugin _plugin;
    private VoteData? _activeVote;
    private string _hintHtml = "";

    public bool HasActiveVote => _activeVote != null && _activeVote.InProgress;

    public VoteManager(Warmup1v1Plugin plugin) { _plugin = plugin; }
    public void Init() { _activeVote = null; }

    public bool MapSupports1v1()
    {
        return _plugin.Config.AllowedMaps.Contains(_plugin.WarmupManager.CurrentMap, StringComparer.OrdinalIgnoreCase);
    }

    public bool IsTargetInVote(ulong steamID)
    {
        return _activeVote != null && _activeVote.InProgress && _activeVote.TargetSteamID == steamID;
    }

    public void StartDuelVote(CCSPlayerController challenger, CCSPlayerController target)
    {
        CancelAllVotes();

        if (!_plugin.DuelManager.CanStartDuel(challenger) || !_plugin.DuelManager.CanStartDuel(target))
            return;

        int voteDuration = _plugin.Config.VoteDuration;

        _activeVote = new VoteData
        {
            ChallengerSteamID = challenger.SteamID,
            TargetSteamID = target.SteamID,
            InProgress = true
        };

        _hintHtml = $"<font color='#00FF00' size='+2'>{challenger.PlayerName}</font><br>向你发起1v1挑战！<br><font color='#FFFF00'>R 接受 | 输入!no拒绝</font>";

        _activeVote.TimeoutTimer = _plugin.AddTimer(voteDuration, () =>
        {
            if (_activeVote?.InProgress == true)
                HandleVoteTimeout();
        });

        challenger.PrintToChat($"\u0001[ \u0004WANG\u0001 ] 你向 {target.PlayerName} 发起了1v1挑战！({voteDuration}秒)");
    }

    public void AcceptVote(CCSPlayerController player)
    {
        if (_activeVote == null || !_activeVote.InProgress || _activeVote.TargetSteamID != player.SteamID) return;

        var vote = _activeVote;
        ClearVote();
        player.PrintToCenterHtml("");

        var challenger = Utilities.GetPlayers().FirstOrDefault(p => p.SteamID == vote.ChallengerSteamID);
        if (challenger == null || !Warmup1v1Plugin.IsPlayerValid(challenger))
        {
            player.PrintToChat($"\u0001[ \u0004WANG\u0001 ] 挑战者已离开。");
            return;
        }

        if (!_plugin.WarmupManager.IsWarmupActive)
        {
            player.PrintToChat($"\u0001[ \u0004WANG\u0001 ] 热身已结束。");
            return;
        }

        Server.PrintToChatAll($"\u0001[ \u0004WANG\u0001 ] {player.PlayerName} 接受了 {challenger.PlayerName} 的1v1挑战！");
        _plugin.DuelManager.StartDuel(challenger, player);
    }

    public void StartTestVote(CCSPlayerController target)
    {
        CancelAllVotes();
        _activeVote = new VoteData
        {
            ChallengerSteamID = 0,
            TargetSteamID = target.SteamID,
            InProgress = true
        };
        _hintHtml = "<font color='#00FF00' size='+2'>TestPlayer</font><br>向你发起1v1挑战！<br><font color='#FFFF00'>R 接受 | 输入!no拒绝</font>";
        _activeVote.TimeoutTimer = _plugin.AddTimer(10, () => { if (_activeVote?.InProgress == true) { target.PrintToCenterHtml(""); ClearVote(); } });
        target.PrintToChat($"\u0001[ \u0004WANG\u0001 ] 测试投票10秒，R接受 !no拒绝");
    }

    public void DeclineVote(CCSPlayerController player)
    {
        if (_activeVote == null || !_activeVote.InProgress || _activeVote.TargetSteamID != player.SteamID) return;

        var challengerSteamID = _activeVote.ChallengerSteamID;
        ClearVote();
        player.PrintToCenterHtml("");

        var challenger = Utilities.GetPlayers().FirstOrDefault(p => p.SteamID == challengerSteamID);
        if (challenger != null && Warmup1v1Plugin.IsPlayerValid(challenger))
            challenger.PrintToChat($"\u0001[ \u0004WANG\u0001 ] {player.PlayerName} 拒绝了你的1v1挑战。");

        player.PrintToChat($"\u0001[ \u0004WANG\u0001 ] 你拒绝了1v1挑战。");
    }

    private void HandleVoteTimeout()
    {
        if (_activeVote == null || !_activeVote.InProgress) return;
        var vote = _activeVote;
        ClearVote();

        var challenger = Utilities.GetPlayers().FirstOrDefault(p => p.SteamID == vote.ChallengerSteamID);
        var target = Utilities.GetPlayers().FirstOrDefault(p => p.SteamID == vote.TargetSteamID);

        target?.PrintToCenterHtml("");
        challenger?.PrintToChat($"\u0001[ \u0004WANG\u0001 ] 挑战超时，对方未响应。");
        target?.PrintToChat($"\u0001[ \u0004WANG\u0001 ] 挑战已超时。");
    }

    public void CancelPlayerVotes(CCSPlayerController player)
    {
        if (_activeVote == null || !_activeVote.InProgress) return;
        if (_activeVote.ChallengerSteamID == player.SteamID || _activeVote.TargetSteamID == player.SteamID)
            CancelAllVotes();
    }

    public void TickHint()
    {
        if (_activeVote == null || !_activeVote.InProgress || string.IsNullOrEmpty(_hintHtml)) return;
        var t = Utilities.GetPlayers().FirstOrDefault(p => p.SteamID == _activeVote.TargetSteamID);
        if (t != null && Warmup1v1Plugin.IsPlayerValid(t))
            t.PrintToCenterHtml(_hintHtml);
    }

    public void CancelAllVotes()
    {
        ClearVote();
    }

    private void ClearVote()
    {
        _hintHtml = "";
        _activeVote?.TimeoutTimer?.Kill();
        _activeVote = null;
    }
}
