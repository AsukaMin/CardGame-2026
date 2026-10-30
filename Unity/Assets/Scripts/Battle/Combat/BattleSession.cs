using System;
using System.Collections.Generic;
using GameEvents;

namespace Battle
{
    /// <summary>战斗结果（同时归零=判负，保守）。</summary>
    public enum BattleResult { None, Victory, Defeat }

    // ==================== 效果伤害记录（宿主自定义 op：引擎事实 → 会话广播） ====================

    /// <summary>记录一次效果伤害（经 Event.Damage.Applied 触发器写入世界日志；战斗伤害不走此路）。</summary>
    public sealed class RecordEffectDamageOp : EffectOp { }

    sealed class RecordEffectDamageSpec : OpSpec
    {
        public override void Execute(SkillEngine engine, SkillContext ctx)
        {
            if (!ctx.Event.IsValid || ctx.Event.Kind != DamageSourceKind.Effect) return;
            BattleWorld world = (BattleWorld)engine.World;
            world.EffectDamageLog.Add((ctx.Event.Instigator, ctx.Event.Target, ctx.Event.Magnitude, ctx.Event.ShieldAbsorbed));
        }
    }

    // ==================== 抽牌 op（宿主自定义 op：效果卡经 JSON 驱动抽牌） ====================

    /// <summary>抽 N 张牌（效果卡载荷）。判别名 "DrawCards" 经 SkillJson 注册表进 JSON（幂等登记）。</summary>
    public sealed class DrawCardsOp : EffectOp
    {
        public int Count = 1;
    }

    sealed class DrawCardsSpec : OpSpec
    {
        public override void Execute(SkillEngine engine, SkillContext ctx)
        {
            DrawCardsOp op = (DrawCardsOp)Data;
            if (op.Count < 1)
                throw new InvalidOperationException($"DrawCards 数量非法：{op.Count}（配置错误）");
            BattleWorld world = (BattleWorld)engine.World;
            if (world.DrawCards == null)
                throw new InvalidOperationException("宿主未注入抽牌动作 BattleWorld.DrawCards（配置错误）");
            world.DrawCards(op.Count); // 在引擎 Execute 内回调宿主——嵌套 Raise 走 FIFO 队列现有语义
        }
    }

    // ==================== 加卡 op（M5·缺口§3：复制卡宿主 op） ====================

    /// <summary>
    /// 按卡表 id 获得 N 张卡入手（"获得这张卡的复制"类）。判别名 "AddCardToHand"（幂等登记）。
    /// 复制语义数据侧用"卡 JSON 自引 id"约定（op 里填自己的卡号）；CardCatalog 加载期校验 id 存在。
    /// </summary>
    public sealed class AddCardToHandOp : EffectOp
    {
        public int CardId;
        public int Count = 1;
    }

    sealed class AddCardToHandSpec : OpSpec
    {
        public override void Execute(SkillEngine engine, SkillContext ctx)
        {
            AddCardToHandOp op = (AddCardToHandOp)Data;
            if (op.CardId <= 0)
                throw new InvalidOperationException($"AddCardToHand 卡 id 非法：{op.CardId}（配置错误）");
            if (op.Count < 1)
                throw new InvalidOperationException($"AddCardToHand 数量非法：{op.Count}（配置错误）");
            BattleWorld world = (BattleWorld)engine.World;
            if (world.AddCardToHand == null)
                throw new InvalidOperationException("宿主未注入加卡动作 BattleWorld.AddCardToHand（配置错误）");
            world.AddCardToHand(op.CardId, op.Count);
        }
    }

    /// <summary>
    /// 战斗会话（M1 最小版）：命令 API 门面 + 结算编排 + 事实广播。
    /// 逻辑核心——零表现依赖；"发生了什么"经 GameEventBus 广播，表现层只监听（设计铁律）。
    /// 结算全部走技能系统：出牌=engine.Execute(EffectGroupDef[])，攻击=ResolveDamage(Combat)；
    /// 死亡观察/胜负判定/回合时点=宿主职责（技能系统设计§9 宿主接入指南）。
    /// </summary>
    public sealed class BattleSession
    {
        readonly BattleSetup setup;
        readonly BattleWorld world;
        readonly SkillEngine engine;
        readonly TurnFlow flow;
        readonly EnemyBrain brain = new EnemyBrain();
        readonly IEnergyRule energyRule;
        readonly SeededRandomSource random;

