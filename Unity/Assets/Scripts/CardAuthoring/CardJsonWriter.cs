using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using Battle;

namespace CardAuthoring
{
    /// <summary>
    /// 卡池 JSON 导出（手写极简 writer，零第三方依赖）。
    /// 规则：公共字段反射序列化、字段名 camelCase、枚举输出名、
    /// 多态根加判别键（EffectOp→"op" / ConditionDef→"condition"，键值=类名去后缀）。
    /// 该格式同时是 SO 内 opJson 持久层与 GameData 导出格式——与 CardJsonReader 严格对称。
    /// </summary>
    public static class CardJsonWriter
    {
        /// <summary>单个操作序列化（SO 的 opJson 持久层）。</summary>
        public static string WriteEffectOp(EffectOp op) => op == null ? "null" : WriteValueString(op, 0);

        /// <summary>效果卡池导出（单文件聚合，按 id 升序稳定排序）。</summary>
        public static string WriteEffectCards(IEnumerable<EffectCardConfig> cards)
        {
            List<EffectCardConfig> sorted = new List<EffectCardConfig>(cards ?? Array.Empty<EffectCardConfig>());
            sorted.Sort((a, b) => a.Id.CompareTo(b.Id));
            StringBuilder sb = new StringBuilder();
            sb.Append("{\n  \"cards\":");
            AppendEnumerable(sb, sorted, 1);
            sb.Append("\n}");
            return sb.ToString();
        }

        /// <summary>部位卡池导出（同上）。</summary>
        public static string WritePartCards(IEnumerable<PartCardConfig> parts)
        {
            List<PartCardConfig> sorted = new List<PartCardConfig>(parts ?? Array.Empty<PartCardConfig>());
            sorted.Sort((a, b) => a.Id.CompareTo(b.Id));
            StringBuilder sb = new StringBuilder();
            sb.Append("{\n  \"parts\":");
            AppendEnumerable(sb, sorted, 1);
            sb.Append("\n}");
            return sb.ToString();
        }

        static string WriteValueString(object value, int depth)
        {
            StringBuilder sb = new StringBuilder();
            AppendValue(sb, value, depth);
            return sb.ToString();
        }

        static void AppendValue(StringBuilder sb, object value, int depth)
        {
            switch (value)
            {
                case null:
                    sb.Append("null");
                    break;
                case bool b:
                    sb.Append(b ? "true" : "false");
                    break;
                case int i:
                    sb.Append(i.ToString(CultureInfo.InvariantCulture));
                    break;
                case long l:
                    sb.Append(l.ToString(CultureInfo.InvariantCulture));
                    break;
                case float f:
                    AppendDouble(sb, f);
                    break;
                case double d:
                    AppendDouble(sb, d);
                    break;
                case string str:
                    AppendString(sb, str);
                    break;
                case Enum e:
                    AppendString(sb, e.ToString());
                    break;
                case IEnumerable enumerable:
                    AppendEnumerable(sb, enumerable, depth);
                    break;
                default:
                    AppendObject(sb, value, depth);
                    break;
            }
        }

        static void AppendObject(StringBuilder sb, object value, int depth)
        {
            string discKey = null;
            string discName = null;
            if (value is EffectOp) { discKey = "op"; }
            else if (value is ConditionDef) { discKey = "condition"; }
            if (discKey != null) discName = EffectJsonNames.Discriminator(value.GetType());

            FieldInfo[] fields = value.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance);
            bool empty = discKey == null && fields.Length == 0;
            if (empty) { sb.Append("{}"); return; }

            sb.Append("{\n");
            Indent(sb, depth + 1);
            if (discKey != null)
            {
                sb.Append('\"').Append(discKey).Append("\": ");
                AppendString(sb, discName);
            }
            bool first = discKey == null;
            foreach (FieldInfo f in fields)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append('\n');
                Indent(sb, depth + 1);
                AppendString(sb, Camelize(f.Name));
                sb.Append(": ");
                AppendValue(sb, f.GetValue(value), depth + 1);
            }
            sb.Append('\n');
            Indent(sb, depth);
            sb.Append('}');
        }

        static void AppendEnumerable(StringBuilder sb, IEnumerable enumerable, int depth)
        {
            sb.Append('[');
            bool first = true;
            foreach (object item in enumerable)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append('\n');
                Indent(sb, depth + 1);
                AppendValue(sb, item, depth + 1);
            }
            if (first) { sb.Append(']'); return; }
            sb.Append('\n');
            Indent(sb, depth);
            sb.Append(']');
        }

        static void AppendDouble(StringBuilder sb, double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d)) { sb.Append("null"); return; }
            sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
        }

        static void AppendString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        static void Indent(StringBuilder sb, int depth)
        {
            for (int i = 0; i < depth; i++) sb.Append("  ");
        }

        internal static string Camelize(string name) =>
            string.IsNullOrEmpty(name) ? name : char.ToLowerInvariant(name[0]) + name.Substring(1);
    }

    /// <summary>判别键命名（writer/reader 共用，防两侧规则漂移）。</summary>
    internal static class EffectJsonNames
    {
        /// <summary>类名去 "Op"/"Condition" 后缀：DamageOp→Damage，TargetHpBelowPercentCondition→TargetHpBelowPercent。</summary>
        public static string Discriminator(Type t)
        {
            string n = t.Name;
            if (n.EndsWith("Condition")) return n.Substring(0, n.Length - "Condition".Length);
            if (n.EndsWith("Op")) return n.Substring(0, n.Length - "Op".Length);
            return n;
        }
    }
}
