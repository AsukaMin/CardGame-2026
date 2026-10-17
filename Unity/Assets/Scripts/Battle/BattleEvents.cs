using System;
using System.Collections.Generic;

namespace Battle
{
    /// <summary>
    /// 战斗核心唯一的"嘴"：只广播既成事实（过去时态），不关心谁在听（UI 框架文档契约）。
    /// UI 侧约定 OnShow 订阅、OnHide 退订、订阅后立即用 GetSnapshot() 全量刷。
    /// 静态事件跨战斗保留——重开局时窗口应自行重刷（UI 框架文档的告诫）。
    /// </summary>
    public static class BattleEvents
    {
        /// <summary>(单位或主战者Id, 新血量)。主战者 Id 约定：玩家=1，敌方=2。</summary>
        public static event Action<int, int> OnHpChanged;
        /// <summary>(卡牌实例Id)。</summary>
        public static event Action<int> OnCardDrawn;
        public static event Action<int> OnCardPlayed;
        public static event Action<int> OnCardDiscarded;
        /// <summary>(单位Id)。</summary>
        public static event Action<int> OnUnitSummoned;
        public static event Action<int> OnUnitDied;
        /// <summary>(回合数)。</summary>
        public static event Action<int> OnTurnStart;
        /// <summary>(新点数)。</summary>
        public static event Action<int> OnActionPointsChanged;
        /// <summary>(本回合敌方意图列表)。</summary>
        public static event Action<IReadOnlyList<Intent>> OnIntentRevealed;
        public static event Action<BattleResult> OnBattleEnd;

        internal static void FireHpChanged(int id, int hp) => OnHpChanged?.Invoke(id, hp);
        internal static void FireCardDrawn(int cardId) => OnCardDrawn?.Invoke(cardId);
        internal static void FireCardPlayed(int cardId) => OnCardPlayed?.Invoke(cardId);
        internal static void FireCardDiscarded(int cardId) => OnCardDiscarded?.Invoke(cardId);
        internal static void FireUnitSummoned(int unitId) => OnUnitSummoned?.Invoke(unitId);
        internal static void FireUnitDied(int unitId) => OnUnitDied?.Invoke(unitId);
        internal static void FireTurnStart(int turn) => OnTurnStart?.Invoke(turn);
        internal static void FireActionPointsChanged(int points) => OnActionPointsChanged?.Invoke(points);
        internal static void FireIntentRevealed(IReadOnlyList<Intent> intents) => OnIntentRevealed?.Invoke(intents);
        internal static void FireBattleEnd(BattleResult result) => OnBattleEnd?.Invoke(result);
    }
}
