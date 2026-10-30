# GameData —— 运行时 JSON 母本（不进 Unity Assets）

- 只由导表工具刷新，**不手工编辑**；出整合包时整体复制成安装包的 `Config/`。
- `manifest.json`：版本号 + 补丁文件清单（version / minAppVersion / files）。**规划中，尚未存在**（M6 出包期）。
- `cards.json`：全量卡表（手写/技能编辑器导出）；`decks.json` / `enemies.json` / `parts.json` / `sleeves.json`（M5 起）：见下各节。`levels.json`：规划中（M6 关卡结构，enemies.json 是其前身）。
- `art/`：热更散图（从 CardGame_Arts/deliver/ 挑进来的，运行时按文件名懒加载，Mods 优先）。

运行时查找顺序：`Game/Mods/ → Game/Config/ → 代码默认值`。
补丁永远不改这里：补丁落朋友的 `Mods/`，本目录是"唯一最新全集"。

## cards.json 形状规范（M2·T2 定稿；M5·T1 补 triggers）

根为卡对象数组，一元素一卡、**扁平无嵌套卡体**。命名与效果载荷遵循序列化层格式四决策（camelCase / 枚举字符串拒数字 / null 省略 / 默认照写，见 `Battle/Serialization/SkillJson.cs` 头注释）。

| 字段 | 类型 | 说明 |
|---|---|---|
| `kind` | `"monster"` / `"effect"` | 卡种判别，全小写字面量 |
| `id` | int | 表行 id，正整数；分段约定：1xxx 效果卡 / 2xxx 怪兽卡（敌方卡同段）。唯一性巡检在加载入口（T3 CardCatalog）；id 同时注入 `BattleCard.Id`（归因/复制卡的卡身份，M5·T1） |
| `name` | string | 卡名，非空 |
| `cost` | int | 能量费；0 费合法（如敌方卡） |
| `attack` / `maxHp` | int | 怪兽卡专属；maxHp 必须为正；attack 0 合法（"墙"） |
| `effects` | EffectGroupDef[] | 效果卡载荷；载荷词汇 = 技能系统 Data 层（op 判别见 T1，`ModOp` 字段键名为 `mod`） |
| `triggers` | TriggerDef[] | 效果卡可选（M5·T1 起）：打出即注册的触发器（"结界"形态，挂玩家领袖随本场生灭）；**effects 与 triggers 至少其一非空**（纯触发卡合法） |

单卡校验（反序列化即抛，不带病运行）：未知 kind / 非正 id / 空 name / maxHp≤0 / effects 与 triggers 双空 / 触发器坏标签（报错带 `triggers[i].eventTag` 数据路径）。
卡间引用校验（CardCatalog 二遍巡检）：`AddCardToHand.cardId` 与 `EventSourceCard` 条件的 `cardId` 必须指向本表存在的卡。
时长词汇提示：`duration: "Rounds"` 以"方回合"计（一整圈=2 tick）——扛过一轮敌方回合用 2（防御卡 1004 先例）；`ThisPhase` 在相位结束即清，扛不到敌方回合（详见 CardGame-Doc/M5-API缺口清单.md）。
新条件词汇（M5·T1）：`{"kind":"EventSourceCard","cardId":N}`（事件归因卡判定）/ `{"kind":"LastDamageKilled"}`（上一伤害击杀——击杀奖励的跨组门控）。

## decks.json（M5·T3：组牌编排）

`[{ "id": 1, "name": "演示牌组", "cards": [{ "id": 2001, "count": 1 }, …] }]`——牌组数组；**条目序展开即牌库顺序**（count 连续重复；抽牌顺序敏感，保序）。校验：id 唯一、cards 非空、卡 id 引用存在。牌组 1（演示牌组）的展开序与退役 M1 硬编码序逐值等价——同种子零漂移锚定依赖它，**勿重排**。牌组构筑 UI 归 M6。

## enemies.json（M5·T3：敌方阵容，M6 levels.json 前身）

`[{ "id": 3001, "name": "哥布林前哨", "leaderHp": 22, "summons": [{ "id": 2003, "count": 6 }] }]`——差异只做数据面（行为差异归 M6 意图系统）。校验：leaderHp 为正、召唤条目必须引用**怪兽卡** id。对局选择：`enemies.All[restartCount % Count]` 顺序轮换。

## parts.json / sleeves.json（M5·T6：融合数据；编辑器导出物）

- parts.json：`PartCardConfig[]`（id/name/slotType/cost/attack/maxHp/effects/triggers/auras/grantTags/consumable）——槽型×内容矩阵由 FusionCatalog 加载期预检 + MonsterSleeve.Install 装配期双查。
- sleeves.json：`MonsterCardConfig[]`（id/name/slots/cost{multiplier,flat}/statMultiplier）。
- **纪律**：这两个文件是技能编辑器的**导出产物**——不手改，回编辑器（Tools/技能编辑器）改再导出；作者稿=`Assets/Editor/SkillCards/Parts/` 下每卡一 .asset。

> cards/decks/enemies 手工维护（值即源头，新增卡走《新卡制作SOP》）；parts/sleeves 由技能编辑器维护。现 21 张卡 = M1 演示 6 张 + M2 验证 4 张 + 剧毒 + M5 量产 10 张（1008~1017）。
