# Resources/UI —— 窗口预制体

约定：**预制体名 = 窗口类名**（DeckWindow.cs ↔ DeckWindow.prefab，根节点挂该脚本）。

UIManager.Show("DeckWindow") 会到这里同步加载。全项目仅 UIManager 一处 Resources.Load。
控件引用在预制体里用 [SerializeField] 拖好，代码不做名字查找。
