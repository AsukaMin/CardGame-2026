using System;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace CardAuthoring.Editor
{
    /// <summary>通用操作节点：输入端口 + "继续"顺序输出端口；描述符声明的分支端口（如条件的是/否）。</summary>
    public class OpNode : Node
    {
        public readonly OpNodeData Data;
        readonly Label summary;
        readonly Action notifyDirty;

        public OpNode(OpNodeData data, Action onDirty)
        {
            Data = data;
            notifyDirty = onDirty;
            summary = new Label { style = { color = UnityEngine.Color.gray } };
            extensionContainer.Add(summary);

            Port input = InstantiatePort(Orientation.Horizontal, Direction.Input, Port.Capacity.Single, typeof(float));
            input.portName = "";
            input.userData = "in";
            inputContainer.Add(input);

            Port next = InstantiatePort(Orientation.Horizontal, Direction.Output, Port.Capacity.Single, typeof(float));
            next.portName = PortNames.Next;
            next.userData = PortNames.Next;
            outputContainer.Add(next);

            OpNodeDescriptor descriptor = OpNodeRegistry.Find(Data.GetOp()?.GetType());
            if (descriptor != null)
            {
                foreach (string branch in descriptor.BranchPorts)
                {
                    Port p = InstantiatePort(Orientation.Horizontal, Direction.Output, Port.Capacity.Single, typeof(float));
                    p.portName = branch;
                    p.userData = branch;
                    outputContainer.Add(p);
                }
            }

            RefreshPresentation();
            SetPosition(new Rect(Data.x, Data.y, 0, 0));
        }

        /// <summary>参数被 Inspector 修改后刷新标题摘要。</summary>
        public void RefreshPresentation()
        {
            Battle.EffectOp op = Data.GetOp();
            OpNodeDescriptor descriptor = OpNodeRegistry.Find(op?.GetType());
            title = descriptor?.Title ?? (op == null ? "空操作" : op.GetType().Name);
            summary.text = op == null ? "（空节点，导出时跳过）" : descriptor?.Summarize?.Invoke(op) ?? "";
            notifyDirty?.Invoke();
        }
    }
}
