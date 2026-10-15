# StateTree 层级状态机 — 设计详解

> 本文详细拆解这套状态机的每个组成部分、执行流程与设计决策，可作为面试深挖时的材料。
> 代码位于 `Assets/Scripts/FSM/`，共 6 个核心文件（不含测试），约 600 行。

---

## 1. 概述

### 1.1 这是什么

一套 Unity / C# 的**层级状态机运行时**，架构思想参考 Unreal Engine 5 的 State Tree（代码注释中逐类标注了对应关系），但用纯 C# 类 + 链式 API 实现，不依赖 ScriptableObject 或可视化编辑器。

核心模型一句话：**状态组织为一棵树，任意时刻只有一条"根 → 叶"的激活路径在运行；切换由声明式的转换规则（条件 + 事件 + 时机）驱动；行为写在任务里，数据放在类型化上下文里。**

### 1.2 设计目标

1. **解决状态爆炸**：传统平铺 FSM 中，"巡逻时发现敌人""战斗中血量低"这类正交关注点会组合出大量状态。层级化后公共逻辑放父状态，只写一次。
2. **声明式转换**：转换规则是数据（谁、在什么条件下、切到哪），集中可见、可审查，而不是埋在各个状态的行为代码里。
3. **类型安全的数据共享**：不做字符串键黑板，Context 直接声明强类型字段。
4. **行为 / 判断 / 切换三者解耦**：Task、Condition、Transition 各自独立，可自由组合复用。
5. **可测试、可嵌入**：核心逻辑为纯 C#，`Tick(dt)` 支持手动步进；Unity 只通过一个薄的 Driver 组件接入。

### 1.3 文件地图

| 文件 | 内容 |
| --- | --- |
| `StateTreeTypes.cs` | 基础枚举与上下文基类（TaskStatus / TransitionType / TransitionRunMode / StateTreeContext） |
| `StateTreeTask.cs` | 任务基类 + 内置任务（LogTask / WaitTask / LambdaTask） |
| `StateTreeCondition.cs` | 条件基类 + 内置条件（Lambda / Context / TimeInState / Not） |
| `StateTreeState.cs` | 状态节点 + 转换规则（StateTreeState / StateTreeTransition） |
| `StateTree.cs` | 运行时核心（激活路径、Tick 流水线、转换求值、事件系统） |
| `StateTreeDriver.cs` | Unity MonoBehaviour 驱动器 |

### 1.4 整体架构

```
                 ┌────────────────────────────┐
   Unity 生命周期 │  StateTreeDriver (MB)      │  Update() 逐棵调 Tick()
                 └───────────┬────────────────┘
                             │ 持有 N 棵
                             ▼
   ┌──────────────────────────────────────────────┐
   │  StateTree（运行时核心）                       │
   │  · rootStates 根状态表                         │
   │  · stateIndex 名字→状态 索引                   │
   │  · activePath  激活路径（根→叶）                │
   │  · eventBuffer / activeEvents 事件系统         │
   │  · Context     共享数据（用户子类）             │
   └───────┬───────────────────────┬──────────────┘
           │ 持有                   │ 求值
           ▼                        ▼
   StateTreeState（树节点）   StateTreeTransition（转换规则）
   · Tasks 行为列表            · Conditions（与语义）
   · Children 子状态           · Event（可选，单次消费）
   · Enter/Tick/Completed      · Type：Goto / Next / None
     三组转换                          │ 引用
           │ 包含                      ▼
           ▼                   StateTreeCondition（判断）
   StateTreeTask（行为）
```

---

## 2. 基础类型（StateTreeTypes.cs）

三个枚举 + 一个抽象类，是全系统的"词汇表"：

### 2.1 `TaskStatus` — 任务运行状态

```csharp
public enum TaskStatus { Running, Succeeded, Failed }
```

- `Running`：状态仍在进行中；
- `Succeeded / Failed`：状态**完成**，会触发该状态路径上的 `OnCompleted` 转换求值。对应 UE 的 `EStateTreeRunStatus`。

