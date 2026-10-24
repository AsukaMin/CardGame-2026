using System;
using System.Collections.Generic;
using Battle;
using GameEvents;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 战斗主窗口（M3 最小接线，全场唯一）。三通道铁律的 UI 侧落地：
/// 逻辑→UI 只有事件——OnShow 订阅 10 个事实事件，回调统一走 RefreshAll 全量刷（M3 规模小，不做增量）；
/// UI→逻辑 只有方法——手势/按钮直调 BattleSession 命令，异常消息即反馈（飘字 + 日志，策划案§六）。
/// 控件 key 与 Resources/UI/BattleWindow.prefab 根节点 UITable 的扫描结果一一对应；
/// 手牌/场上条目为全量重建（清容器 → 实例化模板 → 运行时 AddComponent 视图脚本，见 UI/Items）。
/// </summary>
public sealed class BattleWindow : UIWindow
{
    public override UIWindowLayer Layer => UIWindowLayer.Fullscreen;

    // M3 不做布局系统：条目按索引等距横排的间距（条目宽约 472）
    const float HandSpacing = 500f;
    const float BoardSpacing = 520f;
    const int LogMaxLines = 12;

    GameObject cardItemPrefab; // Resources/UI/CardItem（条目模板：只挂 UITable，脚本运行时挂）
    GameObject unitItemPrefab; // Resources/UI/UnitItem

    readonly List<string> logLines = new List<string>();
    readonly List<RaycastResult> raycastBuffer = new List<RaycastResult>();

    BattleSession Session => BattleController.I != null ? BattleController.I.Session : null;

    // ==================== 生命周期 ====================

    public override void OnCreate(UIParam param)
    {
        cardItemPrefab = Resources.Load<GameObject>("UI/CardItem");
        unitItemPrefab = Resources.Load<GameObject>("UI/UnitItem");
        if (cardItemPrefab == null) Debug.LogError("[BattleWindow] 缺手牌模板 Resources/UI/CardItem（约定见 Resources/UI/README.md）");
        if (unitItemPrefab == null) Debug.LogError("[BattleWindow] 缺单位模板 Resources/UI/UnitItem（约定见 Resources/UI/README.md）");
        GetButton("endTurnButton")?.onClick.AddListener(OnEndTurnClicked);
    }

    public override void OnShow(UIParam param)
    {
        logLines.Clear();
        Subscribe();   // 订阅/退订成对（OnShow/OnHide），重开一局不串台
        RefreshAll();  // 晚开窗错过历史事件 → 订阅完立即全量刷一次
    }

    public override void OnHide() => Unsubscribe();

    // 工程禁用了域重载（Enter Play Mode Options），静态 GameEventBus 跨 Play 会话存活：
    // 退出 Play 时窗口被销毁但订阅不会自动消失，不在销毁时退订，下一局首个事件就会打到已销毁的窗口上
    void OnDestroy() => Unsubscribe();

    // ==================== 事实事件 → 一行日志 + 全量刷 ====================

    void Subscribe()
    {
        GameEventBus.Subscribe<BattleStarted>(On);
        GameEventBus.Subscribe<TurnStarted>(On);
        GameEventBus.Subscribe<EnergyChanged>(On);
        GameEventBus.Subscribe<CardDrawn>(On);
        GameEventBus.Subscribe<CardPlayed>(On);
        GameEventBus.Subscribe<MonsterSummoned>(On);
        GameEventBus.Subscribe<AttackResolved>(On);
        GameEventBus.Subscribe<DamageResolved>(On);
        GameEventBus.Subscribe<UnitDied>(On);
        GameEventBus.Subscribe<BattleEnded>(On);
    }

