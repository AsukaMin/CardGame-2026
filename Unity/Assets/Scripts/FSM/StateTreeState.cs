using System.Collections.Generic;

namespace FSM
{
    /// <summary>
    /// 转换（对应 UE State Tree Transition）：
    /// - Conditions 是条件列表，全部满足才允许切换（与语义）；
    /// - Event 非空时，还要求恰好收到了同名事件（SendEvent 触发，单次有效）；
    /// - Type 决定目标：Goto 指定状态（可跨层级）、Next 同级下一个、None 留在原地。
    /// </summary>
    public class StateTreeTransition
    {
        public TransitionType Type;
        public string Target;
        public string Event;

        public readonly List<StateTreeCondition> Conditions = new List<StateTreeCondition>();

        public StateTreeTransition(TransitionType type, string target = null, string @event = null)
        {
            Type = type;
            Target = target;
            Event = @event;
        }

        public bool ShouldTransition(StateTree tree)
        {
            if (!string.IsNullOrEmpty(Event) && !tree.IsEventActive(Event)) return false;

            for (int i = 0; i < Conditions.Count; i++)
            {
                if (!Conditions[i].IsMet(tree)) return false;
            }
            return true;
        }
    }

    /// <summary>
    /// 状态（对应 UE State Tree State）：
    /// - Tasks：状态的行为，进入时全部激活；
    /// - Transitions：按 RunMode 分三组（进入时 / 每帧 / 完成时）求值；
    /// - Children：子状态。进入本状态后自动进入 InitialChild 链直到叶子，
    ///   父状态的 Task 与 Transition 在子状态激活期间持续生效（求值顺序：叶子优先，向上冒泡）。
    /// </summary>
    public class StateTreeState
    {
        public readonly string Name;

        public readonly List<StateTreeTask> Tasks = new List<StateTreeTask>();
        public readonly List<StateTreeState> Children = new List<StateTreeState>();

        public readonly List<StateTreeTransition> EnterTransitions = new List<StateTreeTransition>();
        public readonly List<StateTreeTransition> TickTransitions = new List<StateTreeTransition>();
        public readonly List<StateTreeTransition> CompletedTransitions = new List<StateTreeTransition>();

        public StateTreeState Parent { get; private set; }
        public StateTreeState InitialChild { get; private set; }

        public StateTreeState(string name)
        {
            Name = name;
        }

        // ---- 构建接口（链式）----

        public StateTreeState AddTask(StateTreeTask task)
        {
            Tasks.Add(task);
            return this;
        }

        public StateTreeState AddChild(StateTreeState child, bool initial = true)
        {
            child.Parent = this;
            Children.Add(child);
            if (initial && InitialChild == null) InitialChild = child;
            return this;
        }

        public StateTreeState AddTransition(StateTreeTransition t, TransitionRunMode mode)
        {
            switch (mode)
            {
                case TransitionRunMode.OnEnter: EnterTransitions.Add(t); break;
                case TransitionRunMode.OnTick: TickTransitions.Add(t); break;
                case TransitionRunMode.OnCompleted: CompletedTransitions.Add(t); break;
            }
            return this;
        }

        /// <summary>便捷方法：Goto 转换（可指定事件与条件）</summary>
        public StateTreeState AddGotoTransition(string target, string @event = null, params StateTreeCondition[] conditions)
        {
            return AddConditions(new StateTreeTransition(TransitionType.Goto, target, @event), TransitionRunMode.OnTick, conditions);
        }

        /// <summary>便捷方法：进入时立即求值的 Goto</summary>
        public StateTreeState AddGotoOnEnterTransition(string target, string @event = null, params StateTreeCondition[] conditions)
        {
            return AddConditions(new StateTreeTransition(TransitionType.Goto, target, @event), TransitionRunMode.OnEnter, conditions);
        }

        /// <summary>便捷方法：状态完成（任一任务 Succeeded/Failed）时求值的 Goto</summary>
        public StateTreeState AddGotoOnCompletedTransition(string target, string @event = null, params StateTreeCondition[] conditions)
        {
            return AddConditions(new StateTreeTransition(TransitionType.Goto, target, @event), TransitionRunMode.OnCompleted, conditions);
        }

        /// <summary>便捷方法：切到同级下一个状态</summary>
        public StateTreeState AddNextTransition(string @event = null, params StateTreeCondition[] conditions)
        {
            return AddConditions(new StateTreeTransition(TransitionType.Next, null, @event), TransitionRunMode.OnTick, conditions);
        }



        private StateTreeState AddConditions(StateTreeTransition t, TransitionRunMode mode, StateTreeCondition[] conditions)
        {
            if (conditions != null) t.Conditions.AddRange(conditions);
            return AddTransition(t, mode);
        }

        public override string ToString() => Name;
    }
}