### 2.2 `TransitionType` — 转换目标

```csharp
public enum TransitionType { Goto, Next, None }
```

- `Goto`：跳到指定名字的状态，**可跨层级**（通过全局 stateIndex 定位）；
- `Next`：跳到同级下一个兄弟状态（序列推进的惯用写法，如 PatrolA → PatrolB → PatrolC）；
- `None`：留在原状态（可用于"只消费事件不做动作"等占位场景）。

### 2.3 `TransitionRunMode` — 转换求值时机

```csharp
public enum TransitionRunMode { OnEnter, OnTick, OnCompleted }
```

| 时机 | 求值时刻 | 典型用途 |
| --- | --- | --- |
| `OnEnter` | 进入状态后立即求值一次 | 入口重定向：进入"移动"状态但目标无效时立刻转去"待机" |
| `OnTick` | 每帧求值 | 常规轮询：血量低于阈值、收到事件 |
| `OnCompleted` | 状态内任一任务返回 Succeeded/Failed 后求值 | 行为驱动：WaitTask 等完 3 秒 → 去下一个巡逻点 |

三个时机覆盖了状态机切换的三大类来源：**进入即判定、持续观察、行为结束**。

### 2.4 `StateTreeContext` — 类型化共享上下文

```csharp
public abstract class StateTreeContext { }
```

对应 UE State Tree 的 Context Data。用户继承并直接声明字段：

```csharp
class EnemyCtx : StateTreeContext {
    public float Hp;
    public Vector3 Target;
}
```

任务与条件通过 `tree.Context` 读写这些字段。**与黑板模式的对比**是面试常问点：

| | 字符串黑板 | 类型化 Context |
| --- | --- | --- |
| 键的正确性 | 运行时才暴露（拼错静默失败） | 编译期保证 |
| 值类型 | 常伴随 object 装箱 | 强类型，无装箱 |
| 重构 | 改名要全局搜字符串 | IDE / 编译器自动处理 |
| 代价 | 运行时可动态增删键 | 结构固定，需预先声明 |

本系统选择 Context：游戏 AI 的共享数据结构通常在开发期就稳定，类型安全的收益大于动态性的损失。

---

## 3. 任务 StateTreeTask（StateTreeTask.cs）

### 3.1 基类

```csharp
public abstract class StateTreeTask
{
    public virtual void OnEnter(StateTree tree, StateTreeState state) { }
    public virtual TaskStatus OnTick(StateTree tree, StateTreeState state, float dt)
        => TaskStatus.Succeeded;
    public virtual void OnExit(StateTree tree, StateTreeState state) { }
}
```

- 任务是**状态的行为单元**，一个状态可挂多个任务，进入时全部 `OnEnter`、每帧全部 `OnTick`、退出时全部 `OnExit`；
- `OnTick` 的返回值是任务与运行时的唯一协议：返回非 `Running` 即宣告"状态完成"；
- **任务自身的字段就是实例数据**（如 WaitTask 的 `elapsed`），不需要额外的数据注入机制；
- 生命周期语义：`OnExit` 无论正常切换还是被打断都会调用，任务可依赖这一点做清理。

### 3.2 内置任务

| 任务 | 行为 |
| --- | --- |
| `LogTask` | 进入时打一条日志，调试用 |
| `WaitTask` | 内部累计 `elapsed`，超过指定秒数返回 `Succeeded`，是 `OnCompleted` 转换的最小驱动器 |
| `LambdaTask` | 构造时传入 onEnter / onTick / onExit 三个委托，简单状态不必建子类 |

`LambdaTask` 体现了分层策略：**简单行为用 lambda 快速表达，复杂行为继承基类**，两者在系统内完全同权。

---

## 4. 条件 StateTreeCondition（StateTreeCondition.cs）

### 4.1 基类

```csharp
public abstract class StateTreeCondition
{
    public abstract bool IsMet(StateTree tree);
}
```

