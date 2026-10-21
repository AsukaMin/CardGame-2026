using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Battle
{
    /// <summary>
    /// 卡牌 JSON 信封（cards.json 的一个元素）：id（表行）+ 卡体（BattleCard 联合，判别 "kind"）。
    /// "表行"（有 id 的配置条目）与"卡体"（玩法定义）是两层概念——id 只在信封，卡定义保持纯净；
    /// 运行时卡类型零新增，牌库/手牌链路（List&lt;BattleCard&gt;）原样吃。
    /// </summary>
    public sealed class CardEntry
    {
        public int Id;
        public BattleCard Card;
    }

    /// <summary>
    /// 卡牌扁平信封转换器：{"kind":"monster"/"effect", id, name, cost, + attack/maxHp | effects}。
    /// 专用而非 DiscriminatedConverter 第三实例——扁平形状要求卡体字段内联进信封（无嵌套卡体层），
    /// kind 值是全小写字面量、两个卡定义又无公共基类，通用转换器覆盖不了。
    /// 单卡加载期校验（数组级校验如 id 唯一性归加载入口 CardCatalog）：id≤0 / name 空 / maxHp≤0 / effects 空。
    /// </summary>
    public sealed class CardJsonConverter : JsonConverter
    {
        public const string KindMonster = "monster";
        public const string KindEffect = "effect";

        public override bool CanConvert(Type objectType) => objectType == typeof(CardEntry);

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            JObject obj = JObject.Load(reader);
            string kindName = ReadKind(obj);
            int id = ReadId(obj);

            BattleCard card;
            if (kindName == KindEffect)
            {
                EffectCardDef def = new EffectCardDef();
                PopulateDef(obj, def, serializer);
                if (def.Name.Length == 0)
                    throw new JsonSerializationException($"卡 #{id} name 为空（配置错误，加载期暴露）");
                if (def.Effects == null || def.Effects.Length == 0)
                    throw new JsonSerializationException($"卡 #{id}（{def.Name}）效果卡无效果载荷 effects（配置错误，加载期暴露）");
                card = BattleCard.Of(def);
            }
            else
            {
                MonsterCardDef def = new MonsterCardDef();
                PopulateDef(obj, def, serializer);
                if (def.Name.Length == 0)
                    throw new JsonSerializationException($"卡 #{id} name 为空（配置错误，加载期暴露）");
                if (def.MaxHp <= 0)
                    throw new JsonSerializationException($"卡 #{id}（{def.Name}）maxHp 必须为正（配置错误，加载期暴露）");
                card = BattleCard.Of(def);
            }
            return new CardEntry { Id = id, Card = card };
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            CardEntry entry = (CardEntry)value;
            object def = entry.Card != null ? (object)entry.Card.Effect ?? entry.Card.Monster : null;
            if (def == null)
                throw new InvalidOperationException("CardEntry 缺少卡体（Effect/Monster 均空）——不写出无卡体信封");
            JObject obj = new JObject();
            obj.Add(new JProperty("kind", entry.Card.IsMonster ? KindMonster : KindEffect));
            obj.Add(new JProperty("id", entry.Id));
            BattleJsonFields.WriteFields(obj, def, serializer, "kind"); // 卡体字段内联（同 T1 约定：声明序/null 省略/默认照写）
            obj.WriteTo(writer);
        }

        static string ReadKind(JObject obj)
        {
            JToken kind = obj["kind"];
            if (kind == null || kind.Type != JTokenType.String)
                throw new JsonSerializationException(
                    $"缺少判别字段 \"kind\"（卡牌信封）——已登记：{KindMonster}, {KindEffect}（配置错误，加载期暴露）");
            string kindName = (string)kind;
            if (kindName != KindMonster && kindName != KindEffect)
                throw new JsonSerializationException(
                    $"未知卡牌判别值 \"{kindName}\"——已登记：{KindMonster}, {KindEffect}（配置错误，加载期暴露）");
            return kindName;
        }

        static int ReadId(JObject obj)
        {
            JToken id = obj["id"];
            if (id == null || id.Type != JTokenType.Integer)
                throw new JsonSerializationException("卡牌信封缺少整数字段 \"id\"（配置错误，加载期暴露）");
            int value = (int)id;
            if (value <= 0)
                throw new JsonSerializationException($"id 必须为正整数：{value}（配置错误，加载期暴露）");
            return value;
        }

        static void PopulateDef(JObject obj, object def, JsonSerializer serializer)
        {
            using (JsonReader populateReader = obj.CreateReader())
                serializer.Populate(populateReader, def); // kind/id 无对应卡体成员，自然跳过
        }
    }
}
