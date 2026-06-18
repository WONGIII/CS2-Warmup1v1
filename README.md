# CS2 Warmup 1v1 热身决斗插件

CounterStrikeSharp 热身1v1决斗插件，在热身期间玩家可以对准按E发起挑战，或使用 `!1v1` 指令。

## 功能

- **E键挑战** — 热身时对准玩家按E发起1v1挑战
- **R键接受** — 收到挑战按R接受，输入 `!no` 拒绝
- **13轮武器轮换** — Glock→USP→DE→DE→M4A1-S→AK47→AK47→M4A1-S→AWP→AWP→DE→AK47→M4A1-S
- **BO7** — 先到7分获胜
- **竞技场系统** — 支持每张地图独立配置竞技场坐标
- **重生点互换** — 每轮重生点互换
- **FFA模式** — 热身期间开启友伤+队友即敌人
- **道具补给** — 热身期间自动补充手雷/闪光/烟雾/火
- **自动连跳** — 热身期间开启自动连跳
- **道具播报** — 丢出道具后显示飞行时间、弹跳次数、闪光致盲信息

## 安装

1. 将 `Warmup1v1.dll` 放入 `addons/counterstrikesharp/plugins/Warmup1v1/`
2. 启动服务器，插件会自动生成配置文件

## 配置竞技场

管理员在游戏中使用指令配置竞技场坐标：

```
!setarena add      — 开始创建新竞技场
!setarena spawn1   — 站在第一个位置执行
!setarena spawn2   — 站在第二个位置执行
!setarena save     — 保存到 arenas.json
```

竞技场配置文件自动保存在 `configs/plugins/Warmup1v1/arenas.json`。

也可以手动编写，格式：

```json
{
  "Arenas": {
    "de_dust2": [
      {
        "Spawn1": { "X": 1438.0, "Y": 1980.0, "Z": 64.0, "Pitch": 0, "Yaw": 180 },
        "Spawn2": { "X": 1438.0, "Y": 2240.0, "Z": 64.0, "Pitch": 0, "Yaw": 0 }
      }
    ]
  }
}
```

## 指令

| 指令 | 说明 |
|------|------|
| `!1v1` / `!duel` | 发起挑战 |
| `!1v1 <玩家名>` | 向指定玩家发起挑战 |
| `R 键` | 接受挑战 |
| `!no` / `!decline` | 拒绝挑战 |
| `!sr` | 投降 |
| `!setarena` | 管理员管理竞技场 |
| `!arena` | 查看竞技场状态 |

## 配置

```json
{
  "WinScore": 7,
  "VoteDuration": 10,
  "DuelCooldown": 30,
  "EnableBunnyHop": true,
  "EnableGrenadeRefill": true,
  "AllowedMaps": ["de_dust2", "de_mirage", "de_inferno", "de_nuke", "de_ancient", "de_vertigo", "de_train", "de_anubis"]
}
```

## 依赖

- CounterStrikeSharp v201+
- .NET 10.0

## 鸣谢

基于 CS:GO SourceMod 插件 yezau_warmup_solo 移植。