条件是**纯判断**：只读树与上下文，不改任何数据（注释明确约束：对应 UE Condition 的"只做判断"）。这带来两个性质：

1. **无副作用**：条件在一帧内被多次求值也是安全的，求值顺序不影响结果；
2. **可组合**：装饰器模式直接套用，如内置的 `NotCondition`。

### 4.2 内置条件

| 条件 | 语义 |
| --- | --- |
| `LambdaCondition` | 函数式条件，接收整棵树 |
| `ContextCondition<T>` | 类型化上下文条件：`new ContextCondition<EnemyCtx>(c => c.Hp <= 0)`，内部做一次类型匹配后调用强类型委托 |
| `TimeInStateCondition` | 当前状态停留超过 N 秒时成立（读 `tree.TimeInState`），实现超时类转换 |
| `NotCondition` | 取反装饰器，演示条件的可组合性 |

`ContextCondition<T>` 与 `LambdaCondition` 的分工：前者面向 Context 数据（更常用、签名更短），后者需要访问树本身的状态（如 `ActivePath`）时使用。

---

## 5. 转换 StateTreeTransition（StateTreeState.cs 内）

### 5.1 结构

```csharp
public class StateTreeTransition
{
    public TransitionType Type;   // Goto / Next / None
    public string Target;         // Goto 的目标状态名
    public string Event;          // 可选：要求收到的事件名

    public readonly List<StateTreeCondition> Conditions;  // 与语义
}
```

一条转换 = **何时（RunMode 决定，挂在状态的三张表里）+ 是否（条件与事件）+ 去哪（Type/Target）**。

### 5.2 判定逻辑

```csharp
public bool ShouldTransition(StateTree tree)
{
    if (!string.IsNullOrEmpty(Event) && !tree.IsEventActive(Event)) return false;
    for (int i = 0; i < Conditions.Count; i++)
        if (!Conditions[i].IsMet(tree)) return false;
    return true;
}
```

- **条件是与语义**：全部满足才放行；要"或"语义用两条转换表达（转换表本身就是或）；
- **事件是额外的门**：`Event` 非空时，还必须恰好在本帧收到了同名事件。事件与条件可以同用（"收到 Hit 事件 **且** 血量 < 30% 才逃跑"）。

### 5.3 事件的单次消费语义

- `tree.SendEvent("Spotted")` 只是写入 `eventBuffer`；
- 下一次 `Tick` 开始时 `FlushEvents` 把缓冲整体替换进 `activeEvents`，随后清空缓冲；
- 再下一帧若无新事件，`activeEvents` 为空，带 `Event` 的转换全部失效。

即**事件的有效期恰好是它被刷新的那一帧**。这个设计避免了常见 bug：轮询型条件（如血量）持续成立时，事件门如果不失效，状态会每帧连续跳转。一次事件 = 至多一次事件驱动的切换。

---

## 6. 状态 StateTreeState（StateTreeState.cs 内）

### 6.1 结构

```csharp
public class StateTreeState
{
    public readonly string Name;
    public readonly List<StateTreeTask> Tasks;            // 行为
    public readonly List<StateTreeState> Children;        // 子状态

    // 按 RunMode 分组的三张转换表
    public readonly List<StateTreeTransition> EnterTransitions;
    public readonly List<StateTreeTransition> TickTransitions;
    public readonly List<StateTreeTransition> CompletedTransitions;

    public StateTreeState Parent { get; private set; }
    public StateTreeState InitialChild { get; private set; }
}
```

要点：

- **转换表挂在状态上而不是集中在一处**，这是层级模型的关键：父状态的转换在其整个子树激活期间持续参与求值——"战斗"父状态的"血量归零 → 死亡"转换，对战斗的所有子状态（追击、攻击、闪避）统一生效，不必每个子状态重复声明。
- `InitialChild`：进入一个父状态时，运行时会沿 InitialChild 链自动下钻到叶子，保证 `CurrentState` 永远是叶子（**不变式：激活路径末端必为叶子状态**）。
- `AddChild(child, initial)` 中 `initial` 参数只对**第一个**以 initial=true 加入的子状态生效，之后的 initial=true 不覆盖，避免歧义。