        readonly List<BattleCard> deck = new List<BattleCard>();
        readonly List<BattleCard> hand = new List<BattleCard>();
        readonly List<SkillEntity> playerBoard = new List<SkillEntity>();
        readonly List<SkillEntity> enemyBoard = new List<SkillEntity>();
        readonly HashSet<int> attackedThisTurn = new HashSet<int>();
        readonly Dictionary<int, SummonPayload> summonPayloads = new Dictionary<int, SummonPayload>(); // 组装怪兽：单位 id → 投影（离场效果用）
        readonly EnergyContext energyContext; // 复用载体（避免每次钩子分配）

        SkillEntity playerLeader;
        SkillEntity enemyLeader;
        int energy;
        int nextEnemySummonIndex;
        BattleResult result = BattleResult.None;
        bool started;

        public BattleSession(BattleSetup setup)
        {
            this.setup = setup ?? throw new ArgumentNullException(nameof(setup));
            random = new SeededRandomSource(setup.RandomSeed);
            world = new BattleWorld(random);
            world.DrawCards = count => { for (int i = 0; i < count; i++) DrawOne(); }; // 抽牌 op 的执行通路
            world.AddCardToHand = (cardId, count) => AddCardById(cardId, count);       // 加卡 op（复制卡）
            engine = new SkillEngine(world);
            flow = new TurnFlow(this);
            energyRule = setup.EnergyRule ?? new FlatPerTurnRule(2);
            energyContext = new EnergyContext { Session = this };

            // 宿主自定义 op：效果伤害记录 + 抽牌 + 加卡（技能系统设计§7.6 扩展点）
            engine.RegisterOp<RecordEffectDamageOp>(op => new RecordEffectDamageSpec { Data = op });
            engine.RegisterOp<DrawCardsOp>(op => new DrawCardsSpec { Data = op });
            engine.RegisterOp<AddCardToHandOp>(op => new AddCardToHandSpec { Data = op });
            SkillJson.TryRegisterOp<DrawCardsOp>("DrawCards");      // JSON 判别登记——幂等（每场对局都构造）
            SkillJson.TryRegisterOp<AddCardToHandOp>("AddCardToHand");
            engine.RegisterTrigger(null, new TriggerDef
            {
                EventTag = SkillTags.EventDamageApplied.Name,
                Effects = new EffectGroupDef
                {
                    Target = TargetMode.EventTarget,
                    Ops = new EffectOp[] { new RecordEffectDamageOp() },
                },
            });
        }

        // ==================== 只读视图（测试与未来 UI 消费；Start 后有效） ====================

        public BattlePhase Phase => flow.Phase;
        public int Turn => flow.Turn;
        public BattleResult Result => result;
        public bool IsBattleOver => result != BattleResult.None;
        public bool IsStarted => started;
        public int Energy => energy;
        public int DeckCount => deck.Count;
        public IReadOnlyList<BattleCard> Hand => hand;
        public IReadOnlyList<SkillEntity> PlayerBoard => playerBoard;
        public IReadOnlyList<SkillEntity> EnemyBoard => enemyBoard;
        public SkillEntity PlayerLeader => playerLeader;
        public SkillEntity EnemyLeader => enemyLeader;
        public int PlayerHp => playerLeader.Attributes.CurrentInt(AttributeId.CurrentHp);
        public int EnemyHp => enemyLeader.Attributes.CurrentInt(AttributeId.CurrentHp);
        public IRandomSource Random => random;

        /// <summary>该单位本回合是否已攻击（M3 留白①清偿：UI 灰化/预判用；我方回合开始重置）。</summary>
        public bool HasAttackedThisTurn(int unitId) => attackedThisTurn.Contains(unitId);

