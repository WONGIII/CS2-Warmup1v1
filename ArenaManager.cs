using System.Text.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace Warmup1v1;

public class ArenaData
{
    public SpawnPoint Spawn1 { get; set; } = new();
    public SpawnPoint Spawn2 { get; set; } = new();
    public bool InUse { get; set; }
}

public class SpawnPoint
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float Pitch { get; set; }
    public float Yaw { get; set; }
}

public class ArenaConfig
{
    public Dictionary<string, List<ArenaData>> Arenas { get; set; } = new();
}

public class ArenaManager
{
    private readonly Warmup1v1Plugin _plugin;
    private readonly ArenaData[] _arenas;
    private ArenaConfig _config = new();

    // Arena editing state
    private ArenaData? _editingArena;
    private CCSPlayerController? _editingPlayer;

    public int ArenaCount { get; private set; }

    private const int MaxArenas = 10;

    public ArenaManager(Warmup1v1Plugin plugin)
    {
        _plugin = plugin;
        _arenas = new ArenaData[MaxArenas];
        for (int i = 0; i < MaxArenas; i++)
            _arenas[i] = new ArenaData();
    }

    private string GetArenasPath()
    {
        return Path.Combine(_plugin.ModuleDirectory, _plugin.Config.ArenaConfigPath);
    }

