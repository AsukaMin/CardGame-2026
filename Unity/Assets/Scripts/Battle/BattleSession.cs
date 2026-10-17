using System;
using System.Collections.Generic;

namespace Battle
{
    /// <summary>
    /// 一场战斗的会话（门面）。
    /// 契约：UI→核心只有命令方法（返回 PlayResult，带失败原因）；核心→UI 只有 BattleEvents 事实广播。
    /// 回合循环：玩家行动 → 结束回合 → 敌方执行意图+单位攻击 → 揭示下一意图 → 下一玩家回合。
    /// </summary>
    public class BattleSession
    {
        /// <summary>主战者目标 Id 约定（OnHpChanged 等事件同用）。</summary>
        public const int PlayerHeroId = 1;
        public const int EnemyHeroId = 2;

        readonly BattleSetup setup;
        readonly BattleTuning tuning;
        readonly Rng rng;
        readonly EffectInterpreter interpreter;
        readonly EnemyAI ai;
        readonly ActionEconomy economy;
        readonly Deck deck = new Deck();
        readonly Hand hand = new Hand();
        readonly Graveyard graveyard = new Graveyard();
        readonly Battlefield field = new Battlefield();
        readonly List<RosterEntry> roster = new List<RosterEntry>();
        readonly List<MonsterSleeve> enemyPool;
        readonly List<bool> enemyPoolAvailable;

        List<Intent> enemyIntents = new List<Intent>();
        int nextUnitId = 100;
        int nextCardId = 1000;
        bool finished;

        class RosterEntry
        {
            public MonsterSleeve Sleeve;
            public RosterStatus Status = RosterStatus.Available;
        }

        public BattleSession(BattleSetup setup)
        {
            this.setup = setup ?? throw new ArgumentNullException(nameof(setup));
            tuning = setup.Tuning ?? new BattleTuning();
            rng = new Rng(setup.Seed);
            interpreter = new EffectInterpreter(this);
            economy = new ActionEconomy(tuning.InitialActionPoints);
            ai = new EnemyAI(setup.Enemy ?? new EnemyConfig(), rng);

            foreach (MonsterSleeve sleeve in setup.PlayerRoster)
            {
                sleeve.Validate(); // 防御性校验：整备阶段应已保证合法
                roster.Add(new RosterEntry { Sleeve = sleeve });
            }

            enemyPool = setup.EnemySummonPool ?? new List<MonsterSleeve>();
            enemyPoolAvailable = new List<bool>(new bool[enemyPool.Count]);
            for (int i = 0; i < enemyPool.Count; i++)
            {
                enemyPool[i].Validate();
                enemyPoolAvailable[i] = true;
            }

            StartBattle();
        }

        // ---------- 只读状态 ----------

        public BattlePhase Phase { get; private set; } = BattlePhase.NotStarted;
        public int Turn { get; private set; }
        public int PlayerHp { get; private set; }
        public int PlayerMaxHp { get; private set; }
        public int EnemyHp { get; private set; }
        public int EnemyMaxHp { get; private set; }
        public int ActionPoints => economy.Points;
        public int DeckCount => deck.Count;
        public int HandCount => hand.Count;
        public bool Finished => finished;
        public BattleResult Result { get; private set; }
        public IReadOnlyList<Intent> EnemyIntents => enemyIntents;

        internal Battlefield Field => field;
        internal Rng Rng => rng;
        internal ActionEconomy Economy => economy;
        internal List<Intent> EnemyIntentsInternal => enemyIntents;

        // ---------- 命令（仅 PlayerMain 阶段） ----------

        /// <summary>从手牌打出效果卡：选充能档位、按面值付费（永不吃公式折扣）。</summary>
        public PlayResult PlayCard(int handIndex, int tierIndex, int chosenTargetId = 0)
        {
            PlayResult gate = RequirePlayerMain();
            if (gate != null) return gate;

            EffectCardInstance card = hand.GetAt(handIndex);
            if (card == null)
                return PlayResult.Fail(FailReason.CardNotFound, $"手牌索引 {handIndex} 无效");
            if (tierIndex < 0 || tierIndex >= card.Config.Tiers.Length)
                return PlayResult.Fail(FailReason.TierNotFound, $"档位 {tierIndex} 不存在");

            EffectTier tier = card.Config.Tiers[tierIndex];

            if (EffectInterpreter.NeedsChosenTarget(tier))
            {
                if (chosenTargetId <= 0 || !TargetResolves(chosenTargetId))
                    return PlayResult.Fail(FailReason.InvalidTarget, "该效果需要指定一个有效目标");
                if (EffectInterpreter.NeedsUnitTarget(tier) && field.GetUnit(chosenTargetId) == null)
                    return PlayResult.Fail(FailReason.RequiresUnitTarget, "该效果需要指定一个单位目标（主战者不可）");
            }

            if (!economy.TrySpend(tier.Cost))
                return PlayResult.Fail(FailReason.NotEnoughPoints,
                    $"行动点不足：需要 {tier.Cost}，当前 {economy.Points}");

            hand.Remove(card);
            BattleEvents.FireCardPlayed(card.Id);
            interpreter.Execute(tier.Effects, Side.Player, null,
                chosenTargetId > 0 ? chosenTargetId : (int?)null);
            return PlayResult.Ok();
        }

