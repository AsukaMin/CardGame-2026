# Tools —— 导表 + 打补丁脚本

推荐做成 Unity Editor 菜单（放在 Unity 工程的 Editor 目录里），两个按钮：

1. **导表**：读 Excel/ 源表 → 生成 C# 类到 `Unity/Assets/Scripts/ConfigGen/`，
   同时刷新运行时 JSON 母本到 `GameData/`。
2. **打补丁包**：复制 `PatchSource/`（或勾选的 GameData 变更文件）→ 生成 manifest.json → 压缩 zip。

注意：改数值 = 补丁可行；改表结构（加字段）= 生成类变化 = 必须出整合包。
