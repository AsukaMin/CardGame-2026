using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Battle
{
    /// <summary>
    /// 序列化门面（唯一公共入口）：JSON 字符串 ↔ Data 层 POCO。只做转换零 IO——读文件永远在调用方（目录纪律）。
    /// 格式四决策（未来技能编辑器直接读写的合同）：
    /// 1. 字段名 camelCase（amount/condition/then）；唯一改写 ModOp→"mod"（"op" 让位给判别字段，见 BattleJsonNaming）。
    /// 2. 枚举一律字符串原样名、拒绝数字（"Const"/"Chosen"）。
    /// 3. null 位省略（缺省读入 = 字段默认值，"null 数组视为空"）。
    /// 4. 默认值照写——数据自解释，将来改代码默认值不会悄悄改变老 JSON 的语义（补丁纪律）。
    /// 手写最小 JSON 合法（省略=取默认）；未知判别值/非法枚举名/未知标签名反序列化即抛——不带病运行。
    /// </summary>
    public static class SkillJson
    {
        /// <summary>EffectOp 判别注册表："op"（8 内置 + 宿主 op 经 RegisterOp 追加）。</summary>
        static readonly DiscriminatedRegistry<EffectOp> OpRegistry = new DiscriminatedRegistry<EffectOp>("op", "操作");

        /// <summary>ConditionDef 判别注册表："kind"（3 内置，扩条件经 RegisterCondition 追加）。</summary>
        static readonly DiscriminatedRegistry<ConditionDef> ConditionRegistry =
            new DiscriminatedRegistry<ConditionDef>("kind", "条件");

        static readonly JsonSerializerSettings Settings = CreateSettings();

        static SkillJson()
        {
            OpRegistry.Register<DamageOp>("Damage");
            OpRegistry.Register<HealOp>("Heal");
            OpRegistry.Register<ModifyAttributeOp>("ModifyAttribute");
            OpRegistry.Register<GrantTagOp>("GrantTag");
            OpRegistry.Register<RemoveTagOp>("RemoveTag");
            OpRegistry.Register<ApplyEffectOp>("ApplyEffect");
            OpRegistry.Register<ConditionalOp>("Conditional");
            OpRegistry.Register<ModifyEventMagnitudeOp>("ModifyEventMagnitude");
            ConditionRegistry.Register<AttributeCompareCondition>("AttributeCompare");
            ConditionRegistry.Register<EventValueCondition>("EventValue");
            ConditionRegistry.Register<HasTagCondition>("HasTag");
        }

        /// <summary>登记宿主自定义 op 的判别名（镜像 engine.RegisterOp 的扩展点；先登记后序列化）。</summary>
        public static void RegisterOp<TOp>(string name) where TOp : EffectOp => OpRegistry.Register<TOp>(name);

        /// <summary>幂等登记宿主自定义 op（名字/类型已被占用返回 false——每场对局都构造的宿主用）。</summary>
        public static bool TryRegisterOp<TOp>(string name) where TOp : EffectOp => OpRegistry.TryRegister<TOp>(name);

        /// <summary>登记自定义条件类型的判别名（3 内置条件之外扩条件时用）。</summary>
        public static void RegisterCondition<TCondition>(string name) where TCondition : ConditionDef =>
            ConditionRegistry.Register<TCondition>(name);

        /// <summary>序列化（缩进排版——补丁文件 diff 友好）。</summary>
        public static string Serialize<T>(T value) =>
            JsonConvert.SerializeObject(value, Formatting.Indented, Settings);

        /// <summary>反序列化 + 数据巡检（标签位逐个 TagRegistry.Resolve，失败即抛——加载期暴露，不带病运行）。</summary>
        public static T Deserialize<T>(string json)
        {
            T result = JsonConvert.DeserializeObject<T>(json, Settings);
            SkillJsonValidator.Validate(result);
            return result;
        }

        static JsonSerializerSettings CreateSettings() => new JsonSerializerSettings
        {
            ContractResolver = new BattleContractResolver(),
            NullValueHandling = NullValueHandling.Ignore,
            Converters =
            {
                new DiscriminatedConverter<EffectOp>(OpRegistry),
                new DiscriminatedConverter<ConditionDef>(ConditionRegistry),
                new CardJsonConverter(),
                new StringEnumConverter { AllowIntegerValues = false },
            },
        };
    }
}
