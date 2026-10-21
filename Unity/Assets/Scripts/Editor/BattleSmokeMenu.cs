using System.IO;
using Battle;
using Presentation;
using UnityEditor;
using UnityEngine;

/// <summary>编辑器冒烟入口：自动跑完一场演示对局，全程事实经占位表现输出到控制台（M1 验收用）。</summary>
public static class BattleSmokeMenu
{
    /// <summary>固定种子，保证每次冒烟对局一致（肉眼核对用）。</summary>
    const int SmokeSeed = 20261019;

    [MenuItem("Tools/战斗核心/跑一场冒烟对局")]
    public static void RunSmokeBattle()
    {
        AutoBattleResult result;
        using (BattleDebugPresenter presenter = BattleDebugPresenter.Attach())
        {
            result = AutoBattle.Run(DemoBattleData.CreateSetup(SmokeSeed));
        }
        Debug.Log($"[战斗] 冒烟对局结束：{(result.Result == BattleResult.Victory ? "玩家胜利" : "玩家失败")}，共 {result.Turns} 回合");
    }

    /// <summary>卡表加载校验（M2 数据管线冒烟）：读仓库根 GameData/cards.json，配置错误即在此暴露。</summary>
    [MenuItem("Tools/战斗核心/加载卡表校验")]
    public static void LoadCardsJson()
    {
        string repoRoot = Directory.GetParent(Application.dataPath).Parent.FullName; // Assets→Unity→仓库根
        string json = File.ReadAllText(Path.Combine(repoRoot, "GameData", "cards.json"));
        CardCatalog catalog = CardCatalog.Load(json);
        int monsters = 0, effects = 0;
        foreach (BattleCard card in catalog.Entries)
        {
            if (card.IsMonster) monsters++;
            else effects++;
        }
        Debug.Log($"[战斗] 卡表加载：{catalog.Count} 张（效果 {effects} / 怪兽 {monsters}）——校验通过");
    }
}