        /// <summary>召唤名单中的怪兽卡套（每张每场限一次；无召唤失调，当回合可攻击）。</summary>
        public PlayResult SummonMonster(int rosterIndex)
        {
            PlayResult gate = RequirePlayerMain();
            if (gate != null) return gate;

            if (rosterIndex < 0 || rosterIndex >= roster.Count)
                return PlayResult.Fail(FailReason.SleeveNotAvailable, $"名单索引 {rosterIndex} 无效");
            RosterEntry entry = roster[rosterIndex];
            if (entry.Status != RosterStatus.Available)
                return PlayResult.Fail(FailReason.SleeveNotAvailable,
                    $"卡套[{entry.Sleeve.Config.Name}]已召唤或已阵亡");
            if (tuning.FieldUnitCap > 0 && field.CountOf(Side.Player) >= tuning.FieldUnitCap)
                return PlayResult.Fail(FailReason.FieldCapReached, "场上单位已达上限");

            int cost = entry.Sleeve.SummonCost;
            if (!economy.TrySpend(cost))
                return PlayResult.Fail(FailReason.NotEnoughPoints,
                    $"行动点不足：召唤需 {cost}，当前 {economy.Points}");

            SummonUnit(entry.Sleeve, Side.Player);
            entry.Status = RosterStatus.OnField;
            return PlayResult.Ok();
        }

        /// <summary>我方单位攻击：自由选目标（敌方单位或敌方主战者），嘲讽必须优先处理。</summary>
        public PlayResult Attack(int attackerUnitId, int targetId)
        {
            PlayResult gate = RequirePlayerMain();
            if (gate != null) return gate;

            BattleUnit attacker = field.GetUnit(attackerUnitId);
            if (attacker == null || !attacker.Alive)
                return PlayResult.Fail(FailReason.UnitNotFound, $"单位 #{attackerUnitId} 不存在");
            if (attacker.Owner != Side.Player)
                return PlayResult.Fail(FailReason.NotFriendlyUnit, "只能指挥我方单位攻击");
            if (attacker.AttackedThisTurn)
                return PlayResult.Fail(FailReason.AlreadyAttacked, "该单位本回合已攻击");

            bool targetIsHero = targetId == EnemyHeroId;
            BattleUnit targetUnit = targetIsHero ? null : field.GetUnit(targetId);
            if (!targetIsHero && (targetUnit == null || !targetUnit.Alive || targetUnit.Owner != Side.Enemy))
                return PlayResult.Fail(FailReason.InvalidTarget, $"攻击目标 #{targetId} 无效");

            // 嘲讽：对方场上有嘲讽单位时，攻击只能指向嘲讽单位（打脸也被拦截）
            if (field.AnyTaunt(Side.Enemy))
            {
                bool hittingTaunt = !targetIsHero && targetUnit.HasKeyword(Keywords.Taunt);
                if (!hittingTaunt)
                    return PlayResult.Fail(FailReason.TauntViolation, "敌方存在嘲讽单位，必须先攻击嘲讽单位");
            }

            attacker.AttackedThisTurn = true;
            if (targetIsHero)
                ApplyDamage(new EffectTarget(Side.Enemy), attacker.EffectiveAttack);
            else
                ResolveUnitCombat(attacker, targetUnit);

            setup.Recovery?.OnFriendlyAttackCompleted(NewRecoveryContext(attackerUnitId, 0));
            return PlayResult.Ok();
        }

