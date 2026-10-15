using System.Collections.Generic;
using UnityEngine;

namespace FSM
{
    /// <summary>
    /// 状态树运行时（对应 UE State Tree）。
    /// - 根状态列表构成顶层，每个状态可再嵌套子状态；
    /// - 激活路径为从根到叶的一条链，父状态的 Task/Transition 在子状态激活期间持续生效；
    /// - Tick 顺序：任务从根向叶更新 → 完成转换（叶子优先冒泡）→ 每帧转换；
    /// - SendEvent 触发事件，带 Event 的转换只在事件当帧生效（单次消费）。
    /// </summary>
    public class StateTree
    {
        public string Name { get; }
        public StateTreeContext Context { get; }

        /// <summary>当前激活的叶子状态（未启动为 null）</summary>
        public StateTreeState CurrentState { get; private set; }

        /// <summary>当前状态停留时间（秒），激活路径变化时归零</summary>
        public float TimeInState { get; private set; }

        public bool IsRunning { get; private set; }

        private readonly List<StateTreeState> rootStates = new List<StateTreeState>();        // 根状态表

        private readonly List<StateTreeState> activePath = new List<StateTreeState>(); // 根 -> 叶   激活状态链
        private readonly HashSet<string> activeEvents = new HashSet<string>();
        private readonly List<string> eventBuffer = new List<string>();
        private readonly Dictionary<string, StateTreeState> stateIndex = new Dictionary<string, StateTreeState>();        // 状态名字索引表

        private bool isTransitioning;

        // 防止 Enter 转换链无限循环（如 A->B->A 互相 GotoOnEnter）
        private const int MaxEnterTransitionDepth = 16;

        public StateTree(string name = null, StateTreeContext context = null)
        {
            Name = string.IsNullOrEmpty(name) ? "StateTree" : name;
            Context = context;
        }

        // ---- 构建 ----

        public StateTree AddState(StateTreeState state)
        {
            if (state == null) return this;
            rootStates.Add(state);
            IndexStates(state);
            return this;
        }

        private void IndexStates(StateTreeState state)
        {
            stateIndex[state.Name] = state;
            for (int i = 0; i < state.Children.Count; i++) IndexStates(state.Children[i]);
        }

        /// <summary>启动状态树，进入指定状态（会连同其 InitialChild 链一起进入）</summary>
        public void Start(string initialStateName)
        {
            if (IsRunning) return;
            if (!stateIndex.TryGetValue(initialStateName, out var state))
            {
                Debug.LogError("[StateTree:" + Name + "] 未注册的初始状态：'" + initialStateName + "'");
                return;
            }

            IsRunning = true;
            TimeInState = 0f;
            Activate(state);
        }

        /// <summary>发送事件。带 Event 的转换在本次 Tick 内有效，之后自动失效（单次消费）</summary>
        public void SendEvent(string eventName)
        {
            if (!string.IsNullOrEmpty(eventName)) eventBuffer.Add(eventName);
        }

        public bool IsEventActive(string eventName) => activeEvents.Contains(eventName);

        /// <summary>外部主动切换（无视转换表），常用于 UI/输入直接控制</summary>
        public void ChangeState(string targetName)
        {
            if (!IsRunning || isTransitioning) return;
            if (!stateIndex.TryGetValue(targetName, out var state))
            {
                Debug.LogWarning("[StateTree:" + Name + "] ChangeState 到未注册状态：'" + targetName + "'，已忽略。");
                return;
            }
            Activate(state);
        }

        // ---- 驱动 ----

        public void Tick()
        {
            Tick(Time.deltaTime);
        }

        public void Tick(float dt)
        {
            if (!IsRunning) return;

            TimeInState += dt;
            FlushEvents();

            if (isTransitioning) return;

            // 1. 任务更新：从根到叶
            bool anyCompleted = false;
            for (int i = 0; i < activePath.Count; i++)
            {
                var s = activePath[i];
                for (int t = 0; t < s.Tasks.Count; t++)
                {
                    var status = s.Tasks[t].OnTick(this, s, dt);
                    if (status != TaskStatus.Running) anyCompleted = true;
                }
            }

            if (anyCompleted && TryEvaluate(CompletedTransitionsOf)) return;

            // 2. 每帧转换：从叶到根冒泡（叶子优先级高）
            TryEvaluate(TickTransitionsOf);
        }

        /// <summary> Tick中，把eventBuffer写入activeEvents </summary>
        private void FlushEvents()
        {
            if (eventBuffer.Count == 0) return;
            activeEvents.Clear();
            foreach (var e in eventBuffer) activeEvents.Add(e);
            eventBuffer.Clear();
        }

