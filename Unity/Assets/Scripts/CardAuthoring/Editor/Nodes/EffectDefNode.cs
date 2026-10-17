using System;
using Battle;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace CardAuthoring.Editor
{
    /// <summary>效果组根节点：图上的"效果定义"入口（目标选择 + 一条操作链）。目标模式在右侧 Inspector 编辑。</summary>
    public class EffectDefNode : Node
    {
        public readonly EffectDefNodeData Data;
        readonly Label targetLabel;
        readonly Action notifyDirty;

        public EffectDefNode(EffectDefNodeData data, Action onDirty)
        {
            Data = data;
            notifyDirty = onDirty;
            title = "效果组";

            targetLabel = new Label($"目标：{Data.target}");
            extensionContainer.Add(targetLabel);

            Port port = InstantiatePort(Orientation.Horizontal, Direction.Output, Port.Capacity.Single, typeof(float));
            port.portName = PortNames.Ops;
            port.userData = PortNames.Ops;
            outputContainer.Add(port);

            RefreshExpandedState();
            SetPosition(new Rect(Data.x, Data.y, 0, 0));
        }

        /// <summary>Inspector 修改目标模式后同步节点显示。</summary>
        public void RefreshTargetLabel()
        {
            targetLabel.text = $"目标：{Data.target}";
            notifyDirty?.Invoke();
        }
    }
}