        /// <summary>主动弃一张手牌（收益由回复策略决定，核心不默认给点）。</summary>
        public PlayResult Discard(int handIndex)
        {
            PlayResult gate = RequirePlayerMain();
            if (gate != null) return gate;

            EffectCardInstance card = hand.GetAt(handIndex);
            if (card == null)
                return PlayResult.Fail(FailReason.CardNotFound, $"手牌索引 {handIndex} 无效");

            hand.Remove(card);
            BattleEvents.FireCardDiscarded(card.Id);
            setup.Recovery?.OnCardDiscarded(NewRecoveryContext(0, card.Id));
            return PlayResult.Ok();
        }

        /// <summary>结束玩家回合：执行敌方回合（意图→敌方单位攻击→揭示下一意图），随后进入下一玩家回合。</summary>
        public PlayResult EndTurn()
        {
            PlayResult gate = RequirePlayerMain();
            if (gate != null) return gate;

            ExpireThisTurnAll();
            Phase = BattlePhase.EnemyTurn;
            ResetAttackFlags(Side.Enemy);
            RunEnemyTurn();
            if (finished) return PlayResult.Ok();

            Turn++;
            Phase = BattlePhase.PlayerMain;
            foreach (BattleUnit u in field.AllUnits) u.TickTurnDurations();
            ResetAttackFlags(Side.Player);
            BattleEvents.FireTurnStart(Turn);
            DrawCards(tuning.CardsDrawnPerTurn);
            setup.Recovery?.OnTurnStart(NewRecoveryContext(0, 0));
            return PlayResult.Ok();
        }

        // ---------- 快照 ----------

        public BattleSnapshot GetSnapshot()
        {
            BattleSnapshot snap = new BattleSnapshot
            {
                Phase = Phase,
                Turn = Turn,
                Finished = finished,
                PlayerWon = Result != null && Result.PlayerWon,
                PlayerHp = PlayerHp,
                PlayerMaxHp = PlayerMaxHp,
                EnemyHp = EnemyHp,
                EnemyMaxHp = EnemyMaxHp,
                ActionPoints = economy.Points,
                DeckCount = deck.Count,
                HandCount = hand.Count,
                GraveyardPackageCount = graveyard.Count,
            };

            foreach (BattleUnit u in field.AllUnits)
                snap.Units.Add(new BattleSnapshot.UnitView
                {
                    Id = u.Id,
                    Owner = u.Owner,
                    Name = u.Name,
                    Hp = u.Hp,
                    MaxHp = u.EffectiveMaxHp,
                    Attack = u.EffectiveAttack,
                    IsTaunt = u.HasKeyword(Keywords.Taunt),
                    AttackedThisTurn = u.AttackedThisTurn,
                });

            foreach (EffectCardInstance c in hand.Cards)
            {
                int[] costs = new int[c.Config.Tiers.Length];
                string[] labels = new string[c.Config.Tiers.Length];
                for (int i = 0; i < c.Config.Tiers.Length; i++)
                {
                    costs[i] = c.Config.Tiers[i].Cost;
                    labels[i] = c.Config.Tiers[i].Label;
                }
                snap.Hand.Add(new BattleSnapshot.HandCardView
                {
                    CardInstanceId = c.Id,
                    ConfigId = c.Config.Id,
                    Name = c.Config.Name,
                    TierCosts = costs,
                    TierLabels = labels,
                });
            }

            for (int i = 0; i < roster.Count; i++)
            {
                (int atk, int hp) = roster[i].Sleeve.BaseStats;
                snap.Roster.Add(new BattleSnapshot.RosterView
                {
                    RosterIndex = i,
                    ConfigId = roster[i].Sleeve.Config.Id,
                    Name = roster[i].Sleeve.Config.Name,
                    SummonCost = roster[i].Sleeve.SummonCost,
                    Attack = atk,
                    MaxHp = hp,
                    Status = roster[i].Status,
                });
            }

            foreach (Intent intent in enemyIntents)
                snap.EnemyIntents.Add(intent.Describe());
            return snap;
        }

        // ---------- 内部：开局 ----------

