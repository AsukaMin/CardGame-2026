using System.Collections.Generic;

/// <summary>
/// 开窗参数包：OpenType 标"这扇窗以什么身份开"，其余键值对按需塞。
/// 用法：
///   var p = new UIParam { OpenType = "Edit" };
///   p.Set("DeckId", 1001);
///   p.Set("OnPicked", (Action&lt;int&gt;)(cardId =&gt; AddToDeck(cardId)));
///   UIManager.I.Show("ShopWindow", p);
/// </summary>
public class UIParam
{
    public string OpenType; // "Edit" / "View" / "Shop"……

    readonly Dictionary<string, object> map = new Dictionary<string, object>();

    public void Set(string key, object value) { map[key] = value; }

    public T Get<T>(string key)
    {
        map.TryGetValue(key, out object v);
        return (T)v;
    }

    public bool TryGet<T>(string key, out T value)
    {
        if (map.TryGetValue(key, out object v) && v is T t)
        {
            value = t;
            return true;
        }
        value = default;
        return false;
    }
}
