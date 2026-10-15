using UnityEngine;

namespace FSM
{
    /// <summary>
    /// 任务基类（对应 UE State Tree Task）。
    /// 一个状态可以挂多个任务，进入状态时全部 OnEnter，每帧全部 OnTick，退出时全部 OnExit。
    /// 任务自身的字段就是实例数据（如持续时间、目标点等），直接声明即可。
    /// </summary>
    public abstract class StateTreeTask
    {
        /// <summary>进入状态时调用</summary>
        public virtual void OnEnter(StateTree tree, StateTreeState state) { }

        /// <summary>每帧调用。返回 Succeeded/Failed 表示状态完成，会触发该状态的 OnCompleted 转换</summary>
        public virtual TaskStatus OnTick(StateTree tree, StateTreeState state, float dt)
        {
            return TaskStatus.Succeeded;
        }

        /// <summary>退出状态时调用（无论正常切换还是被打断）</summary>
        public virtual void OnExit(StateTree tree, StateTreeState state) { }
    }

    /// <summary>日志任务：进入状态时打一条日志，调试用</summary>
    public class LogTask : StateTreeTask
    {
        private readonly string message;
        public LogTask(string message) { this.message = message; }
        public override void OnEnter(StateTree tree, StateTreeState state)
        {
            Debug.Log("[StateTree:" + tree.Name + "] " + message);
        }
    }

    /// <summary>等待任务：持续指定秒数后返回 Succeeded，驱动 OnCompleted 转换</summary>
    public class WaitTask : StateTreeTask
    {
        private readonly float seconds;
        private float elapsed;

        public WaitTask(float seconds) { this.seconds = seconds; }

        public override void OnEnter(StateTree tree, StateTreeState state) { elapsed = 0f; }

        public override TaskStatus OnTick(StateTree tree, StateTreeState state, float dt)
        {
            elapsed += dt;
            return elapsed >= seconds ? TaskStatus.Succeeded : TaskStatus.Running;
        }
    }

    /// <summary>函数式任务：用 lambda 描述行为，简单状态不必建子类</summary>
    public class LambdaTask : StateTreeTask
    {
        private readonly System.Action<StateTree, StateTreeState> onEnter;
        private readonly System.Func<StateTree, StateTreeState, float, TaskStatus> onTick;
        private readonly System.Action<StateTree, StateTreeState> onExit;

        public LambdaTask(
            System.Action<StateTree, StateTreeState> onEnter = null,
            System.Func<StateTree, StateTreeState, float, TaskStatus> onTick = null,
            System.Action<StateTree, StateTreeState> onExit = null)
        {
            this.onEnter = onEnter;
            this.onTick = onTick;
            this.onExit = onExit;
        }

        public override void OnEnter(StateTree tree, StateTreeState state) => onEnter?.Invoke(tree, state);
        public override TaskStatus OnTick(StateTree tree, StateTreeState state, float dt) =>
            onTick?.Invoke(tree, state, dt) ?? TaskStatus.Running;
        public override void OnExit(StateTree tree, StateTreeState state) => onExit?.Invoke(tree, state);
    }
}