        void StartBattle()
        {
            PlayerHp = PlayerMaxHp = tuning.PlayerMaxHp;
            EnemyHp = EnemyMaxHp = setup.Enemy.MaxHp;

            List<EffectCardInstance> instances = new List<EffectCardInstance>();
            foreach (EffectCardConfig cfg in setup.DeckCards)
                instances.Add(new EffectCardInstance { Id = nextCardId++, Config = cfg });

            if (setup.FixedDeckOrder != null)
            {
                // 固定顺序（测试复现）：按配置 Id 排列，同 Id 按组队顺序消耗
                Dictionary<int, Queue<EffectCardInstance>> byConfig =
                    new Dictionary<int, Queue<EffectCardInstance>>();
                foreach (EffectCardInstance inst in instances)
                {
                    if (!byConfig.TryGetValue(inst.Config.Id, out Queue<EffectCardInstance> queue))
                    {
                        queue = new Queue<EffectCardInstance>();
                        byConfig[inst.Config.Id] = queue;
                    }
                    queue.Enqueue(inst);
                }
                List<EffectCardInstance> ordered = new List<EffectCardInstance>();
                foreach (int id in setup.FixedDeckOrder)
                    if (byConfig.TryGetValue(id, out Queue<EffectCardInstance> queue) && queue.Count > 0)
                        ordered.Add(queue.Dequeue());
                foreach (EffectCardInstance inst in instances) // 防御：未列出的剩余卡接在末尾
                    if (!ordered.Contains(inst)) ordered.Add(inst);
                deck.SetCards(ordered);
            }
            else
            {
                deck.SetCards(instances);
                deck.Shuffle(rng);
            }

            DrawCards(tuning.StartingHandSize);

            enemyIntents = new List<Intent> { ai.GenerateIntent(field, enemyPoolAvailable) };
            BattleEvents.FireIntentRevealed(enemyIntents);

            Turn = 1;
            Phase = BattlePhase.PlayerMain;
            BattleEvents.FireTurnStart(Turn);
            setup.Recovery?.OnTurnStart(NewRecoveryContext(0, 0));
        }

        // ---------- 内部：敌方回合 ----------

        void RunEnemyTurn()
        {
            List<Intent> intents = new List<Intent>(enemyIntents);
            foreach (Intent intent in intents)
            {
                if (finished) return;
                ExecuteIntent(intent);
            }
            if (finished) return;

            List<BattleUnit> enemyUnits = field.UnitsOf(Side.Enemy);
            foreach (BattleUnit unit in enemyUnits)
            {
                if (finished) return;
                if (!unit.Alive || unit.AttackedThisTurn) continue;
                unit.AttackedThisTurn = true;
                EffectTarget target = ai.PickUnitAttackTarget(field);
                if (target.IsUnit)
                    ResolveUnitCombat(unit, target.Unit);
                else
                    ApplyDamage(target, Math.Max(0, unit.EffectiveAttack));
            }
            if (finished) return;

            ExpireThisTurnAll();
            enemyIntents = new List<Intent> { ai.GenerateIntent(field, enemyPoolAvailable) };
            BattleEvents.FireIntentRevealed(enemyIntents);
        }

        void ExecuteIntent(Intent intent)
        {
            switch (intent)
            {
                case AttackPlayerIntent attackPlayer:
                    ApplyDamage(new EffectTarget(Side.Player), attackPlayer.Damage);
                    break;

                case AttackUnitIntent attackUnit:
                    BattleUnit target = field.GetUnit(attackUnit.TargetUnitId);
                    if (target == null || !target.Alive)
                    {
                        // 预选目标已死：重定向到玩家本人（意图谜面仍可读）
                        ApplyDamage(new EffectTarget(Side.Player), attackUnit.Damage);
                    }
                    else
                    {
                        ApplyDamage(new EffectTarget(target), attackUnit.Damage);
                    }
                    break;

                case SummonIntent summon:
                    if (summon.PoolIndex >= 0 && summon.PoolIndex < enemyPool.Count
                        && enemyPoolAvailable[summon.PoolIndex]
                        && (tuning.FieldUnitCap <= 0 || field.CountOf(Side.Enemy) < tuning.FieldUnitCap))
                    {
                        // 敌方召唤不走行动点（PvE 规则不对称）
                        SummonUnit(enemyPool[summon.PoolIndex], Side.Enemy);
                        enemyPoolAvailable[summon.PoolIndex] = false;
                    }
                    break;

                case CastIntent cast:
                    interpreter.Execute(cast.Effects, Side.Enemy, null, null);
                    break;
            }
        }

        // ---------- 内部：召唤/攻击/伤害/死亡 ----------

