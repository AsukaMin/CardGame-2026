# Loader —— 启动加载

全项目唯一知道外部路径的类放这里：

- `ConfigDatabase`（规划中）：启动一次性读 Config/ → Mods/ 的配置 JSON + manifest 校验
  （minAppVersion 防旧整合包装新补丁）。主流程第一步调 LoadAll，没加载完不进主菜单。
- `ArtStore`（规划中）：热更散图懒加载 + 缓存池，查找顺序 Mods/art → Config/art，
  找不到返回 null 由调用方显示占位灰图。

路径纪律：业务代码里出现 Path.Combine 就是坏味道，要么归这几个类，要么新建第五个 Loader。
