using System.Collections.Generic;
using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 整备窗（M5·T6 最小版，控件与交互契约见 CardGame-Doc《整备窗控件策划案》）：
/// 选卡套 → 点击装槽/卸槽 → 召唤费/攻血/校验预览 → 确认出阵（或跳过）→ 战斗。
/// 空壳预制体+代码自建控件（TipWindow 先例，不进扫描表无 key，M6 打磨再转预制体）；
/// 不订阅任何事实事件——纯局外装配，数据来自 BattleController.Fusion 只读引用。
/// 装配合法性全部走 MonsterSleeve.Install/Validate（融合文档§三：整备期保证合法性），失败弹 TipWindow。
/// </summary>
public sealed class PrepWindow : UIWindow
{
    public override UIWindowLayer Layer => UIWindowLayer.Fullscreen;

    static readonly string[] SlotNames = { "身材", "入场", "常驻", "离场" };

    FusionCatalog catalog;
    int sleeveIndex;
    MonsterSleeve sleeve;

    TMP_Text sleeveNameText;
    TMP_Text previewText;
    Transform slotListRoot;
    Transform partListRoot;
    Button confirmButton;

    public override void OnCreate(UIParam param)
    {
        catalog = BattleController.Instance.Fusion;
        if (catalog == null || catalog.Sleeves.Count == 0)
        {
            // 读表失败/无数据（Controller 已 LogError）——不拦流程，自动跳过
            Skip();
            return;
        }
        BuildUi();
        SelectSleeve(0);
    }

    public override void OnShow(UIParam param) => Rebuild();

    // ==================== 结构（OnCreate 一次） ====================

    void BuildUi()
    {
        // 全屏底色（顶掉入口窗后不至于透出战斗层）
        MakeImage(transform, new Vector2(0, 0), new Vector2(1920, 1080), new Color(0.08f, 0.08f, 0.11f, 0.97f), true);

        MakeText(transform, new Vector2(0, 480), new Vector2(800, 70), 52).text = "整 备";
        MakeText(transform, new Vector2(-430, 380), new Vector2(500, 50), 30).text = "选择卡套：";
        MakeButton(transform, new Vector2(560, 380), new Vector2(90, 60), "◀", () => SelectSleeve(sleeveIndex - 1));
        MakeButton(transform, new Vector2(820, 380), new Vector2(90, 60), "▶", () => SelectSleeve(sleeveIndex + 1));
        sleeveNameText = MakeText(transform, new Vector2(690, 380), new Vector2(380, 60), 36);
        previewText = MakeText(transform, new Vector2(430, 300), new Vector2(1200, 50), 30);

        MakeText(transform, new Vector2(-600, 230), new Vector2(500, 44), 32).text = "槽位（点击卸下）：";
        slotListRoot = MakeContainer(transform, new Vector2(-330, 195));

        MakeText(transform, new Vector2(-600, -40), new Vector2(500, 44), 32).text = "部位池（点击装入）：";
        partListRoot = MakeContainer(transform, new Vector2(-330, -75));

        confirmButton = MakeButton(transform, new Vector2(530, -420), new Vector2(340, 80), "确认出阵", Confirm);
        MakeButton(transform, new Vector2(900, -420), new Vector2(280, 80), "跳过整备", Skip);
    }

    // ==================== 行为 ====================

    void SelectSleeve(int index)
    {
        sleeveIndex = ((index % catalog.Sleeves.Count) + catalog.Sleeves.Count) % catalog.Sleeves.Count;
        sleeve = new MonsterSleeve(catalog.Sleeves[sleeveIndex]);
        Rebuild();
    }

    void OnPartClicked(PartCardConfig part)
    {
        // 装入其槽型的第一个空槽（全校验走 Install——槽型不匹配/占用/矩阵违例抛中文异常）
        for (int i = 0; i < sleeve.Config.Slots.Length; i++)
        {
            if (sleeve.Config.Slots[i] != part.SlotType || sleeve.Installed[i] != null) continue;
            try { sleeve.Install(i, part); }
            catch (System.InvalidOperationException ex) { ShowTip(ex.Message); }
            Rebuild();
            return;
        }
        ShowTip($"无处可装「{part.Name}」：{SlotName(part.SlotType)}槽已满或不存在");
    }

    void OnUninstallClicked(int slotIndex)
    {
        sleeve.Installed[slotIndex] = null;
        Rebuild();
    }

    void Confirm()
    {
        BattleController.Instance.CurrentSleeve = sleeve;
        CloseAndStart();
    }

    void Skip()
    {
        BattleController.Instance.CurrentSleeve = null;
        CloseAndStart();
    }

    void CloseAndStart()
    {
        UIManager.Instance.Hide("PrepWindow");
        BattleController.Instance.BeginBattle();
    }

    // ==================== 刷新（全量重建动态行） ====================

