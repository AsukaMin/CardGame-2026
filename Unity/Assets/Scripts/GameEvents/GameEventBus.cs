using System;
using System.Collections.Generic;

namespace GameEvents
{
    /// <summary>
    /// 全局事件总线（struct 事件 + 泛型订阅，类型即键——零字符串零反射，编译期全检）。
    /// 立即同步派发（订阅序）；回调内再 Send 抛异常（同步链式派发是时序灾难源头）。
    /// 静态总线跨场景存活：订阅/退订必须成对。设计见《单机卡牌游戏-事件系统设计》。
    /// </summary>
    public static class GameEventBus
    {
        static readonly Dictionary<Type, List<Delegate>> handlers = new Dictionary<Type, List<Delegate>>();
        static bool dispatching;

        public static void Subscribe<T>(Action<T> handler) where T : struct
        {
            if (handler == null) return;
            if (!handlers.TryGetValue(typeof(T), out List<Delegate> list))
                handlers[typeof(T)] = list = new List<Delegate>();
            if (!list.Contains(handler)) list.Add(handler);
        }

        public static void Unsubscribe<T>(Action<T> handler) where T : struct
        {
            if (handler == null) return;
            if (handlers.TryGetValue(typeof(T), out List<Delegate> list))
            {
                list.Remove(handler);
                if (list.Count == 0) handlers.Remove(typeof(T));
            }
        }

        public static void Send<T>(in T evt) where T : struct
        {
            if (dispatching)
                throw new InvalidOperationException(
                    "[GameEventBus] 订阅者回调内禁止再 Send——同步链式派发是时序灾难的源头");
            if (!handlers.TryGetValue(typeof(T), out List<Delegate> list) || list.Count == 0) return;

            Delegate[] snapshot = list.ToArray(); // 快照遍历：允许回调内退订
            dispatching = true;
            try
            {
                foreach (Delegate d in snapshot)
                    if (list.Contains(d)) // 已退订的跳过
                        ((Action<T>)d)(evt);
            }
            finally
            {
                dispatching = false;
            }
        }

        /// <summary>清空全部订阅。仅测试用，业务代码禁调。</summary>
        public static void ClearAll() => handlers.Clear();
    }
}
