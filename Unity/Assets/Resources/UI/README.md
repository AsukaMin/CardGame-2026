# Resources/UI —— 窗口与条目预制体目录

全项目唯一 Resources/UI；**UIManager 是唯一加载方**：`UIManager.Show("窗口名")` → `Resources.Load("UI/" + 窗口名)`。

## 窗口预制体（UIWindow 子类）

- **文件名 = 窗口类名**（如 `BattleWindow.prefab` ↔ `BattleWindow.cs`），根节点挂该窗口脚本（UITable 由 RequireComponent 自动带上）。
- 控件按约定命名后，在**根节点 UITable** 的 Inspector 点"扫描子节点"，引用即序列化进预制体——运行时零 Find，代码 `GetButton("key")` 按取用。

## 条目/小部件模板预制体（无窗口脚本）

- 如 `CardItem` / `UnitItem`：根节点只挂 UITable、控件按约定命名、各自扫描；不放任何脚本（代码实例化后 `GetComponent<UITable>()` 取用，需要行为时运行时 `AddComponent` 视图脚本）。

## 控件命名约定（详见 .docs/UI框架-控件引用预序列化.md）

| 节点名 | 收进引用表 | 代码取用 |
|---|---|---|
| `&_image_key_&` | Image | `GetImage("key")` |
| `&_button_key_&` | Button | `GetButton("key")` |
| `&_text_key_&` | TMP_Text | `GetText("key")` |
| `&_gameobject_key_&` | GameObject | `GetGameObject("key")` |
| `$_node_key_$` | 嵌套子窗口（UIWindow） | `GetWindow("key")` |
| `$_table_key_$` | 嵌套 UITable（扫描边界） | `GetTable("key")` |

- 改名入口：Hierarchy 右键 `GameObject/UITable/改名为 …`；扫描入口：UITable Inspector 底部"扫描子节点"（仅编辑态）。
- **节点改名后必须重扫**，否则序列化 key 还是旧值；同表 key 冲突时扫描会自动改名并警告。