        // ==================== 生命周期 ====================

        /// <summary>战斗开始：造双方主战者、洗入牌库（按传入序）、抓起手、进入第 1 回合。</summary>
        public void Start()
        {
            if (started) throw new InvalidOperationException("战斗已开始，不可重复 Start");
            started = true;

            playerLeader = world.AddLeader(BattleWorld.PlayerTeam, "玩家", setup.PlayerHp);
            enemyLeader = world.AddLeader(BattleWorld.EnemyTeam, "敌方", setup.EnemyHp);
            deck.AddRange(setup.Deck);
            energy = setup.InitialEnergy;
            OnBattleStarted(); // "战斗开始"事实先于起手抽牌（叙事顺序）
            for (int i = 0; i < setup.StartingHandSize; i++) DrawOne();

            flow.Start();
        }

        /// <summary>结束我方回合（敌方回合在返回前同步执行完，回到我方回合或终局）。</summary>
        public void EndTurn() => flow.EndPlayerTurn();

        // ==================== 玩家命令（仅出牌阶段合法；违例 InvalidOperationException） ====================

        /// <summary>召唤怪兽（扣面值费用；召唤当回合可攻击）。组装怪兽（BattleCard.Summon 臂）走投影接线。</summary>
        public void SummonMonster(int handIndex)
        {
            flow.RequirePlayerPlay();
            BattleCard card = TakeHandCard(handIndex, requireMonster: true);
            PayEnergy(card.Cost);
            hand.RemoveAt(handIndex);

            if (card.Summon != null)
            {
                SummonPayloadCore(card.Summon, card.Id);
                return;
            }

            SkillEntity unit = world.AddUnit(BattleWorld.PlayerTeam,
                card.Monster.Name, card.Monster.Attack, card.Monster.MaxHp);
            playerBoard.Add(unit);
            engine.Raise(new GameplayEvent(BattleEventTags.UnitSummoned, playerLeader, unit, 0, DamageSourceKind.None));
            Broadcast(new MonsterSummoned(unit.Id, unit.Name, card.Monster.Attack, card.Monster.MaxHp, true));
        }

        /// <summary>
        /// 组装怪兽召唤（M5·T6）：接线形状 =《融合核心框架设计》§五——
        /// GrantTags→单位标签容器；OnSummon Effects→engine.Execute（Source=新单位）；
        /// Continuous Triggers→RegisterTrigger（随单位注销）；Auras→施加我方全体（Source=单位，死亡精准撤除）。
        /// </summary>
        void SummonPayloadCore(SummonPayload payload, int cardId)
        {
            SkillEntity unit = world.AddUnit(BattleWorld.PlayerTeam, payload.Name, payload.Attack, payload.MaxHp);
            playerBoard.Add(unit);
            summonPayloads[unit.Id] = payload;

            if (payload.GrantTags != null)
                foreach (string tagName in payload.GrantTags)
                    if (!string.IsNullOrEmpty(tagName)) unit.Tags.Add(TagRegistry.Resolve(tagName));

            engine.Raise(new GameplayEvent(BattleEventTags.UnitSummoned, playerLeader, unit, 0, DamageSourceKind.None));
            Broadcast(new MonsterSummoned(unit.Id, unit.Name, payload.Attack, payload.MaxHp, true));

            if (payload.OnSummonEffects != null && payload.OnSummonEffects.Length > 0)
                engine.Execute(payload.OnSummonEffects, new SkillContext { Source = unit, CardId = cardId });
            if (payload.ContinuousTriggers != null)
                foreach (TriggerDef trigger in payload.ContinuousTriggers)
                    engine.RegisterTrigger(unit, trigger);
            if (payload.Auras != null)
                foreach (GameplayEffectDef aura in payload.Auras)
                    engine.Execute(new[]
                    {
                        new EffectGroupDef
                        {
                            Target = TargetMode.AllAlliedUnits,
                            Ops = new EffectOp[] { new ApplyEffectOp { Effect = aura } },
                        },
                    }, new SkillContext { Source = unit, CardId = cardId });
            AfterCombat(); // 入场效果可能造成伤害/致死，统一收口
        }

