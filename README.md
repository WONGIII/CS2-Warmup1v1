# CS2 Warmup 1v1 热身单挑插件

基于 Sprite 的 CS:GO 热身单挑插件思路，为 CS2 + CounterStrikeSharp 全新编写的热身1v1决斗插件。

原帖：[CSGO-类平台-热身修改，热身改为跑图并给予玩家道具，热身期间按E发起单挑](https://bbs.csgocn.net/thread-1175.htm)

## 功能

- **E键挑战** — 热身时对准玩家按E发起1v1挑战
- **R键接受** — 收到挑战按R接受，输入 `!no` 拒绝
- **HTML提示** — 收到挑战时屏幕中央显示绿色大字提示
- **13轮武器轮换** — Glock→USP→DE→DE→M4A1-S→AK47→AK47→M4A1-S→AWP→AWP→DE→AK47→M4A1-S
- **BO7抢7** — 先到7分获胜
- **竞技场系统** — 支持每张地图独立配置竞技场坐标（JSON配置 + 游戏内管理员指令）
- **重生点互换** — 每轮重生点互换
- **多人同时决斗** — 最多5组同时进行
- **FFA模式** — 热身期间开启友伤+队友即敌人
- **道具补给** — 热身期间自动补充手雷/闪光/烟雾/火，丢出后自动补回
- **自动连跳** — 热身期间开启自动连跳
- **道具播报** — 丢出道具后显示飞行时间、弹跳次数、闪光致盲信息（仅热身期间）
- **无敌保护** — 热身时非决斗者无敌，决斗中解除，结束后恢复
- **定时广播** — 每15秒自动广播1v1玩法提示

## 安装

1. 将 `Warmup1v1.dll` 放入 `addons/counterstrikesharp/plugins/Warmup1v1/`
2. 重启服务器或热重载：`css_plugins load "Warmup 1v1"`
3. 插件会自动在 `configs/plugins/Warmup1v1/` 下生成配置文件 `Warmup1v1.json`

## 配置文件位置

| 文件 | 路径 |
|------|------|
| 插件配置 | `addons/counterstrikesharp/configs/plugins/Warmup1v1/Warmup1v1.json` |
| 竞技场配置 | `addons/counterstrikesharp/configs/plugins/Warmup1v1/arenas.json` |

## 配置竞技场

### 方法一：管理员指令（推荐）

```
!setarena add      — 开始创建新竞技场
!setarena spawn1   — 走到第一个出生点执行
!setarena spawn2   — 走到第二个出生点执行
!setarena save     — 保存到 arenas.json
```

### 方法二：手动编写 arenas.json

```json
{
  "Arenas": {
    "de_dust2": [
      {
        "Spawn1": { "X": 1438.0, "Y": 1980.0, "Z": 64.0, "Pitch": 0, "Yaw": 180 },
        "Spawn2": { "X": 1438.0, "Y": 2240.0, "Z": 64.0, "Pitch": 0, "Yaw": 0 }
      },
      {
        "Spawn1": { "X": 534.0, "Y": 2112.0, "Z": 64.0, "Pitch": 0, "Yaw": -90 },
        "Spawn2": { "X": 534.0, "Y": 2368.0, "Z": 64.0, "Pitch": 0, "Yaw": 90 }
      }
    ],
    "de_mirage": [ ... ]
  }
}
```

最多10个竞技场，每个地图独立配置。

## 玩家指令

| 指令 | 说明 |
|------|------|
| `!1v1` / `!duel` | 列出可挑战玩家 |
| `!1v1 <玩家名>` | 向指定玩家发起挑战 |
| `R 键` | 接受挑战 |
| `!no` / `!decline` | 拒绝挑战 |
| `!sr` | 投降认输 |

## 管理员指令

| 指令 | 说明 |
|------|------|
| `!setarena` | 管理竞技场坐标 |
| `!arena` | 查看当前地图竞技场状态 |
| `!1v1status` | 查看插件运行状态 |

## 插件配置

```json
{
  "WinScore": 7,
  "VoteDuration": 10,
  "DuelCooldown": 30,
  "EnableBunnyHop": true,
  "EnableGrenadeRefill": true,
  "GrenadeRefillCooldown": 10,
  "MaxDuels": 5,
  "MaxArenas": 10,
  "AllowedMaps": [
    "de_dust2", "de_mirage", "de_inferno", "de_nuke",
    "de_ancient", "de_vertigo", "de_train", "de_anubis"
  ]
}
```

| 配置项 | 默认 | 说明 |
|--------|------|------|
| WinScore | 7 | 获胜分数 |
| VoteDuration | 10 | 挑战有效时长（秒） |
| DuelCooldown | 30 | 决斗冷却时间（秒） |
| EnableBunnyHop | true | 热身自动连跳 |
| EnableGrenadeRefill | true | 热身道具补给 |
| AllowedMaps | 8张 | 允许1v1的地图列表 |

## 依赖

- CounterStrikeSharp v201+
- .NET 10.0
- CS2 服务器

## 编译

```bash
dotnet restore
dotnet build
```

dll 输出在 `bin/Debug/net10.0/Warmup1v1.dll`

## 参考

原 CS:GO SourceMod 插件由 Sprite 发布：[类平台-热身修改](https://bbs.csgocn.net/thread-1175.htm)

本项目为 CS2 平台全新编写，参考了原 SourceMod 插件的设计思路和功能。