### 6.2 链式构建 API

```csharp
new StateTreeState("Combat")
    .AddTask(new AttackTask())
    .AddGotoTransition("Flee", @event: "LowHp",
        new ContextCondition<EnemyCtx>(c => c.Hp < 0.3f))
    .AddChild(new StateTreeState("Chase")
        .AddTask(new WaitTask(5f))
        .AddGotoOnCompletedTransition("Attack"));
```

四个便捷方法分别对应常用组合：

| 方法 | 语义 |
| --- | --- |
| `AddGotoTransition(target, event, conditions)` | 每帧求值的 Goto |
| `AddGotoOnEnterTransition(...)` | 进入时立即求值的 Goto |
| `AddGotoOnCompletedTransition(...)` | 任务完成时求值的 Goto |
| `AddNextTransition(event, conditions)` | 每帧求值的"下一个兄弟" |

便捷方法底层统一走 `AddTransition(t, mode)`，RunMode 体系保持完备，未封闭扩展。

---

## 7. 运行时核心 StateTree（StateTree.cs）

这是系统的心脏，负责四件事：**构建与索引、启动、驱动（Tick）、状态激活**。

### 7.1 内部数据

| 字段 | 作用 |
| --- | --- |
| `rootStates` | 顶层状态表（树的多个根入口） |
| `stateIndex` | 名字 → 状态的扁平字典（含所有子孙），Goto 跨层级定位靠它，O(1) |
| `activePath` | 当前激活链（根 → 叶），是运行时的"唯一真相" |
| `eventBuffer / activeEvents` | 事件的两级结构（写入缓冲 / 当帧生效集） |
| `isTransitioning` | 重入保护标志 |
| `TimeInState` | 当前叶子状态的停留时间，激活路径变化时归零 |

`CurrentState`（当前叶子）、`IsRunning`、`Context`、`Name` 对外暴露。

### 7.2 启动与外部控制

- `Start(name)`：从 stateIndex 找到初始状态，`Activate` 它（会连带 InitialChild 链一起进入）；
- `ChangeState(name)`：**外部主动切换，无视转换表**——用于 UI 按钮、调试面板这类"权威来自外部"的场景；受 `isTransitioning` 保护，防止在激活过程中被回调重入；
- `SendEvent(name)`：写入事件缓冲；
- `GetState(name)` / `ActivePath` / `GetActivePathString()`：查询与调试接口。

### 7.3 Tick 流水线（核心执行序）

```csharp
public void Tick(float dt)
{
    if (!IsRunning) return;

    TimeInState += dt;
    FlushEvents();                      // 事件缓冲 → 当帧生效集（单次消费）

    if (isTransitioning) return;        // 重入保护

    // 1. 任务更新：从根到叶
    bool anyCompleted = false;
    for (int i = 0; i < activePath.Count; i++)
        for (int t = 0; t < activePath[i].Tasks.Count; t++)
            if (activePath[i].Tasks[t].OnTick(this, activePath[i], dt) != TaskStatus.Running)
                anyCompleted = true;

    if (anyCompleted && TryEvaluate(CompletedTransitionsOf)) return;

    // 2. 每帧转换：从叶到根冒泡
    TryEvaluate(TickTransitionsOf);
}
```

固定顺序：**任务更新（根→叶）→ 完成转换（叶→根）→ 每帧转换（叶→根）**，且完成转换命中即返回，当帧不再求值每帧转换。两个方向各有含义：

- 任务**从根到叶**更新：父状态的任务先于子状态执行，父任务（如"维持移动"）为子任务（如"瞄准"）提供基础；
- 转换**从叶到根冒泡**：叶子优先级最高。同一个条件父、子都配置了转换时，子状态的转换先命中——即**子状态覆盖父状态**，与继承重写同构。

