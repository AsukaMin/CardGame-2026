# GameData —— 运行时 JSON 母本（不进 Unity Assets）

- 只由导表工具刷新，**不手工编辑**；出整合包时整体复制成安装包的 `Config/`。
- `manifest.json`：版本号 + 补丁文件清单（version / minAppVersion / files）。
- `effect_cards.json`、`part_cards.json`：效果卡/部位卡数据——由**技能编辑器**（`Tools/技能编辑器/导出卡池 JSON`）从 `Assets/CardAuthoring/` 的 SO 源导出刷新。
- `monster_sleeves.json`（未来，卡套纯数值表）、`levels.json`：由 Excel 导表工具刷新。
- `art/`：热更散图（从 CardGame_Arts/deliver/ 挑进来的，运行时按文件名懒加载，Mods 优先）。

运行时查找顺序：`Game/Mods/ → Game/Config/ → 代码默认值`。
补丁永远不改这里：补丁落朋友的 `Mods/`，本目录是"唯一最新全集"。
