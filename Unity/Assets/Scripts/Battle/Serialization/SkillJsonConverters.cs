using System;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace Battle
{
    // ==================== 命名规则（读入/写出共用一处） ====================

    /// <summary>
    /// 字段→JSON 键名规则：camelCase；唯一改写 = ModOp 类型字段 → "mod"——
    /// "op" 是 EffectOp 层的判别字段名（{"op":"Damage"}），ModifyAttributeOp.Op / AttributeModifierDef.Op 须让位。
    /// 条件层的 CompareOperator 字段（AttributeCompareCondition.Op 等）仍叫 "op"：条件层判别字段是 "kind"，无冲突。
    /// </summary>
    static class BattleJsonNaming
    {
        static readonly CamelCaseNamingStrategy Camel = new CamelCaseNamingStrategy();

        public static string FieldName(FieldInfo field) =>
            field.FieldType == typeof(ModOp) ? "mod" : Camel.GetPropertyName(field.Name, false);
    }

    /// <summary>
    /// 与 BattleJsonNaming 同一规则的合同解析器：Populate 读入与普通对象（ScopedValue/GameplayEffectDef 等）
    /// 的成员命名走它；判别转换器写出时另按同规则手工写字段。
    /// </summary>
    sealed class BattleContractResolver : CamelCasePropertyNamesContractResolver
    {
        protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
        {
            JsonProperty property = base.CreateProperty(member, memberSerialization);
            if (member is FieldInfo field && field.FieldType == typeof(ModOp))
                property.PropertyName = "mod";
            return property;
        }
    }

    // ==================== 判别注册表 ====================

    /// <summary>
    /// 按字段声明序把公开字段写进 JObject：null 跳过（缺省读入=字段默认值）、保留键冲突即抛。
    /// DiscriminatedConverter 与 CardJsonConverter 共用——两处写出行为永远一致。
    /// </summary>
    static class BattleJsonFields
    {
        public static void WriteFields(JObject target, object value, JsonSerializer serializer, string reservedKey)
        {
            Type type = value.GetType();
            foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                string key = BattleJsonNaming.FieldName(field);
                if (key == reservedKey)
                    throw new InvalidOperationException(
                        $"{type.Name}.{field.Name} 的 JSON 键名与保留键 \"{key}\" 冲突——" +
                        "改字段名或加命名改写（参照 ModOp→\"mod\" 的处理），不写出歧义数据");
                object fieldValue = field.GetValue(value);
                if (fieldValue == null) continue; // NullValueHandling.Ignore：null 位不写出
                target.Add(key, JToken.FromObject(fieldValue, serializer));
            }
        }
    }

    /// <summary>
    /// 判别注册表："判别名 ↔ 类型" 双向表（读入按名查类型，写出按类型查名）。
    /// 与 engine.RegisterOp 同构（先登记后使用）；由 SkillJson 静态持有——启动期登记、加载期消费，
    /// 同 TagRegistry 的生命周期（engine 的实例注册表才对应"每场对局"）。
    /// </summary>
    public sealed class DiscriminatedRegistry<TBase> where TBase : class
    {
        readonly string kind; // 报错用（"操作"/"条件"）
        readonly Dictionary<string, Type> byName = new Dictionary<string, Type>(StringComparer.Ordinal);
        readonly Dictionary<Type, string> byType = new Dictionary<Type, string>();

        public DiscriminatedRegistry(string discriminator, string kind)
        {
            Discriminator = discriminator ?? throw new ArgumentNullException(nameof(discriminator));
            this.kind = kind ?? throw new ArgumentNullException(nameof(kind));
        }

        public string Discriminator { get; }

        /// <summary>全部已登记判别名（报错信息与巡检用）。</summary>
        public IEnumerable<string> Names => byName.Keys;

        public void Register<T>(string name) where T : TBase => Register(typeof(T), name);

        /// <summary>幂等登记：名字/类型已被占用或参数非法时返回 false（宿主反复构造时重复登记用）。</summary>
        public bool TryRegister<T>(string name) where T : TBase
        {
            if (string.IsNullOrEmpty(name)) return false;
            Type type = typeof(T);
            if (!typeof(TBase).IsAssignableFrom(type)) return false;
            if (byName.ContainsKey(name) || byType.ContainsKey(type)) return false;
            byName[name] = type;
            byType[type] = name;
            return true;
        }

        public void Register(Type type, string name)
        {
            if (string.IsNullOrEmpty(name))
                throw new InvalidOperationException($"判别名不能为空（{kind}注册表）");
            if (!typeof(TBase).IsAssignableFrom(type))
                throw new InvalidOperationException($"{type.Name} 不是 {typeof(TBase).Name} 的子类（{kind}注册表登记错误）");
            if (byName.TryGetValue(name, out Type owner))
                throw new InvalidOperationException($"判别名 \"{name}\" 已被 {owner.Name} 占用（{kind}注册表）");
            if (byType.ContainsKey(type))
                throw new InvalidOperationException($"{type.Name} 已登记为 \"{byType[type]}\"（{kind}注册表）");
            byName[name] = type;
            byType[type] = name;
        }

        /// <summary>读入：判别名 → 类型。未知即抛（列出全部已登记名——配置错误，加载期暴露）。</summary>
        public Type Resolve(string name)
        {
            if (!string.IsNullOrEmpty(name) && byName.TryGetValue(name, out Type type)) return type;
            throw new InvalidOperationException(
                $"未知{kind}判别值 \"{name}\"——已登记：{string.Join(", ", byName.Keys)}（配置错误，加载期暴露）");
        }

        /// <summary>写出：类型 → 判别名。未登记即抛（宿主自定义类型须先经 SkillJson 登记）。</summary>
        public string NameOf(Type type)
        {
            if (byType.TryGetValue(type, out string name)) return name;
            throw new InvalidOperationException(
                $"{type.Name} 未登记判别名——经 SkillJson.RegisterOp / RegisterCondition 登记（配置错误，加载期暴露）");
        }
    }

    // ==================== 判别转换器 ====================

    /// <summary>
    /// 判别字段多态转换器（EffectOp/ConditionDef 两层各一实例；未来卡牌信封 "kind":"monster"/"effect" 复用同款）。
    /// 读：取判别字段 → 查表 → 无参构造 → Populate 填充其余字段（判别字段无对应成员，自然跳过）。
    /// 写：判别属性置首 + 按字段声明序手工组装——手工写是为避开"转换器内走默认序列化→再次命中自己→无限递归"；
    /// 嵌套成员仍经完整 serializer，多态递归（Conditional→Then、ApplyEffect→周期组）照常。
    /// </summary>
    public sealed class DiscriminatedConverter<TBase> : JsonConverter where TBase : class
    {
        readonly DiscriminatedRegistry<TBase> registry;

        public DiscriminatedConverter(DiscriminatedRegistry<TBase> registry) =>
            this.registry = registry ?? throw new ArgumentNullException(nameof(registry));

        public override bool CanConvert(Type objectType) => typeof(TBase).IsAssignableFrom(objectType);

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            JObject obj = JObject.Load(reader);
            JToken discriminator = obj[registry.Discriminator];
            if (discriminator == null || discriminator.Type != JTokenType.String)
                throw new JsonSerializationException(
                    $"缺少判别字段 \"{registry.Discriminator}\"（{typeof(TBase).Name} 多态位）——" +
                    $"已登记：{string.Join(", ", registry.Names)}（配置错误，加载期暴露）");
            Type type = registry.Resolve((string)discriminator);
            object instance = Activator.CreateInstance(type); // Data 合同：公开字段 + 无参构造
            using (JsonReader populateReader = obj.CreateReader())
                serializer.Populate(populateReader, instance);
            return instance;
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            Type type = value.GetType();
            JObject obj = new JObject();
            obj.Add(new JProperty(registry.Discriminator, registry.NameOf(type)));
            BattleJsonFields.WriteFields(obj, value, serializer, registry.Discriminator);
            obj.WriteTo(writer);
        }
    }
}
