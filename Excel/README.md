# Excel —— 源表，唯一人工编辑点

放 `cards.xlsx`、`levels.xlsx` 等源表。数值不写死在代码里，所有卡牌/关卡数据从这里出发。

- 只由人编辑，导表工具（Tools/）读取它生成代码和 JSON。
- 分发链路：Excel →（导表）→ ConfigGen 生成类 + GameData JSON。
