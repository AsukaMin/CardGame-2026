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
    /// 卡池 JSON 读取（与 CardJsonWriter 严格对称的递归下降解析 + 反射装配）。
    /// 未来 Loader/ConfigDatabase 可直接复用本类把 GameData/*.json 装回 Battle 配置 POCO。
    /// </summary>
    public static class CardJsonReader
    {
        /// <summary>解析单个操作（SO 的 opJson 持久层 → 内存对象）。</summary>
        public static EffectOp ParseEffectOp(string json)
        {
            object root = new Parser(json).Parse();
            if (root is Dictionary<string, object> dict)
                return (EffectOp)ConvertValue(typeof(EffectOp), dict);
            throw new FormatException("效果操作 JSON 的根必须是对象");
        }

        public static List<EffectCardConfig> ParseEffectCards(string json)
        {
            object root = new Parser(json).Parse();
            if (root is Dictionary<string, object> dict && dict.TryGetValue("cards", out object cards))
                return (List<EffectCardConfig>)ConvertValue(typeof(List<EffectCardConfig>), cards);
            throw new FormatException("效果卡 JSON 缺少 \"cards\" 数组");
        }

        public static List<PartCardConfig> ParsePartCards(string json)
        {
            object root = new Parser(json).Parse();
            if (root is Dictionary<string, object> dict && dict.TryGetValue("parts", out object parts))
                return (List<PartCardConfig>)ConvertValue(typeof(List<PartCardConfig>), parts);
            throw new FormatException("部位卡 JSON 缺少 \"parts\" 数组");
        }

        // ---------- 反射装配 ----------

        static object ConvertValue(Type targetType, object jsonValue)
        {
            if (jsonValue == null) return null;

            if (targetType.IsEnum)
                return Enum.Parse(targetType, (string)jsonValue, false);
            if (targetType == typeof(int)) return Convert.ToInt32(jsonValue, CultureInfo.InvariantCulture);
            if (targetType == typeof(long)) return Convert.ToInt64(jsonValue, CultureInfo.InvariantCulture);
            if (targetType == typeof(float)) return Convert.ToSingle(jsonValue, CultureInfo.InvariantCulture);
            if (targetType == typeof(double)) return Convert.ToDouble(jsonValue, CultureInfo.InvariantCulture);
            if (targetType == typeof(bool)) return Convert.ToBoolean(jsonValue);
            if (targetType == typeof(string)) return (string)jsonValue;

            if (jsonValue is Dictionary<string, object> dict)
            {
                // 多态根：按判别键找具体类型（新增操作类型自动被发现，无需注册）
                if (typeof(EffectOp).IsAssignableFrom(targetType))
                {
                    Type t = Resolve(dict, "op", EffectOpTypes.Ops);
                    return FillNew(t, dict);
                }
                if (typeof(ConditionDef).IsAssignableFrom(targetType))
                {
                    Type t = Resolve(dict, "condition", EffectOpTypes.Conditions);
                    return FillNew(t, dict);
                }
                return FillNew(targetType, dict);
            }

            if (targetType.IsArray)
            {
                Type elem = targetType.GetElementType();
                if (!(jsonValue is List<object> list))
                    throw new FormatException($"{targetType.Name} 需要 JSON 数组");
                Array arr = Array.CreateInstance(elem, list.Count);
                for (int i = 0; i < list.Count; i++)
                    arr.SetValue(ConvertValue(elem, list[i]), i);
                return arr;
            }

            if (targetType.IsGenericType && targetType.GetGenericTypeDefinition() == typeof(List<>))
            {
                Type elem = targetType.GetGenericArguments()[0];
                if (!(jsonValue is List<object> list))
                    throw new FormatException($"{targetType.Name} 需要 JSON 数组");
                IList result = (IList)Activator.CreateInstance(targetType);
                foreach (object item in list)
                    result.Add(ConvertValue(elem, item));
                return result;
            }

            throw new FormatException($"无法把 JSON 值装配到类型 {targetType.Name}");
        }

        static Type Resolve(Dictionary<string, object> dict, string key, Dictionary<string, Type> registry)
        {
            if (!dict.TryGetValue(key, out object value) || !(value is string name))
                throw new FormatException($"缺少多态判别字段 \"{key}\"");
            if (!registry.TryGetValue(name, out Type t))
                throw new FormatException($"未注册的类型判别值 \"{name}\"（新增操作/条件须定义在 Battle 程序集）");
            return t;
        }

        static object FillNew(Type type, Dictionary<string, object> dict)
        {
            object obj = Activator.CreateInstance(type);
            foreach (FieldInfo f in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!TryGetValueCamel(dict, CardJsonWriter.Camelize(f.Name), out object v)) continue;
                f.SetValue(obj, ConvertValue(f.FieldType, v));
            }
            return obj;
        }

        static bool TryGetValueCamel(Dictionary<string, object> dict, string camelKey, out object value)
        {
            if (dict.TryGetValue(camelKey, out value)) return true;
            foreach (KeyValuePair<string, object> kv in dict)
            {
                if (string.Equals(kv.Key, camelKey, StringComparison.OrdinalIgnoreCase))
                {
                    value = kv.Value;
                    return true;
                }
            }
            return false;
        }

        /// <summary>效果操作/条件的类型注册表：扫描 Battle 程序集的子类，新类型自动纳入。</summary>
        internal static class EffectOpTypes
        {
            internal static readonly Dictionary<string, Type> Ops = new Dictionary<string, Type>();
            internal static readonly Dictionary<string, Type> Conditions = new Dictionary<string, Type>();

            static EffectOpTypes()
            {
                foreach (Type t in typeof(EffectOp).Assembly.GetTypes())
                {
                    if (t.IsAbstract) continue;
                    if (t.IsSubclassOf(typeof(EffectOp))) Ops[EffectJsonNames.Discriminator(t)] = t;
                    else if (t.IsSubclassOf(typeof(ConditionDef))) Conditions[EffectJsonNames.Discriminator(t)] = t;
                }
            }
        }

        // ---------- 递归下降解析器 ----------

        sealed class Parser
        {
            readonly string s;
            int i;

            public Parser(string json) => s = json ?? "";

            public object Parse()
            {
                object v = ParseValue();
                SkipWs();
                if (i < s.Length) throw Err("JSON 末尾有多余字符");
                return v;
            }

            object ParseValue()
            {
                SkipWs();
                if (i >= s.Length) throw Err("意外的输入结束");
                char c = s[i];
                switch (c)
                {
                    case '{': return ParseObject();
                    case '[': return ParseArray();
                    case '"': return ParseString();
                    case 't': Expect("true"); return true;
                    case 'f': Expect("false"); return false;
                    case 'n': Expect("null"); return null;
                    default: return ParseNumber();
                }
            }

            Dictionary<string, object> ParseObject()
            {
                i++; // {
                Dictionary<string, object> dict = new Dictionary<string, object>();
                SkipWs();
                if (Peek() == '}') { i++; return dict; }
                while (true)
                {
                    SkipWs();
                    string key = ParseString();
                    SkipWs();
                    if (NextChar() != ':') throw Err("期望 ':'");
                    dict[key] = ParseValue();
                    SkipWs();
                    char d = NextChar();
                    if (d == ',') continue;
                    if (d == '}') break;
                    throw Err("期望 ',' 或 '}'");
                }
                return dict;
            }

            List<object> ParseArray()
            {
                i++; // [
                List<object> list = new List<object>();
                SkipWs();
                if (Peek() == ']') { i++; return list; }
                while (true)
                {
                    list.Add(ParseValue());
                    SkipWs();
                    char d = NextChar();
                    if (d == ',') continue;
                    if (d == ']') break;
                    throw Err("期望 ',' 或 ']'");
                }
                return list;
            }

            string ParseString()
            {
                if (NextChar() != '"') throw Err("期望字符串");
                StringBuilder sb = new StringBuilder();
                while (true)
                {
                    if (i >= s.Length) throw Err("字符串未闭合");
                    char c = s[i++];
                    if (c == '"') break;
                    if (c != '\\') { sb.Append(c); continue; }
                    if (i >= s.Length) throw Err("转义序列未闭合");
                    char e = s[i++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'u':
                            if (i + 4 > s.Length) throw Err("\\u 转义不完整");
                            sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16));
                            i += 4;
                            break;
                        default: throw Err("非法转义字符");
                    }
                }
                return sb.ToString();
            }

            object ParseNumber()
            {
                int start = i;
                if (i < s.Length && (s[i] == '-' || s[i] == '+')) i++;
                bool isDouble = false;
                while (i < s.Length)
                {
                    char c = s[i];
                    if (char.IsDigit(c)) { i++; continue; }
                    if (c == '.' || c == 'e' || c == 'E' || c == '+' || c == '-') { isDouble = true; i++; continue; }
                    break;
                }
                string num = s.Substring(start, i - start);
                if (num.Length == 0) throw Err("非法数字");
                if (!isDouble && long.TryParse(num, NumberStyles.Integer, CultureInfo.InvariantCulture, out long l))
                    return l;
                if (double.TryParse(num, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
                    return d;
                throw Err($"无法解析数字 \"{num}\"");
            }

            void SkipWs()
            {
                while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
            }

            char Peek()
            {
                if (i >= s.Length) throw Err("意外的输入结束");
                return s[i];
            }

            char NextChar()
            {
                if (i >= s.Length) throw Err("意外的输入结束");
                return s[i++];
            }

            void Expect(string word)
            {
                if (i + word.Length > s.Length || s.Substring(i, word.Length) != word)
                    throw Err($"期望 \"{word}\"");
                i += word.Length;
            }

            FormatException Err(string message) => new FormatException($"JSON 解析错误（位置 {i}）：{message}");
        }
    }
}