        private delegate List<StateTreeTransition> TransitionSelector(StateTreeState s);
        private static List<StateTreeTransition> TickTransitionsOf(StateTreeState s) => s.TickTransitions;
        private static List<StateTreeTransition> CompletedTransitionsOf(StateTreeState s) => s.CompletedTransitions;
        private static List<StateTreeTransition> EnterTransitionsOf(StateTreeState s) => s.EnterTransitions;

        private bool TryEvaluate(TransitionSelector selector)
        {
            for (int i = activePath.Count - 1; i >= 0; i--)
            {
                var list = selector(activePath[i]);
                for (int t = 0; t < list.Count; t++)
                {
                    var trans = list[t];
                    if (!trans.ShouldTransition(this)) continue;
                    if (ExecuteTransition(activePath[i], trans)) return true;
                }
            }
            return false;
        }

        /// <summary>执行转换，返回是否真的发生了状态切换</summary>
        private bool ExecuteTransition(StateTreeState owner, StateTreeTransition trans)
        {
            switch (trans.Type)
            {
                case TransitionType.Goto:
                    if (stateIndex.TryGetValue(trans.Target, out var target))
                    {
                        Activate(target);
                        return true;
                    }
                    Debug.LogWarning("[StateTree:" + Name + "] Goto 到未注册状态：'" + trans.Target + "'，已忽略。");
                    return false;

                case TransitionType.Next:
                {
                    var next = GetNextSibling(owner);
                    if (next == null) return false; // 已经是最后一个，留在原状态
                    Activate(next);
                    return true;
                }

                default:
                    return false; // None：留在原状态
            }
        }

        private StateTreeState GetNextSibling(StateTreeState state)
        {
            // 根级状态在 rootStates 里找同级，子状态在父状态的 Children 里找
            var siblings = state.Parent != null ? state.Parent.Children : rootStates;

            for (int i = 0; i < siblings.Count; i++)
            {
                if (siblings[i] == state)
                {
                    var idx = i + 1;
                    return idx < siblings.Count ? siblings[idx] : null;
                }
            }
            return null;
        }

        // ---- 状态激活（核心）----

        /// <summary>
        /// 激活目标状态：先下钻到 InitialChild 叶子，再与旧路径对比——
        /// 公共祖先保持不动，退出的调用 OnExit，新进入的调用 OnEnter，最后求值 Enter 转换。
        /// </summary>
        private void Activate(StateTreeState target)
        {
            isTransitioning = true;
            try
            {
                #region 获取新的状态激活链  根 -> 叶
                var oldPath = new List<StateTreeState>(activePath);

                // 新路径：目标向下钻 InitialChild，再向上补全祖先到根
                var newLeaf = target;
                while (newLeaf.InitialChild != null) newLeaf = newLeaf.InitialChild;

                var chain = new List<StateTreeState>();
                for (var s = newLeaf; s != null; s = s.Parent) chain.Add(s);
                chain.Reverse(); // 根 -> 叶
                #endregion


                #region 旧路径中不再保留的状态：从叶到根退出
                var keep = new HashSet<StateTreeState>(chain);
                for (int i = oldPath.Count - 1; i >= 0; i--)
                {
                    var s = oldPath[i];
                    if (!keep.Contains(s))
                    {
                        for (int t = 0; t < s.Tasks.Count; t++) s.Tasks[t].OnExit(this, s);
                    }
                }
                #endregion


                activePath.Clear();
                activePath.AddRange(chain);
                CurrentState = newLeaf;

                var oldSet = new HashSet<StateTreeState>(oldPath);
                if (!oldSet.Contains(newLeaf) || oldPath.Count == 0) TimeInState = 0f;

                // 新进入的状态：从根到叶进入
                for (int i = 0; i < activePath.Count; i++)
                {
                    var s = activePath[i];
                    if (!oldSet.Contains(s))
                    {
                        for (int t = 0; t < s.Tasks.Count; t++) s.Tasks[t].OnEnter(this, s);
                    }
                }

                // Enter 转换：叶子优先，允许级联（有限深度）
                for (int depth = 0; depth < MaxEnterTransitionDepth && TryEvaluate(EnterTransitionsOf); depth++) { }
            }
            finally
            {
                isTransitioning = false;
            }
        }


        /// <summary>按名字取状态（构建自定义逻辑时使用）</summary>
        public StateTreeState GetState(string name)
        {
            return stateIndex.TryGetValue(name, out var s) ? s : null;
        }

        /// <summary>当前激活路径（根到叶），调试用</summary>
        public IReadOnlyList<StateTreeState> ActivePath => activePath;

        public string GetActivePathString()
        {
            var names = new List<string>();
            for (int i = 0; i < activePath.Count; i++) names.Add(activePath[i].Name);
            return string.Join(" / ", names);
        }

        public override string ToString() => Name + " [" + GetActivePathString() + "]";
    }
}
