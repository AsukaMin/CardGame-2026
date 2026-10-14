# Art —— UI 底子图（从 CardGame_Arts/deliver/ 挑进来的）

永远不会通过补丁换的 UI 框架件：九宫格背景、按钮框、卡框、字体。
预制体 Inspector 直接 [SerializeField] 引用这里的 Sprite，零代码加载。

分流规则：会随补丁换内容的图（卡面、头像、关卡插图）去 GameData/art/，不进这里。