`TryEvaluate` 用一个 `TransitionSelector` 委托统一三张表的遍历（Enter/Tick/Completed 共用同一套"叶→根、表内按序、首个命中即执行"的逻辑），是从叶到根扫描 activePath、在每个状态内按声明顺序检查转换、`ShouldTransition` 通过且 `ExecuteTransition` 真的切换了才返回 true。

`ExecuteTransition` 按 Type 分派：Goto 查 stateIndex 找不到则告警忽略（不中断运行）；Next 通过 `GetNextSibling` 定位兄弟（根级状态查 `rootStates`，子状态查父的 `Children`），已是最后一个则留在原状态返回 false——**序列到达末尾自动停在原地**，这是状态序列的默认语义。

### 7.4 状态激活算法（Activate）

切换状态时如何正确地退旧进新，是层级状态机最微妙的部分。本实现采用**路径差量**算法：

```
1. 计算新路径：目标沿 InitialChild 下钻到叶子 leaf，再沿 Parent 回溯到根，反转得到 根→叶 链
2. 退出：旧路径中不在新路径的状态，从叶到根调用其任务的 OnExit
3. 提交：activePath = 新链，CurrentState = leaf；若 leaf 是新叶子则 TimeInState 归零
4. 进入：新路径中不在旧路径的状态，从根到叶调用其任务的 OnEnter
5. Enter 转换：求值各级 EnterTransitions（叶→根），允许级联，上限 16 层
```

三个值得强调的性质：

- **公共祖先零扰动**：从 `战斗/攻击` 切到 `战斗/追击` 时，"战斗"父状态既不退出也不重进，其任务持续运行（比如父状态上的"仇恨衰减"计时不会被打断）。这是层级状态机相对平铺 FSM 的核心收益之一。
- **退出与进入的顺序**：先退旧（叶→根）再进新（根→叶），符合状态机的直觉——完全离开旧语境后再进入新语境；
- **回调安全**：整个 Activate 包在 `try/finally` 里维护 `isTransitioning`，激活过程中任何回调（OnEnter、Enter 转换）若再触发 Tick 或 ChangeState 会被直接拒绝，杜绝重入导致的状态损坏。

### 7.5 Enter 转换的有限级联

```csharp
private const int MaxEnterTransitionDepth = 16;

for (int depth = 0; depth < MaxEnterTransitionDepth && TryEvaluate(EnterTransitionsOf); depth++) { }
```

进入状态后立即再求值 Enter 转换并循环，支持"入口重定向链"（A 进入后转 B，B 进入后转 C）。但若无上限，两个互相 GotoOnEnter 的状态会死循环——深度上限 16 是兜底：合法的重定向链几乎不可能超过个位数，超限即视为配置错误，链被截断而非程序挂死。**用可预测的失败替代不可预测的死循环**，是防御性设计在状态机里的典型应用。

### 7.6 一个值得注意的细节：TimeInState 的归零条件

```csharp
if (!oldSet.Contains(newLeaf) || oldPath.Count == 0) TimeInState = 0f;
```

归零条件是"**新叶子不在旧路径中**"，而不是"发生了 Activate"。因此 Goto 回自己（或转换后叶子恰好没变，例如父状态间平移）时，计时**保留**。`TimeInStateCondition` 的语义因此更精确：它度量的是"叶子状态连续停留的时长"。

---

## 8. Unity 接入 StateTreeDriver（StateTreeDriver.cs）

```csharp
public class StateTreeDriver : MonoBehaviour
{
    private readonly List<StateTree> trees = new List<StateTree>();

    public T Register<T>(T tree) where T : StateTree { ... }
    public void Unregister(StateTree tree) => trees.Remove(tree);

    private void Update()
    {
        for (int i = 0; i < trees.Count; i++) trees[i].Tick();
    }
}
```

- 一个 Driver 可挂多棵树（角色一棵、全局游戏流程一棵），统一 `Update` 驱动；
- `Register` 返回原树对象，支持 `driver.Register(new StateTree(...)).AddState(...)` 连写；
- `OnDestroy` 清空列表，随 GameObject 销毁自动停摆。