    public void LoadArenas()
    {
        var mapName = Server.MapName;
        var configPath = GetArenasPath();
        _plugin.Logger.LogInformation($"[Warmup1v1] Loading arenas from: {configPath} (exists={File.Exists(configPath)})");

        try
        {
            if (File.Exists(configPath))
            {
                var json = File.ReadAllText(configPath);
                _plugin.Logger.LogInformation($"[Warmup1v1] Read {json.Length} bytes");
                _config = JsonSerializer.Deserialize<ArenaConfig>(json) ?? new ArenaConfig();
            }
            else
            {
                _plugin.Logger.LogWarning($"[Warmup1v1] arenas.json not found at {configPath}, creating default");
                _config = new ArenaConfig();
                var dir = Path.GetDirectoryName(configPath);
                if (dir != null && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                SaveConfig(configPath);
            }

            if (_config.Arenas.TryGetValue(mapName, out var mapArenas))
            {
                ArenaCount = Math.Min(mapArenas.Count, MaxArenas);
                for (int i = 0; i < ArenaCount; i++)
                {
                    _arenas[i].Spawn1 = mapArenas[i].Spawn1;
                    _arenas[i].Spawn2 = mapArenas[i].Spawn2;
                    _arenas[i].InUse = false;
                }
                _plugin.Logger.LogInformation($"[Warmup1v1] Loaded {ArenaCount} arenas for {mapName}");
            }
            else
            {
                ArenaCount = 0;
                _plugin.Logger.LogInformation($"[Warmup1v1] No arenas configured for {mapName}");
            }
        }
        catch (Exception ex)
        {
            _plugin.Logger.LogError($"[Warmup1v1] Failed to load arenas: {ex.Message}");
            ArenaCount = 0;
        }
    }

    public int GetFreeArena()
    {
        var free = new List<int>();
        for (int i = 0; i < ArenaCount; i++)
            if (!_arenas[i].InUse)
                free.Add(i);
        if (free.Count == 0) return -1;
        return free[Random.Shared.Next(free.Count)];
    }

    public void MarkArenaInUse(int index, bool inUse)
    {
        if (index >= 0 && index < MaxArenas)
            _arenas[index].InUse = inUse;
    }

    public void TeleportPlayerToArena(CCSPlayerController player, int arenaIndex, bool isSpawn1)
    {
        if (arenaIndex < 0 || arenaIndex >= ArenaCount) return;

        var arena = _arenas[arenaIndex];
        var spawn = isSpawn1 ? arena.Spawn1 : arena.Spawn2;

        var pos = new Vector(spawn.X, spawn.Y, spawn.Z);
        var angles = new QAngle(spawn.Pitch, spawn.Yaw, 0);

        player.PlayerPawn?.Value?.Teleport(pos, angles, new Vector(0, 0, 0));
    }

    // ===== Admin Commands =====

    public void BeginArenaEdit(CCSPlayerController player)
    {
        _editingArena = new ArenaData();
        _editingPlayer = player;
        player.PrintToChat($"\u0001[ \u0004WANG\u0001 ] 开始创建新竞技场。请走到 spawn1 位置后输入 !setarena spawn1");
    }

    public void SetSpawn1(CCSPlayerController player)
    {
        if (_editingArena == null || _editingPlayer != player)
        {
            player.PrintToChat($"\u0001[ \u0004WANG\u0001 ] 请先输入 !setarena add");
            return;
        }

        SaveCurrentPosition(player, _editingArena.Spawn1);
        player.PrintToChat($"\u0001[ \u0004WANG\u0001 ] Spawn1 已设置。请走到 spawn2 位置后输入 !setarena spawn2");
    }

    public void SetSpawn2(CCSPlayerController player)
    {
        if (_editingArena == null || _editingPlayer != player)
        {
            player.PrintToChat($"\u0001[ \u0004WANG\u0001 ] 请先输入 !setarena add");
            return;
        }

        if (_editingArena.Spawn1.X == 0 && _editingArena.Spawn1.Y == 0 && _editingArena.Spawn1.Z == 0)
        {
            player.PrintToChat($"\u0001[ \u0004WANG\u0001 ] 请先设置 spawn1！");
            return;
        }

        SaveCurrentPosition(player, _editingArena.Spawn2);
        player.PrintToChat($"\u0001[ \u0004WANG\u0001 ] Spawn2 已设置。输入 !setarena save 保存。");
    }

    public void SaveArena(CCSPlayerController player)
    {
        if (_editingArena == null || _editingPlayer != player)
        {
            player.PrintToChat($"\u0001[ \u0004WANG\u0001 ] 没有正在编辑的竞技场。");
            return;
        }

        if (_editingArena.Spawn1.X == 0 && _editingArena.Spawn1.Y == 0 && _editingArena.Spawn1.Z == 0)
        {
            player.PrintToChat($"\u0001[ \u0004WANG\u0001 ] 请先设置 spawn1 和 spawn2。");
            return;
        }
        if (_editingArena.Spawn2.X == 0 && _editingArena.Spawn2.Y == 0 && _editingArena.Spawn2.Z == 0)
        {
            player.PrintToChat($"\u0001[ \u0004WANG\u0001 ] 请先设置 spawn2。");
            return;
        }

        if (ArenaCount >= MaxArenas)
        {
            player.PrintToChat($"\u0001[ \u0004WANG\u0001 ] 已达到最大竞技场数量 ({MaxArenas})。");
            return;
        }

        _arenas[ArenaCount] = _editingArena;
        ArenaCount++;

        SaveArenasToFile();
        player.PrintToChat($"\u0001[ \u0004WANG\u0001 ] 竞技场 #{ArenaCount} 已保存！当前共 {ArenaCount} 个竞技场。");

        _editingArena = null;
        _editingPlayer = null;
    }

    public void ClearArenas(CCSPlayerController player)
    {
        ArenaCount = 0;
        for (int i = 0; i < MaxArenas; i++)
        {
            _arenas[i] = new ArenaData();
        }
        SaveArenasToFile();
        player.PrintToChat($"\u0001[ \u0004WANG\u0001 ] 所有竞技场已清除。");
    }

    public void ShowArenaStatus(CCSPlayerController player)
    {
        var mapName = Server.MapName;
        player.PrintToChat($"\u0001[ \u0004WANG\u0001 ] 地图: {mapName} | 竞技场数: {ArenaCount}");
        for (int i = 0; i < ArenaCount; i++)
        {
            string status = _arenas[i].InUse ? "使用中" : "空闲";
            player.PrintToChat($"  Arena #{i + 1}: {status}");
        }
    }

    // ===== Helpers =====

    private void SaveCurrentPosition(CCSPlayerController player, SpawnPoint spawn)
    {
        var absOrigin = player.Pawn?.Value?.AbsOrigin;
        var absRotation = player.Pawn?.Value?.AbsRotation;

        if (absOrigin != null)
        {
            spawn.X = absOrigin.X;
            spawn.Y = absOrigin.Y;
            spawn.Z = absOrigin.Z;
        }
        if (absRotation != null)
        {
            spawn.Pitch = absRotation.X;
            spawn.Yaw = absRotation.Y;
        }
    }

    private void SaveArenasToFile()
    {
        var mapName = Server.MapName;
        var configPath = GetArenasPath();

        var mapArenas = new List<ArenaData>();
        for (int i = 0; i < ArenaCount; i++)
        {
            mapArenas.Add(new ArenaData
            {
                Spawn1 = new SpawnPoint
                {
                    X = _arenas[i].Spawn1.X,
                    Y = _arenas[i].Spawn1.Y,
                    Z = _arenas[i].Spawn1.Z,
                    Pitch = _arenas[i].Spawn1.Pitch,
                    Yaw = _arenas[i].Spawn1.Yaw
                },
                Spawn2 = new SpawnPoint
                {
                    X = _arenas[i].Spawn2.X,
                    Y = _arenas[i].Spawn2.Y,
                    Z = _arenas[i].Spawn2.Z,
                    Pitch = _arenas[i].Spawn2.Pitch,
                    Yaw = _arenas[i].Spawn2.Yaw
                }
            });
        }

        _config.Arenas[mapName] = mapArenas;
        SaveConfig(configPath);
    }

    private void SaveConfig(string path)
    {
        try
        {
            var json = JsonSerializer.Serialize(_config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            _plugin.Logger.LogError($"[Warmup1v1] Failed to save arenas: {ex.Message}");
        }
    }
}
