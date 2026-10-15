using System;

namespace FSM
{
    /// <summary>条件基类（对应 UE State Tree Condition）：只做判断，不改数据</summary>
    public abstract class StateTreeCondition
    {
        public abstract bool IsMet(StateTree tree);
    }

    /// <summary>函数式条件</summary>
    public class LambdaCondition : StateTreeCondition
    {
        private readonly Func<StateTree, bool> check;
        public LambdaCondition(Func<StateTree, bool> check) { this.check = check ?? (_ => false); }
        public override bool IsMet(StateTree tree) => check(tree);
    }

    /// <summary>
    /// 类型化上下文条件：直接读 Context 上的字段判断，取代黑板。
    /// 例如 new ContextCondition&lt;EnemyCtx&gt;(c =&gt; c.Hp &lt;= 0)
    /// </summary>
    public class ContextCondition<T> : StateTreeCondition where T : StateTreeContext
    {
        private readonly Func<T, bool> check;
        public ContextCondition(Func<T, bool> check) { this.check = check ?? (_ => false); }

        public override bool IsMet(StateTree tree) => tree.Context is T ctx && check(ctx);
    }

    /// <summary>状态停留时间条件：在当前状态超过指定秒数时成立</summary>
    public class TimeInStateCondition : StateTreeCondition
    {
        private readonly float seconds;
        public TimeInStateCondition(float seconds) { this.seconds = seconds; }
        public override bool IsMet(StateTree tree) => tree.TimeInState >= seconds;
    }

    /// <summary>取反条件</summary>
    public class NotCondition : StateTreeCondition
    {
        private readonly StateTreeCondition inner;
        public NotCondition(StateTreeCondition inner) { this.inner = inner; }
        public override bool IsMet(StateTree tree) => !inner.IsMet(tree);
    }
}