        /// <summary>打出效果卡。目标规则（M5 放开己方增益）：依赖主目标的卡（RequiresEnemyTarget，直伤/单体指向）
        /// 目标必须是敌方主战者或敌方怪兽；其余卡（治疗/增益/AOE 类）目标可为任意存活实体——
        /// 拖到己方场上=给自己（领袖）或给该怪（MainTarget 对此类卡不参与结算，仅作释放锚）。</summary>
        public void PlayEffectCard(int handIndex, int targetEntityId)
        {
            flow.RequirePlayerPlay();
            BattleCard card = TakeHandCard(handIndex, requireMonster: false);
            SkillEntity target = card.RequiresEnemyTarget
                ? ResolveEnemyTarget(targetEntityId)
                : ResolveAnyTarget(targetEntityId);
            PayEnergy(card.Cost);

            hand.RemoveAt(handIndex);
            Broadcast(new CardPlayed(card.Name, target.Id));
            // 归因上下文：CardId 进事件载荷（缺口§2"当这张卡造成伤害时"可查）
            engine.Execute(card.Effect.Effects, new SkillContext
            {
                Source = playerLeader, MainTarget = target, CardId = card.Id,
            });
            // 触发器卡（"结界"形态）：打出即注册，挂玩家领袖，随本场生灭（注册序=打出序）
            if (card.Effect.Triggers != null)
                foreach (TriggerDef trigger in card.Effect.Triggers)
                    engine.RegisterTrigger(playerLeader, trigger);
            AfterCombat();
        }

        /// <summary>我方怪兽攻击（一回合一次；无反击；目标=敌方主战者或敌方怪兽）。</summary>
        public void Attack(int attackerUnitId, int targetEntityId)
        {
            flow.RequirePlayerPlay();
            SkillEntity attacker = world.Find(attackerUnitId);
            if (attacker == null || attacker.TeamId != BattleWorld.PlayerTeam || attacker.Kind != EntityKind.Unit)
                throw new InvalidOperationException("攻击者必须是己方在场怪兽");
            if (attackedThisTurn.Contains(attacker.Id))
                throw new InvalidOperationException("该怪兽本回合已攻击（一回合一次）");
            SkillEntity target = ResolveEnemyTarget(targetEntityId);

            attackedThisTurn.Add(attacker.Id);
            PerformAttackCore(attacker, target);
            AfterCombat();
            energyContext.Turn = Turn;
            energyContext.IsPlayerSide = true;
            energyRule.OnPlayerAttackDone(energyContext); // 我方攻击后钩子（框架留位）
        }

        // ==================== 结算编排 ====================

        /// <summary>一次攻击结算（伤害走技能系统三段管线，战斗伤害类别；事件带盾吸收明细——M5·T2.3）。</summary>
        void PerformAttackCore(SkillEntity attacker, SkillEntity target)
        {
            DamageResult r = engine.ResolveDamage(attacker, target,
                attacker.Attributes.Current(AttributeId.Attack), DamageSourceKind.Combat);
            Broadcast(new AttackResolved(attacker.Id, attacker.Name, target.Id, target.Name,
                (int)r.Amount, (int)r.ShieldAbsorbed, target.Attributes.CurrentInt(AttributeId.Shield)));
        }

        /// <summary>结算收口：效果伤害事实广播 → 死亡清扫 → 胜负判定（每次可能造成伤害的操作后调用）。</summary>
        void AfterCombat()
        {
            DrainEffectDamage();
            SweepDeaths();
            EvaluateResult();
        }