**分层意义**：整个系统只有这一个 MonoBehaviour。StateTree 的 Tick 有 `Tick(float dt)` 重载，不依赖 `Time.deltaTime`，可以在测试里手动步进、精确控制帧序列（配合固定 dt 即得确定性模拟）。Unity 依赖被压缩到"每帧调一次 Tick"这一行。

---

## 9. 关键设计决策与权衡（面试 Q&A 素材）

**Q1：为什么选 State Tree 而不是行为树（BT）？**
AI 行为存在大量"长期驻留的状态"（巡逻、战斗、待机），FSM/State Tree 的显式状态与转换天然表达"驻留"；BT 的每帧重规划（从根重新评估优先级）更适合"每次挑一个最优动作"的决策型逻辑。State Tree 相对传统 FSM 又补上了层级复用与声明式转换。可以理解为：State Tree = FSM 的层级化 + 转换的声明式化，介于 FSM 和 BT 之间。

**Q2：事件为什么做成单次消费，而不是置位直到被读取？**
两类语义各有坑。本系统的每帧转换是持续求值的，若事件粘住，"收到 Hit → 硬直"会在硬直结束后又被同一事件触发一次。帧消费把事件对齐到转换的求值周期：一次 SendEvent 最多驱动一次切换。需要持续状态时，用 Context 字段 + 普通条件表达，而不是事件。

**Q3：条件为什么限制为只读？**
保证求值无副作用 → 一帧内多次求值安全、与求值顺序无关、条件实例可跨状态复用。副作用（改数据）全部收在 Task 的 OnEnter/OnExit 里，职责边界清晰。

**Q4：转换优先级怎么定？**
两级：先深度后声明顺序。叶到根冒泡保证子状态覆盖父状态；同一状态的三张表内按添加顺序，先声明先命中。每帧最多执行一次转换（命中即返回），状态变化在同帧内不可见，避免了同帧连环转换的顺序歧义。

**Q5：如何防止状态机跑飞？**
三道防线：① `isTransitioning` 重入保护，激活过程中的嵌套切换/直接被拒；② Enter 级联深度上限 16；③ Goto/ChangeState 到未注册状态只告警不异常，运行时保持存活。

**Q6：如果要做编辑器可视化/数据驱动（ScriptableObject / JSON），要动多少？**
核心模型已经是纯数据形状（State 树 + 三张转换表 + Task/Condition 实例），序列化层只需描述"如何创建这些对象"（如类型名 + 参数反射构造），运行时 StateTree 一行不用改。这是把构建（链式 API）与运行（Activate/Tick）分离的直接收益。

**Q7：已知的取舍 / 可改进点**（主动谈局限是加分项）
- 转换是全局名字寻址（stateIndex），重名会互相覆盖——可改为注册时校验唯一性；
- `TimeInState` 只针对叶子，父状态的停留时长没有单独度量；
- 同帧最多一次转换，依赖"当帧完成、下帧继续"的语义，极端级联会跨多帧；
- Task/Condition 目前是类实例，无对象池；高频创建销毁时（如子弹）可加池化。

---

## 10. 扩展指南

| 想扩展什么 | 怎么做 |
| --- | --- |
| 新行为 | 继承 `StateTreeTask`（复杂）或用 `LambdaTask`（简单） |
| 新判断 | 继承 `StateTreeCondition`，或组合现有条件（`NotCondition` 模式） |
| 新共享数据 | 继承 `StateTreeContext` 声明字段 |
| 新转换目标类型（如 Random、GotoParent） | 扩 `TransitionType` + 在 `ExecuteTransition` 加一个 case |
| 新求值时机 | 扩 `TransitionRunMode` + 状态加对应转换表 + Tick 流水线插入求值点 |
| 接入其他引擎/服务器 | 替换 `Debug`/`Time` 两个 Unity API，其余照搬 |

四个扩展点全部是"加法"（新子类、新枚举值、新 case），不需要修改现有运行逻辑，符合开闭原则。
