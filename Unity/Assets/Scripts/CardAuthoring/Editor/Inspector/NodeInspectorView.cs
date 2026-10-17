using System;
using System.Collections.Generic;
using System.Reflection;
using Battle;
using UnityEditor.Experimental.GraphView;
using UnityEngine.UIElements;

namespace CardAuthoring.Editor
{
    /// <summary>
    /// 节点参数面板：效果组节点编辑目标模式；操作节点按反射字段自动生成表单
    /// （int/float/double/bool/string/enum 开箱即编辑，未来新操作零代码接入）。
    /// 条件分支节点额外提供条件类型下拉与条件参数表单；Then/Else 内容即图连线，不在此编辑。
    /// </summary>
    public class NodeInspectorView : VisualElement
    {
        public void Show(GraphElement selection, Action onDirty)
        {
            Clear();
            switch (selection)
            {
                case null:
                    Add(new Label("（选择节点查看参数）") { style = { color = UnityEngine.Color.gray } });
                    break;

                case EffectDefNode defNode:
                    BuildEffectDefForm(defNode, onDirty);
                    break;

                case OpNode opNode:
                    BuildOpForm(opNode, onDirty);
                    break;
            }
        }

        void BuildEffectDefForm(EffectDefNode node, Action onDirty)
        {
            Add(SectionTitle("效果组"));
            var field = new EnumField("目标模式", node.Data.target);
            field.RegisterValueChangedCallback(evt =>
            {
                node.Data.target = (TargetMode)evt.newValue;
                node.RefreshTargetLabel();
                onDirty?.Invoke();
            });
            Add(field);
        }

        void BuildOpForm(OpNode node, Action onDirty)
        {
            Add(SectionTitle($"节点参数（{node.title}）"));
            EffectOp op = node.Data.GetOp();
            if (op == null)
            {
                Add(new Label("（空节点）") { style = { color = UnityEngine.Color.gray } });
                return;
            }
            BuildFieldsFor(op, node.Data, node, onDirty);

            if (op is ConditionalOp cond)
                BuildConditionForm(cond, node.Data, node, onDirty);
        }

        void BuildFieldsFor(object target, OpNodeData data, OpNode node, Action onDirty)
        {
            foreach (FieldInfo f in target.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (target is ConditionalOp && (f.Name == nameof(ConditionalOp.Then) || f.Name == nameof(ConditionalOp.Else)))
                    continue; // 分支内容由图连线表达
                if (target is ConditionalOp && f.Name == nameof(ConditionalOp.Condition))
                    continue; // 条件由专属表单编辑
                Add(MakeField(f.Name, f.FieldType, target, f, () =>
                {
                    data.SetOp(data.GetOp()); // 同步 opJson 持久层（表单打开时 op 必非空）
                    node?.RefreshPresentation();
                    onDirty?.Invoke();
                }));
            }
        }

        void BuildConditionForm(ConditionalOp cond, OpNodeData data, OpNode node, Action onDirty)
        {
            Add(SectionTitle("分支条件"));

            List<string> conditionNames = new List<string>(CardJsonReader.EffectOpTypes.Conditions.Keys);
            conditionNames.Sort();
            int currentIndex = cond.Condition == null ? -1
                : conditionNames.IndexOf(OpNodeRegistry.ConditionDisplayName(cond.Condition.GetType()));

            var picker = new DropdownField("条件类型", conditionNames, currentIndex);
            picker.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue == null) return;
                cond.Condition = CardJsonReader.EffectOpTypes.Conditions.TryGetValue(evt.newValue, out Type t)
                    ? (ConditionDef)Activator.CreateInstance(t)
                    : null;
                data.SetOp(cond);
                node?.RefreshPresentation();
                onDirty?.Invoke();
                Show(node, onDirty); // 重建以显示新条件的参数表单
            });
            Add(picker);

            if (cond.Condition != null)
            {
                VisualElement box = new VisualElement();
                box.style.marginLeft = 12;
                foreach (FieldInfo f in cond.Condition.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
                    box.Add(MakeField(f.Name, f.FieldType, cond.Condition, f, () =>
                    {
                        data.SetOp(cond);
                        node?.RefreshPresentation();
                        onDirty?.Invoke();
                    }));
                Add(box);
            }
        }

        VisualElement MakeField(string name, Type fieldType, object target, FieldInfo info, Action changed)
        {
            VisualElement row = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            Label label = new Label(name) { style = { width = 110, alignSelf = Align.Center } };
            row.Add(label);

            VisualElement control;
            if (fieldType.IsEnum)
            {
                var f = new EnumField((System.Enum)info.GetValue(target));
                f.RegisterValueChangedCallback(evt => { info.SetValue(target, evt.newValue); changed(); });
                control = f;
            }
            else if (fieldType == typeof(int))
            {
                var f = new IntegerField { value = (int)info.GetValue(target), style = { flexGrow = 1 } };
                f.RegisterValueChangedCallback(evt => { info.SetValue(target, evt.newValue); changed(); });
                control = f;
            }
            else if (fieldType == typeof(float))
            {
                var f = new FloatField { value = (float)info.GetValue(target), style = { flexGrow = 1 } };
                f.RegisterValueChangedCallback(evt => { info.SetValue(target, evt.newValue); changed(); });
                control = f;
            }
            else if (fieldType == typeof(double))
            {
                var f = new DoubleField { value = (double)info.GetValue(target), style = { flexGrow = 1 } };
                f.RegisterValueChangedCallback(evt => { info.SetValue(target, evt.newValue); changed(); });
                control = f;
            }
            else if (fieldType == typeof(bool))
            {
                var f = new Toggle { value = (bool)info.GetValue(target) };
                f.RegisterValueChangedCallback(evt => { info.SetValue(target, evt.newValue); changed(); });
                control = f;
            }
            else if (fieldType == typeof(string))
            {
                var f = new TextField { value = (string)info.GetValue(target), style = { flexGrow = 1 } };
                f.RegisterValueChangedCallback(evt => { info.SetValue(target, evt.newValue); changed(); });
                control = f;
            }
            else
            {
                control = new Label($"（暂不支持编辑：{fieldType.Name}）") { style = { color = UnityEngine.Color.gray } };
            }
            row.Add(control);
            return row;
        }

        static Label SectionTitle(string text) => new Label(text)
        {
            style =
            {
                unityFontStyleAndWeight = UnityEngine.FontStyle.Bold,
                marginTop = 8,
                marginBottom = 4,
            },
        };
    }
}