        /// <summary>排空效果伤害记录并广播事实（战斗伤害经 AttackResolved，不在此列；带盾明细——M5·T2.3）。</summary>
        void DrainEffectDamage()
        {
            if (world.EffectDamageLog.Count == 0) return;
            foreach ((SkillEntity source, SkillEntity target, double amount, double shieldAbsorbed) in world.EffectDamageLog)
                Broadcast(new DamageResolved(source.Id, source.Name, target.Id, target.Name,
                    (int)amount, target.Attributes.CurrentInt(AttributeId.CurrentHp),
                    (int)shieldAbsorbed, target.Attributes.CurrentInt(AttributeId.Shield)));
            world.EffectDamageLog.Clear();
        }

        /// <summary>死亡清扫：双方场上 CurrentHp≤0 的单位离场（整包：效果/触发器一并清理）+ 死亡事件。</summary>
        void SweepDeaths()
        {
            SweepBoard(playerBoard);
            SweepBoard(enemyBoard);
        }

        void SweepBoard(List<SkillEntity> board)
        {
            for (int i = board.Count - 1; i >= 0; i--)
            {
                SkillEntity unit = board[i];
                if (unit.Attributes.Current(AttributeId.CurrentHp) > 0) continue;

                board.RemoveAt(i);
                world.Remove(unit);
                // 离场效果（M5·T6：组装怪兽 OnLeave 槽，死亡时一次——Source=亡单位，在效果/触发器清理前执行）
                if (summonPayloads.TryGetValue(unit.Id, out SummonPayload payload))
                {
                    if (payload.OnLeaveEffects != null && payload.OnLeaveEffects.Length > 0)
                        engine.Execute(payload.OnLeaveEffects, new SkillContext { Source = unit });
                    summonPayloads.Remove(unit.Id);
                }
                engine.RemoveEffectsFromSource(unit); // 撤销它施加的效果（光环）
                engine.RemoveEntityEffects(unit);     // 清它身上的效果
                engine.UnregisterTriggers(unit);      // 注销它的触发器
                engine.Raise(new GameplayEvent(BattleEventTags.UnitDied, null, unit, 0, DamageSourceKind.None));
                Broadcast(new UnitDied(unit.Id, unit.Name));
            }
        }

        /// <summary>胜负判定：我方主战者归零=负（先判，同时归零判负）；敌方归零=胜。</summary>
        void EvaluateResult()
        {
            if (result != BattleResult.None) return;
            if (PlayerHp <= 0) result = BattleResult.Defeat;
            else if (EnemyHp <= 0) result = BattleResult.Victory;
            else return;

            flow.MarkGameOver();
            Broadcast(new BattleEnded(result == BattleResult.Victory));
        }

        // ==================== 内部时点（TurnFlow 回调；同程序集可见） ====================

        internal void OnBattleStarted() =>
            Broadcast(new BattleStarted(
                playerLeader.Attributes.CurrentInt(AttributeId.MaxHp),
                enemyLeader.Attributes.CurrentInt(AttributeId.MaxHp)));

        internal void OnSideTurnStarted(bool isPlayer)
        {
            SkillEntity sideLeader = isPlayer ? playerLeader : enemyLeader;
            Broadcast(new TurnStarted(Turn, isPlayer));
            engine.Raise(new GameplayEvent(BattleEventTags.TurnStarted, sideLeader, null, 0, DamageSourceKind.None));
            engine.TickRound(); // 轮次开始：周期执行/倒计时（技能系统§7.3，时长以"方回合"计）

            energyContext.Turn = Turn;
            energyContext.IsPlayerSide = isPlayer;
            energyRule.OnTurnStarted(energyContext);

            if (isPlayer)
            {
                attackedThisTurn.Clear(); // 重置攻击机会（一回合一次）
                DrawOne();                // 每我方回合开始抽 1（牌库空跳过）
            }
            AfterCombat(); // 时点效果（未来）可能致死，统一收口
        }

        internal void OnSideTurnEnded(bool isPlayer) => engine.ExpirePhase();

        internal void RunEnemySummon() => brain.RunSummon(this);

        internal void RunEnemyAttacks() => brain.RunAttacks(this);

        // ==================== 敌方 AI 支撑（EnemyBrain 调用；同程序集可见） ====================