        void SummonUnit(MonsterSleeve sleeve, Side side)
        {
            (int atk, int hp) = sleeve.BaseStats;
            int maxHp = Math.Max(1, hp);
            BattleUnit unit = new BattleUnit
            {
                Id = nextUnitId++,
                Owner = side,
                Name = sleeve.Config.Name,
                BaseAttack = atk,
                BaseMaxHp = maxHp,
                Hp = maxHp,
                Sleeve = sleeve,
            };
            foreach (PartCardConfig part in sleeve.AllParts)
                unit.BaseKeywords |= part.GrantKeywords;

            field.Add(unit);
            BattleEvents.FireUnitSummoned(unit.Id);

            // 入场槽：召唤时触发
            foreach (PartCardConfig part in sleeve.PartsOf(SlotType.OnSummon))
                interpreter.Execute(part.Effects, side, unit.Id, null);

            RecomputeAuras();
            ProcessDeaths();     // 入场伤害可能直接击杀
            CheckBattleEnd();
        }

        void ResolveUnitCombat(BattleUnit attacker, BattleUnit defender)
        {
            int atkToDef = Math.Max(0, attacker.EffectiveAttack);
            int defToAtk = Math.Max(0, defender.EffectiveAttack);
            // 同时结算：互殴伤害一起落地
            defender.Hp = Math.Max(0, defender.Hp - atkToDef);
            attacker.Hp = Math.Max(0, attacker.Hp - defToAtk);
            BattleEvents.FireHpChanged(defender.Id, defender.Hp);
            BattleEvents.FireHpChanged(attacker.Id, attacker.Hp);
            ProcessDeaths();
            CheckBattleEnd();
        }

        internal void ApplyDamage(EffectTarget target, int amount)
        {
            int dmg = Math.Max(0, amount);
            if (target.IsUnit)
            {
                target.Unit.Hp = Math.Max(0, target.Unit.Hp - dmg);
                BattleEvents.FireHpChanged(target.Unit.Id, target.Unit.Hp);
                if (target.Unit.Hp <= 0) ProcessDeaths();
            }
            else if (target.HeroSide == Side.Player)
            {
                PlayerHp = Math.Max(0, PlayerHp - dmg);
                BattleEvents.FireHpChanged(PlayerHeroId, PlayerHp);
                CheckBattleEnd();
            }
            else
            {
                EnemyHp = Math.Max(0, EnemyHp - dmg);
                BattleEvents.FireHpChanged(EnemyHeroId, EnemyHp);
                CheckBattleEnd();
            }
        }

        internal void ApplyHeal(EffectTarget target, int amount)
        {
            if (amount <= 0) return;
            if (target.IsUnit)
            {
                int before = target.Unit.Hp;
                target.Unit.Hp = Math.Min(target.Unit.EffectiveMaxHp, target.Unit.Hp + amount);
                if (target.Unit.Hp != before) BattleEvents.FireHpChanged(target.Unit.Id, target.Unit.Hp);
            }
            else if (target.HeroSide == Side.Player)
            {
                int before = PlayerHp;
                PlayerHp = Math.Min(PlayerMaxHp, PlayerHp + amount);
                if (PlayerHp != before) BattleEvents.FireHpChanged(PlayerHeroId, PlayerHp);
            }
            else
            {
                int before = EnemyHp;
                EnemyHp = Math.Min(EnemyMaxHp, EnemyHp + amount);
                if (EnemyHp != before) BattleEvents.FireHpChanged(EnemyHeroId, EnemyHp);
            }
        }

        /// <summary>死亡结算：整包进墓地、离场槽触发（可能连锁新死亡，循环带防护）。</summary>
        void ProcessDeaths()
        {
            int guard = 0;
            while (true)
            {
                List<BattleUnit> dead = null;
                foreach (BattleUnit u in field.AllUnits)
                    if (u.Alive && u.Hp <= 0)
                        (dead ??= new List<BattleUnit>()).Add(u);
                if (dead == null) break;
                if (++guard > 64) break; // 连锁死亡防护

                foreach (BattleUnit u in dead)
                {
                    u.Alive = false;
                    field.Remove(u);
                    graveyard.Add(u.Sleeve); // 整包：卡套连同槽内部位卡

                    // 名单状态推进（玩家侧）
                    foreach (RosterEntry entry in roster)
                        if (entry.Status == RosterStatus.OnField && entry.Sleeve == u.Sleeve)
                            entry.Status = RosterStatus.Dead;

                    // 离场槽：死亡时触发（单位已移出场，效果上下文仍指向它）
                    foreach (PartCardConfig part in u.Sleeve.PartsOf(SlotType.OnLeave))
                        interpreter.Execute(part.Effects, u.Owner, u.Id, null);

                    BattleEvents.FireUnitDied(u.Id);
                }

                RecomputeAuras(); // 光环随离场失效
                CheckBattleEnd();
                if (finished) return;
            }
        }