    void Rebuild()
    {
        if (catalog == null || sleeve == null) return;

        sleeveNameText.text = sleeve.Config.Name;
        (int attack, int maxHp) = sleeve.BaseStats;
        bool valid = IsValid(out string invalidReason);
        previewText.text = valid
            ? $"召唤费 {sleeve.SummonCost} · 攻 {attack} / 血 {maxHp} · 可出阵"
            : $"召唤费 {sleeve.SummonCost} · 攻 {attack} / 血 {maxHp} · {invalidReason}";
        previewText.color = valid ? Color.white : new Color(1f, 0.45f, 0.45f);
        if (confirmButton != null) confirmButton.interactable = valid;

        ClearChildren(slotListRoot);
        float slotY = 0;
        for (int i = 0; i < sleeve.Config.Slots.Length; i++)
        {
            PartCardConfig installed = sleeve.Installed[i];
            int index = i;
            TMP_Text line = MakeText(slotListRoot, new Vector2(0, slotY), new Vector2(760, 44), 28);
            line.alignment = TextAlignmentOptions.Left;
            line.text = installed != null
                ? $"{i + 1}. [{SlotName(sleeve.Config.Slots[i])}] {installed.Name}（{installed.Cost} 费）"
                : $"{i + 1}. [{SlotName(sleeve.Config.Slots[i])}] —— 空";
            if (installed != null)
                MakeButton(slotListRoot, new Vector2(450, slotY), new Vector2(120, 40), "卸下",
                    () => OnUninstallClicked(index));
            slotY -= 56;
        }

        ClearChildren(partListRoot);
        float partY = 0;
        foreach (PartCardConfig part in catalog.Parts)
        {
            PartCardConfig current = part;
            TMP_Text line = MakeText(partListRoot, new Vector2(0, partY), new Vector2(760, 44), 28);
            line.alignment = TextAlignmentOptions.Left;
            line.text = $"{part.Name} · {SlotName(part.SlotType)} · {part.Cost} 费";
            MakeButton(partListRoot, new Vector2(450, partY), new Vector2(120, 40), "装入",
                () => OnPartClicked(current));
            partY -= 56;
        }
    }

    bool IsValid(out string reason)
    {
        reason = "";
        try { sleeve.Validate(); }
        catch (System.InvalidOperationException ex) { reason = ex.Message.Split('—')[0].Trim(); return false; }
        return true;
    }

    static string SlotName(Fusion.SlotType slot) => SlotNames[(int)slot];

    void ShowTip(string message)
    {
        var param = new UIParam();
        param.Set("Message", message);
        param.Set("Color", new Color(1f, 0.5f, 0.4f));
        UIManager.Instance.Show("TipWindow", param);
    }

    // ==================== 代码自建控件（不进扫描表；M6 打磨转预制体） ====================

    static TMP_Text MakeText(Transform parent, Vector2 anchoredPos, Vector2 size, float fontSize)
    {
        var go = new GameObject("text", typeof(RectTransform), typeof(TextMeshProUGUI));
        RectTransform rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = anchoredPos;
        rect.sizeDelta = size;
        TextMeshProUGUI label = go.GetComponent<TextMeshProUGUI>();
        label.font = TMP_Settings.defaultFontAsset;
        label.fontSize = fontSize;
        label.alignment = TextAlignmentOptions.Center;
        label.raycastTarget = false;
        return label;
    }

    static Button MakeButton(Transform parent, Vector2 anchoredPos, Vector2 size, string label, System.Action onClick)
    {
        var go = new GameObject("btn_" + label, typeof(RectTransform), typeof(Image), typeof(Button));
        RectTransform rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = anchoredPos;
        rect.sizeDelta = size;
        Image bg = go.GetComponent<Image>();
        bg.color = new Color(0.30f, 0.34f, 0.42f);
        Button button = go.GetComponent<Button>();
        button.onClick.AddListener(() => onClick());
        MakeText(go.transform, Vector2.zero, size, Mathf.Min(30f, size.y * 0.45f)).text = label;
        return button;
    }

    static Image MakeImage(Transform parent, Vector2 anchoredPos, Vector2 size, Color color, bool fullStretch)
    {
        var go = new GameObject("img", typeof(RectTransform), typeof(Image));
        RectTransform rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        if (fullStretch)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
        else
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = anchoredPos;
            rect.sizeDelta = size;
        }
        Image image = go.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = fullStretch; // 底色挡点击；装饰图不挡
        return image;
    }

    static Transform MakeContainer(Transform parent, Vector2 anchoredPos)
    {
        var go = new GameObject("list", typeof(RectTransform));
        RectTransform rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = anchoredPos;
        rect.sizeDelta = new Vector2(800, 10);
        return go.transform;
    }

    static void ClearChildren(Transform root)
    {
        for (int i = root.childCount - 1; i >= 0; i--) Destroy(root.GetChild(i).gameObject);
    }
}