    void Unsubscribe()
    {
        GameEventBus.Unsubscribe<BattleStarted>(On);
        GameEventBus.Unsubscribe<TurnStarted>(On);
        GameEventBus.Unsubscribe<EnergyChanged>(On);
        GameEventBus.Unsubscribe<CardDrawn>(On);
        GameEventBus.Unsubscribe<CardPlayed>(On);
        GameEventBus.Unsubscribe<MonsterSummoned>(On);
        GameEventBus.Unsubscribe<AttackResolved>(On);
        GameEventBus.Unsubscribe<DamageResolved>(On);
        GameEventBus.Unsubscribe<UnitDied>(On);
        GameEventBus.Unsubscribe<BattleEnded>(On);
    }

    void On(BattleStarted e) { AppendLog($"开战 —— 我方 {e.PlayerMaxHp} HP vs 敌方 {e.EnemyMaxHp} HP"); RefreshAll(); }
    void On(TurnStarted e) { AppendLog($"── 第 {e.Turn} 回合 · {(e.IsPlayerSide ? "我方" : "敌方")} ──"); RefreshAll(); }
    void On(EnergyChanged e) { AppendLog($"能量 {e.Before} → {e.After}"); RefreshAll(); }
    void On(CardDrawn e) { AppendLog($"抽牌：{e.CardName}"); RefreshAll(); }
    void On(CardPlayed e) { AppendLog($"打出：{e.CardName}"); RefreshAll(); }
    void On(MonsterSummoned e) { AppendLog($"{(e.IsPlayerSide ? "我方" : "敌方")}召唤 {e.Name} {e.Attack}/{e.MaxHp}"); RefreshAll(); }
    void On(AttackResolved e) { AppendLog($"{e.AttackerName} 攻击 {e.TargetName}，伤害 {e.Amount}"); RefreshAll(); }
    void On(DamageResolved e) { AppendLog($"{e.SourceName} 对 {e.TargetName} 效果伤害 {e.Amount}（剩 {e.TargetHpAfter} HP）"); RefreshAll(); }
    void On(UnitDied e) { AppendLog($"{e.Name} 死亡离场"); RefreshAll(); }

    void On(BattleEnded e)
    {
        // M3 临时：ResultWindow 未做——胜负走 Console + logText；重开按 R（BattleController.Update）
        string msg = e.PlayerWon ? "胜利！" : "失败…";
        AppendLog($"终局 —— {msg}");
        Debug.Log($"[BattleWindow] 终局 —— {msg}（M3 临时无 ResultWindow：按 R 重开一局）");
        RefreshAll();
    }

    // ==================== 全量刷新（只读轮询 session；盾不在事件载荷里，只能轮询——M2 定案） ====================

    void RefreshAll()
    {
        if (this == null) return; // 僵尸订阅防御：销毁后漏网的调用直接吞掉（GetComponent 会抛 MissingReference）
        BattleSession s = Session;
        if (s == null) return;

        TMP_Text turnText = GetText("turnText");
        if (turnText != null)
            turnText.text = !s.IsStarted ? "准备中…"
                : s.IsBattleOver ? $"战斗结束 —— {(s.Result == BattleResult.Victory ? "胜利！" : "失败…")}"
                : $"第 {s.Turn} 回合 · {(s.Phase == BattlePhase.PlayerPlay ? "我方行动" : "敌方回合")}";

        SetActive("enemyTurnBanner", s.IsStarted && s.Phase == BattlePhase.EnemyTurn); // 敌方回合标记=全局交互锁的可视化

        TMP_Text playerHp = GetText("playerHpText");
        if (playerHp != null) playerHp.text = s.IsStarted ? $"我方 HP {s.PlayerHp}" : "我方 HP —";

        TMP_Text enemyHp = GetText("enemyHpText");
        if (enemyHp != null) enemyHp.text = s.IsStarted ? $"敌方 HP {s.EnemyHp}" : "敌方 HP —";

        // 盾位：只属于我方领袖（怪兽无盾，策划案§四定案）；有盾才显示，无盾整个节点隐藏
        TMP_Text shield = GetText("playerLeaderShieldText");
        if (shield != null)
        {
            int v = s.PlayerLeader != null ? s.PlayerLeader.Attributes.CurrentInt(AttributeId.Shield) : 0;
            shield.text = $"盾 {v}";
            shield.gameObject.SetActive(v > 0);
        }

        TMP_Text deck = GetText("deckCountText");
        if (deck != null) deck.text = $"牌库 {s.DeckCount}";

        TMP_Text energy = GetText("energyText");
        if (energy != null) energy.text = $"能量 {s.Energy}";

        TMP_Text logText = GetText("logText");
        if (logText != null) logText.text = string.Join("\n", logLines);

        // 全局交互锁：非我方出牌阶段/已终局 → 手势一律忽略（见各手势入口）、结束回合置灰
        Button endTurn = GetButton("endTurnButton");
        if (endTurn != null) endTurn.interactable = s.Phase == BattlePhase.PlayerPlay && !s.IsBattleOver;

        RebuildHand(s);
        RebuildBoard(s.PlayerBoard, "playerBoardRoot", isPlayerSide: true);
        RebuildBoard(s.EnemyBoard, "enemyBoardRoot", isPlayerSide: false);
    }

