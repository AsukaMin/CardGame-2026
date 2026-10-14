# ConfigGen —— 导表生成的 C# 类（只读区）

导表工具从 Excel/ 生成的容器/访问器类放这里（如 Card.Generated.cs、Level.Generated.cs），
**不手改**：要加辅助方法用 partial class / 扩展方法，另建文件放本目录或业务目录。

和手写代码物理隔开，看见本目录就知道别动。
生成类编译进包，永远不进补丁——改表结构（加字段）必须出整合包。
