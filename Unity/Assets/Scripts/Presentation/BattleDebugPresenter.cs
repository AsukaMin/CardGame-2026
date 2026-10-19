using System;
using GameEvents;
using UnityEngine;

namespace Presentation
{
    /// <summary>
    /// 战斗事实 → Debug.Log 占位表现（M1：零 UI，表现=控制台日志）。
    /// 只监听、不参与逻辑（逻辑/表现分离铁律）；M3 战斗 UI 接线时替换本类。
    /// 订阅/退订成对（事件系统设计§4）。
    /// </summary>
    public sealed class BattleDebugPresenter : IDisposable
    {
        public BattleDebugPresenter() => Subscribe();

        /// <summary>订阅并返回（配合 using 语句）。</summary>
        public static BattleDebugPresenter Attach() => new BattleDebugPresenter();

        public void Dispose() => Unsubscribe();

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

        void On(BattleStarted e) =>
            Debug.Log($"[战斗] 开始 —— 玩家 {e.PlayerMaxHp} HP vs 敌方 {e.EnemyMaxHp} HP");

        void On(TurnStarted e) =>
            Debug.Log($"[战斗] ── 第 {e.Turn} 回合 · {(e.IsPlayerSide ? "我方回合（出牌阶段）" : "敌方回合")} ──");

        void On(EnergyChanged e) =>
            Debug.Log($"[战斗] 能量 {e.Before} → {e.After}");

        void On(CardDrawn e) =>
            Debug.Log($"[战斗] 抽牌：{e.CardName}");

        void On(CardPlayed e) =>
            Debug.Log($"[战斗] 打出：{e.CardName}（目标 #{e.TargetId}）");

        void On(MonsterSummoned e) =>
            Debug.Log($"[战斗] {(e.IsPlayerSide ? "我方" : "敌方")}召唤：{e.Name} {e.Attack}/{e.MaxHp}（#{e.UnitId}）");

        void On(AttackResolved e) =>
            Debug.Log($"[战斗] {e.AttackerName} 攻击 {e.TargetName}，造成 {e.Amount} 伤害");

        void On(DamageResolved e) =>
            Debug.Log($"[战斗] {e.SourceName} 的效果对 {e.TargetName} 造成 {e.Amount} 伤害（剩余 {e.TargetHpAfter} HP）");

        void On(UnitDied e) =>
            Debug.Log($"[战斗] {e.Name}（#{e.UnitId}）死亡离场");

        void On(BattleEnded e) =>
            Debug.Log($"[战斗] 终局 —— {(e.PlayerWon ? "玩家胜利！" : "玩家失败…")}");
    }
}