    // ==================== 手势/按钮 → 命令（UI→逻辑只有方法；失败=InvalidOperationException 中文消息） ====================

    void OnEndTurnClicked()
    {
        BattleSession s = Session;
        if (s == null || s.Phase != BattlePhase.PlayerPlay || s.IsBattleOver) return; // 全局锁
        RunCommand(s.EndTurn);
    }

    void OnCardDragEnded(CardItemView card, PointerEventData eventData)
    {
        BattleSession s = Session;
        if (s == null || s.Phase != BattlePhase.PlayerPlay || s.IsBattleOver) return; // 全局锁：静默忽略
        if (card.Card == null) return;

        if (card.Card.IsMonster)
        {
            // 怪兽卡 → 我方场上区。容器无 Graphic 不参与射线，用矩形判定（M3 无拖动跟随，只看释放点）
            RectTransform board = GetGameObject("playerBoardRoot")?.transform as RectTransform;
            if (board != null &&
                RectTransformUtility.RectangleContainsScreenPoint(board, eventData.position, eventData.pressEventCamera))
                RunCommand(() => s.SummonMonster(card.HandIndex));
            // 释放区外 = 取消（静默，策划案§六）
        }
        else if (TryResolveEnemyTarget(eventData, card.transform, out SkillEntity target))
        {
            // 效果卡 → 敌方单位 / 敌方领袖
            RunCommand(() => s.PlayEffectCard(card.HandIndex, target.Id));
        }
    }

    void OnUnitDragEnded(UnitItemView unit, PointerEventData eventData)
    {
        BattleSession s = Session;
        if (s == null || s.Phase != BattlePhase.PlayerPlay || s.IsBattleOver) return; // 全局锁
        if (unit == null || !unit.IsPlayerSide || unit.Entity == null) return;        // 只有我方单位是攻击拖源
        if (TryResolveEnemyTarget(eventData, unit.transform, out SkillEntity target))
            RunCommand(() => s.Attack(unit.Entity.Id, target.Id));
    }

    /// <summary>释放点判定：从最上层射线结果往后找第一个合法敌方目标（跳过拖源自身；策划案§六"只看释放点命中"）。</summary>
    bool TryResolveEnemyTarget(PointerEventData eventData, Transform dragSource, out SkillEntity target)
    {
        target = null;
        if (EventSystem.current == null) return false;
        BattleSession s = Session;
        if (s == null || s.EnemyLeader == null) return false;

        Transform enemyLeaderNode = GetButton("enemyLeaderNode")?.transform;
        raycastBuffer.Clear();
        EventSystem.current.RaycastAll(eventData, raycastBuffer);
        foreach (RaycastResult hit in raycastBuffer)
        {
            if (hit.gameObject == null) continue;
            Transform t = hit.gameObject.transform;
            if (dragSource != null && t.IsChildOf(dragSource)) continue; // 拖源自身挡在指针下，跳过

            UnitItemView unitView = t.GetComponentInParent<UnitItemView>();
            if (unitView != null && !unitView.IsPlayerSide && unitView.Entity != null)
            {
                target = unitView.Entity; // 敌方单位
                return true;
            }
            if (enemyLeaderNode != null && t.IsChildOf(enemyLeaderNode))
            {
                target = s.EnemyLeader; // 敌方领袖
                return true;
            }
            // 其余命中（手牌/我方单位/HUD 杂件）不合法，继续往后找
        }
        return false; // 不在任何合法目标上 = 取消（静默）
    }