        /// <summary>常驻槽光环重算：清空所有光环桶 → 逐单位施加其常驻部位效果（仅属性/关键词操作生效）。</summary>
        void RecomputeAuras()
        {
            foreach (BattleUnit u in field.AllUnits) u.ClearAuras();
            foreach (BattleUnit source in field.AllUnits)
            {
                foreach (PartCardConfig part in source.Sleeve.PartsOf(SlotType.Continuous))
                {
                    if (part.Effects == null) continue;
                    foreach (EffectDef def in part.Effects)
                    {
                        if (def == null) continue;
                        List<EffectTarget> targets =
                            interpreter.ResolveTargets(def.Target, source.Owner, source.Id, null);
                        foreach (EffectTarget target in targets)
                        {
                            if (!target.IsUnit) continue;
                            foreach (EffectOp op in def.Ops)
                            {
                                switch (op)
                                {
                                    case ModifyStatOp mod:
                                        target.Unit.AddAura(new StatModifier
                                        {
                                            Stat = mod.Stat,
                                            Amount = mod.Amount,
                                            AuraSource = source,
                                        });
                                        break;
                                    case GrantKeywordOp grant:
                                        target.Unit.AddAura(new StatModifier
                                        {
                                            IsKeywordMod = true,
                                            Keyword = grant.Keyword,
                                            AuraSource = source,
                                        });
                                        break;
                                    // 常驻槽只接受属性/关键词操作，其余忽略（数据配置应保证）
                                }
                            }
                        }
                    }
                }
            }
        }

        void CheckBattleEnd()
        {
            if (finished) return;
            if (EnemyHp <= 0) Finish(true);
            else if (PlayerHp <= 0) Finish(false);
        }

        void Finish(bool playerWon)
        {
            finished = true;
            Phase = BattlePhase.Finished;
            BattleResult result = new BattleResult
            {
                PlayerWon = playerWon,
                Turns = Turn,
                RemainingActionPoints = economy.Points,
            };
            foreach (RosterEntry entry in roster)
            {
                foreach (PartCardConfig part in entry.Sleeve.AllParts)
                {
                    if (entry.Status == RosterStatus.Dead && part.Consumable)
                        result.DestroyedPartCardIds.Add(part.Id);
                    else
                        result.RecoverablePartCardIds.Add(part.Id);
                }
            }
            foreach (BattleUnit u in field.UnitsOf(Side.Player))
                result.SurvivorUnitIds.Add(u.Id);

            Result = result;
            BattleEvents.FireBattleEnd(result);
        }

        // ---------- 内部：杂项 ----------

        PlayResult RequirePlayerMain()
        {
            if (finished)
                return PlayResult.Fail(FailReason.BattleFinished, "战斗已结束");
            if (Phase != BattlePhase.PlayerMain)
                return PlayResult.Fail(FailReason.NotPlayerMain, "当前不是玩家行动阶段");
            return null;
        }

        internal void DrawCards(int count)
        {
            for (int i = 0; i < count; i++)
            {
                EffectCardInstance card = deck.DrawTop();
                if (card == null) break; // 牌库空：不抽、无疲劳
                hand.Add(card);
                BattleEvents.FireCardDrawn(card.Id);
            }
        }

        void ResetAttackFlags(Side side)
        {
            foreach (BattleUnit u in field.UnitsOf(side)) u.ResetAttackFlag();
        }

        void ExpireThisTurnAll()
        {
            foreach (BattleUnit u in field.AllUnits) u.ExpireThisTurn();
        }

        RecoveryContext NewRecoveryContext(int attackerUnitId, int discardedCardId) =>
            new RecoveryContext
            {
                Side = Side.Player,
                TurnNumber = Turn,
                AttackerUnitId = attackerUnitId,
                DiscardedCardInstanceId = discardedCardId,
                Economy = economy,
            };

        bool TargetResolves(int targetId)
        {
            if (targetId == PlayerHeroId || targetId == EnemyHeroId) return true;
            BattleUnit u = field.GetUnit(targetId);
            return u != null && u.Alive;
        }

        internal int HpOf(Side side) => side == Side.Player ? PlayerHp : EnemyHp;
        internal int MaxHpOf(Side side) => side == Side.Player ? PlayerMaxHp : EnemyMaxHp;
    }
}
