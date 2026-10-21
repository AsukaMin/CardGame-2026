# GameData —— 运行时 JSON 母本（不进 Unity Assets）

- 只由导表工具刷新，**不手工编辑**；出整合包时整体复制成安装包的 `Config/`。
- `manifest.json`：版本号 + 补丁文件清单（version / minAppVersion / files）。
- `cards.json`、`levels.json`：全量表快照。
- `art/`：热更散图（从 CardGame_Arts/deliver/ 挑进来的，运行时按文件名懒加载，Mods 优先）。

运行时查找顺序：`Game/Mods/ → Game/Config/ → 代码默认值`。
补丁永远不改这里：补丁落朋友的 `Mods/`，本目录是"唯一最新全集"。

## cards.json 形状规范（M2·T2 定稿）

根为卡对象数组，一元素一卡、**扁平无嵌套卡体**。命名与效果载荷遵循序列化层格式四决策（camelCase / 枚举字符串拒数字 / null 省略 / 默认照写，见 `Battle/Serialization/SkillJson.cs` 头注释）。

| 字段 | 类型 | 说明 |
|---|---|---|
| `kind` | `"monster"` / `"effect"` | 卡种判别，全小写字面量 |
| `id` | int | 表行 id，正整数；分段约定：1xxx 效果卡 / 2xxx 怪兽卡（敌方卡同段）。唯一性巡检在加载入口（T3 CardCatalog） |
| `name` | string | 卡名，非空 |
| `cost` | int | 能量费；0 费合法（如敌方卡） |
| `attack` / `maxHp` | int | 怪兽卡专属；maxHp 必须为正；attack 0 合法（"墙"） |
| `effects` | EffectGroupDef[] | 效果卡专属，非空；载荷词汇 = 技能系统 Data 层（op 判别见 T1，`ModOp` 字段键名为 `mod`） |

单卡校验（反序列化即抛，不带病运行）：未知 kind / 非正 id / 空 name / maxHp≤0 / 空 effects。
时长词汇提示：`duration: "Rounds"` 以"方回合"计（一整圈=2 tick）——扛过一轮敌方回合用 2（防御卡 1004 先例）；`ThisPhase` 在相位结束即清，扛不到敌方回合（详见 CardGame-Doc/M5-API缺口清单.md）。
**牌库编排（id×张数）与敌方召唤序列不进 cards.json**——代码侧 `BattleSetup` 组合，数据化归 M5 内容管线。

> M2~M4 阶段本表手工维护（值即源头）；M5 导表工具接管刷新。现存的六张卡为 M1 `DemoBattleData` 原值移植（T5 切换的对照源）。