    void RunCommand(Action command)
    {
        try
        {
            command();
        }
        catch (InvalidOperationException ex)
        {
            ShowTip(ex.Message); // 命令失败统一反馈：异常消息已是可读中文
        }
    }

    // ==================== 条目全量重建 ====================

    void RebuildHand(BattleSession s)
    {
        Transform root = GetGameObject("handRoot")?.transform;
        if (root == null) return;
        ClearChildren(root);
        if (cardItemPrefab == null) return;

        IReadOnlyList<BattleCard> hand = s.Hand;
        for (int i = 0; i < hand.Count; i++)
        {
            GameObject go = Instantiate(cardItemPrefab, root);
            CardItemView item = go.GetComponent<CardItemView>() ?? go.AddComponent<CardItemView>(); // get-or-add：模板惯例不挂脚本，但挂了也兼容
            item.Setup(hand[i], i, OnCardDragEnded);
            PlaceItem(item.transform, i, hand.Count, HandSpacing);
        }
    }

    void RebuildBoard(IReadOnlyList<SkillEntity> board, string rootKey, bool isPlayerSide)
    {
        Transform root = GetGameObject(rootKey)?.transform;
        if (root == null) return;
        ClearChildren(root);
        if (unitItemPrefab == null) return;

        for (int i = 0; i < board.Count; i++)
        {
            GameObject go = Instantiate(unitItemPrefab, root);
            UnitItemView item = go.GetComponent<UnitItemView>() ?? go.AddComponent<UnitItemView>(); // get-or-add：模板惯例不挂脚本，但挂了也兼容
            // 我方单位挂攻击拖拽手势；敌方单位不挂（仅作释放判定热区）
            item.Setup(board[i], isPlayerSide, isPlayerSide ? OnUnitDragEnded : null);
            PlaceItem(item.transform, i, board.Count, BoardSpacing);
        }
    }

    // ==================== 支撑 ====================

    void AppendLog(string line)
    {
        Debug.Log($"[战斗] {line}"); // 同步 Console，方便调试（logText 屏上只留尾部）
        logLines.Add(line);
        if (logLines.Count > LogMaxLines) logLines.RemoveAt(0); // 只留尾部，防无限增长
    }

    /// <summary>命令失败提示：tipLayer API 未建（框架留白），用 DotweenHelper.FloatText 纯代码飘字顶替 + 日志留底。</summary>
    void ShowTip(string message)
    {
        AppendLog(message);
        TMP_Text logText = GetText("logText");
        if (logText != null) logText.text = string.Join("\n", logLines);
        DotweenHelper.FloatText(transform, new Vector2(0f, -150f), message, Color.red);
    }

    void SetActive(string key, bool active)
    {
        GameObject go = GetGameObject(key);
        if (go != null) go.SetActive(active);
    }

    void ClearChildren(Transform root)
    {
        for (int i = root.childCount - 1; i >= 0; i--) Destroy(root.GetChild(i).gameObject);
    }

    static void PlaceItem(Transform item, int index, int count, float spacing)
    {
        if (item is RectTransform rt)
            rt.anchoredPosition = new Vector2((index - (count - 1) * 0.5f) * spacing, 0f);
    }
}
