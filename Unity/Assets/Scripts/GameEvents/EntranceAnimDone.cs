namespace GameEvents
{
    /// <summary>
    /// 表现层 → 战斗核心：某单位的入场动画演出完成，放行其挂起的效果组
    /// （DeferPresentationEvents=true 时 BattleSession 订阅本事件；见《战斗核心框架设计》表现挂起节）。
    /// </summary>
    public readonly struct EntranceAnimDone
    {
        public readonly int UnitId;

        public EntranceAnimDone(int unitId) => UnitId = unitId;
    }
}
