# StateTree 层级状态机 — 设计简介

> 面试用速览版，详细展开见《StateTree设计详解》。

## 一句话介绍

一套用 C# 实现的**层级状态机（State Tree）**，架构思想参考 Unreal Engine 5 的 State Tree：状态组织为树，任意时刻只有一条"根 → 叶"的激活路径在运行，用**声明式转换（条件 + 事件 + 时机）**驱动状态切换，共 6 个核心类、约 600 行代码。

## 为什么不用传统平铺 FSM

| 传统 FSM 的痛点 | 本设计的解法 |
| --- | --- |
| 状态数随组合爆炸（巡逻中有警报→战斗中受伤…） | 状态嵌套成树，父状态行为在子状态期间持续生效，公共逻辑只写一次 |
| 转换逻辑散落在状态代码里，难以总览 | 转换是数据（条件列表 + 事件 + 目标），集中声明、集中求值 |
| 状态间共享数据靠全局变量或字符串黑板 | 类型化 Context 基类，声明字段即共享，编译期检查 |
| 行为与切换耦合，难复用 | 行为（Task）、判断（Condition）、切换（Transition）三者解耦，各自可组合复用 |

## 组成部分一览

| 类 | 职责 |
| --- | --- |
| `StateTree` | 运行时核心：维护激活路径、驱动 Tick、求值转换、处理事件 |
| `StateTreeState` | 树节点：持有任务、子状态、三组转换，链式 API 构建 |
| `StateTreeTask` | 状态的行为：`OnEnter / OnTick / OnExit`，OnTick 返回状态（Running / Succeeded / Failed） |
| `StateTreeCondition` | 纯判断：`IsMet(tree)`，不改数据，可组合（如 `NotCondition` 取反） |
| `StateTreeTransition` | 一条转换规则：全部条件满足（与语义）+ 可选事件匹配 → 按 Goto / Next 切换 |
| `StateTreeContext` | 类型化共享数据容器，用户继承并声明字段 |
| `StateTreeDriver` | Unity 接入层：一个 MonoBehaviour 统一驱动多棵树自动 Tick |

## 运行机制要点

1. **激活路径**：启动或切换时，从目标状态沿 `InitialChild` 链下钻到叶子，激活路径是根到叶的一条链；父状态的任务和转换在子状态运行期间持续生效。
2. **Tick 流水线**：刷新事件 → 任务从根到叶更新 → 若有任务完成则先求值"完成转换"→ 否则求值"每帧转换"。
3. **叶子优先冒泡**：转换求值从叶到根，子状态的转换优先于父状态，天然实现"子状态覆盖父状态"。
4. **路径差量切换**：切换时对比新旧路径，公共祖先不动，退出从叶到根、进入从根到叶，最小化回调次数。
5. **事件单次消费**：`SendEvent` 送入缓冲，仅在被刷新的那一帧有效，避免"粘性事件"导致的意外连跳。
6. **三种转换时机**：`OnEnter`（进入立即求值，支持有限级联）、`OnTick`（每帧）、`OnCompleted`（任务完成时）——覆盖"立即重定向 / 常规轮询 / 行为驱动"三类切换需求。

## 设计亮点（面试展开点）

- **数据与逻辑分离**：Context 是数据，Task/Condition 是纯逻辑，无内部状态的条件可直接复用、易测试。
- **类型安全替代字符串黑板**：没有 `blackboard.GetValue<float>("Hp")` 这类运行时字符串查找和装箱，字段改名由编译器兜底。
- **健壮性防护**：转换重入保护（`isTransitioning`）、Enter 转换级联深度上限（16 层，防 A→B→A 死循环）、未注册状态的警告与忽略。
- **纯 C# 内核 + 薄 Unity 壳**：核心不依赖 MonoBehaviour，`Tick(dt)` 可手动步进，便于确定性测试；`StateTreeDriver` 只是把 Tick 挂到 `Update`。
- **链式构建 API**：`state.AddTask(...).AddChild(...).AddGotoTransition(...)`，状态配置一目了然。

## 用法速览

```csharp
var tree = new StateTree("Guard", context);
tree.AddState(
    new StateTreeState("Patrol")
        .AddTask(new LambdaTask(onTick: (t, s, dt) => Move(dt)))
        .AddGotoTransition("Combat", @event: "Spotted")   // 收到 Spotted 事件才切
        .AddChild(new StateTreeState("PatrolA")
            .AddTask(new WaitTask(3f))
            .AddGotoOnCompletedTransition("PatrolB")));   // 等 3 秒后去 B
tree.Start("Patrol");
```
