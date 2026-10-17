using System;
using System.Collections.Generic;
using Battle;

namespace CardAuthoring
{
    /// <summary>端口名约定：GraphView 端口与编译器共用的连线键（中文即显示名，仿参考项目）。</summary>
    public static class PortNames
    {
        /// <summary>效果组 → 第一个操作。</summary>
        public const string Ops = "操作";
        /// <summary>操作 → 下一个操作（顺序链）。</summary>
        public const string Next = "继续";
        /// <summary>条件分支：真。</summary>
        public const string Then = "是";
        /// <summary>条件分支：假。</summary>
        public const string Else = "否";
    }

    /// <summary>效果组节点（图上的根）：目标选择 + 一条操作链的入口，对应运行时一个 EffectDef。</summary>
    [Serializable]
    public class EffectDefNodeData
    {
        public string guid = Guid.NewGuid().ToString("N");
        public float x, y;
        public TargetMode target = TargetMode.Chosen;
    }

    /// <summary>
    /// 操作节点：编辑期为图上节点。op 子树以 opJson 字符串持久化在 SO 里——
    /// 多态序列化由自家 writer/reader 承担（不依赖 SerializeReference），
    /// 因此 Battle 的 op 类改名/迁移不丢资产数据；内存 op 仅为解码缓存（NonSerialized）。
    /// </summary>
    [Serializable]
    public class OpNodeData
    {
        public string guid = Guid.NewGuid().ToString("N");
        public float x, y;
        public string opJson = "";

        [NonSerialized] public EffectOp op;

        /// <summary>取内存 op（首次访问时从 opJson 解码）。</summary>
        public EffectOp GetOp() => op ?? (op = string.IsNullOrEmpty(opJson) ? null : CardJsonReader.ParseEffectOp(opJson));

        /// <summary>写入 op（同时序列化进 opJson 持久层）。</summary>
        public void SetOp(EffectOp value)
        {
            op = value;
            opJson = value == null ? "" : CardJsonWriter.WriteEffectOp(value);
        }
    }

    /// <summary>连线：输出节点 guid + 输出端口名 → 输入节点 guid（输入端口唯一，无需名）。</summary>
    [Serializable]
    public class ConnectionData
    {
        public string outputNodeGuid;
        public string outputPort;
        public string inputNodeGuid;
    }
}