        internal MonsterCardDef NextEnemySummon() =>
            nextEnemySummonIndex < setup.EnemySummons.Count
                ? setup.EnemySummons[nextEnemySummonIndex++]
                : null;

        internal void SummonEnemyMonster(MonsterCardDef def)
        {
            SkillEntity unit = world.AddUnit(BattleWorld.EnemyTeam, def.Name, def.Attack, def.MaxHp);
            enemyBoard.Add(unit);
            engine.Raise(new GameplayEvent(BattleEventTags.UnitSummoned, enemyLeader, unit, 0, DamageSourceKind.None));
            Broadcast(new MonsterSummoned(unit.Id, unit.Name, def.Attack, def.MaxHp, false));
        }

        internal List<SkillEntity> SnapshotEnemyBoard() => new List<SkillEntity>(enemyBoard);

        internal void PerformEnemyAttack(SkillEntity attacker, SkillEntity target)
        {
            PerformAttackCore(attacker, target);
            AfterCombat();
        }

        // ==================== 支撑 ====================

        BattleCard TakeHandCard(int handIndex, bool requireMonster)
        {
            if (handIndex < 0 || handIndex >= hand.Count)
                throw new InvalidOperationException($"手牌索引越界：{handIndex}（现有 {hand.Count} 张）");
            BattleCard card = hand[handIndex];
            if (card.IsMonster != requireMonster)
                throw new InvalidOperationException(requireMonster
                    ? $"「{card.Name}」不是怪兽卡"
                    : $"「{card.Name}」不是效果卡");
            return card;
        }

        SkillEntity ResolveEnemyTarget(int entityId)
        {
            SkillEntity target = world.Find(entityId);
            if (target == null || target.TeamId != BattleWorld.EnemyTeam)
                throw new InvalidOperationException("目标必须是敌方主战者或敌方怪兽");
            return target;
        }

        /// <summary>任意存活实体目标（不依赖主目标的卡用——治疗/增益对己方释放的通路，M5 放开）。</summary>
        SkillEntity ResolveAnyTarget(int entityId)
        {
            SkillEntity target = world.Find(entityId);
            if (target == null)
                throw new InvalidOperationException("目标不存在或已离场");
            return target;
        }

        void PayEnergy(int cost)
        {
            if (energy < cost)
                throw new InvalidOperationException($"能量不足：需要 {cost}，现有 {energy}");
            energy -= cost;
            Broadcast(new EnergyChanged(energy + cost, energy));
        }

        /// <summary>获得能量（回复策略与命令共用出口；变化即广播）。</summary>
        public void GainEnergy(int amount)
        {
            if (amount <= 0) return;
            int before = energy;
            energy += amount;
            Broadcast(new EnergyChanged(before, energy));
        }

        void DrawOne()
        {
            if (deck.Count == 0) return; // 牌库空跳过
            BattleCard card = deck[0];
            deck.RemoveAt(0);
            hand.Add(card);
            engine.Raise(new GameplayEvent(BattleEventTags.CardDrawn, playerLeader, null, 0, DamageSourceKind.None));
            Broadcast(new CardDrawn(card.Name));
        }

        /// <summary>按卡表 id 取卡 ×count 入手牌尾（AddCardToHandOp 的执行通路——"获得复制"类）。</summary>
        void AddCardById(int cardId, int count)
        {
            if (setup.Cards == null)
                throw new InvalidOperationException("宿主未注入卡表 BattleSetup.Cards——AddCardToHand 不可用（配置错误）");
            for (int i = 0; i < count; i++)
            {
                BattleCard card = setup.Cards.Get(cardId); // 未定义即抛（加载期已校验，此处兜底）
                hand.Add(card);
                engine.Raise(new GameplayEvent(BattleEventTags.CardDrawn, playerLeader, null, 0, DamageSourceKind.None));
                Broadcast(new CardDrawn(card.Name));
            }
        }

        static void Broadcast<T>(in T evt) where T : struct => GameEventBus.Send(evt);
    }
}
