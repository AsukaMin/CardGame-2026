namespace FSM
{
    /// <summary>
    /// 类型化共享上下文（对应 UE State Tree 的 Context Data）。
    /// 用户继承它并声明字段，状态的任务与条件直接读写类型化字段，没有字符串键值。
    /// </summary>
    public abstract class StateTreeContext { }

    /// <summary>任务运行状态（对应 UE EStateTreeRunStatus）：Running 继续执行，Succeeded/Failed 表示状态完成</summary>
    public enum TaskStatus
    {
        Running,
        Succeeded,
        Failed,
    }

    /// <summary>转换目标类型：Goto 跳到指定状态（可跨层级），Next 跳到同级下一个状态，None 留在当前状态</summary>
    public enum TransitionType
    {
        Goto,
        Next,
        None,
    }

    /// <summary>转换的求值时机（对应 UE State Tree 转换的 Enter/Tick/Completed 插槽）</summary>
    public enum TransitionRunMode
    {
        /// <summary>状态被进入时立即求值一次</summary>
        OnEnter,

        /// <summary>每帧求值</summary>
        OnTick,

        /// <summary>状态内任务完成（Succeeded/Failed）时求值</summary>
        OnCompleted,
    }
}
