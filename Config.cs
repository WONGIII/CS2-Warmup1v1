using CounterStrikeSharp.API.Core;

namespace Warmup1v1;

public class Warmup1v1Config : BasePluginConfig
{
    public override int Version { get; set; } = 1;

    public int WinScore { get; set; } = 7;
    public int VoteDuration { get; set; } = 10;
    public float DuelCooldown { get; set; } = 30.0f;
    public bool EnableBunnyHop { get; set; } = true;
    public bool EnableGrenadeRefill { get; set; } = true;
    public float GrenadeRefillCooldown { get; set; } = 10.0f;
    public int MaxDuels { get; set; } = 5;
    public int MaxArenas { get; set; } = 10;

    public List<string> AllowedMaps { get; set; } = new()
    {
        "de_dust2",
        "de_mirage",
        "de_inferno",
        "de_nuke",
        "de_ancient",
        "de_vertigo",
        "de_train",
        "de_anubis"
    };

    public string ArenaConfigPath { get; set; } = "arenas.json";
}
