using System;
using System.IO;

namespace Battle.Tests
{
    /// <summary>
    /// 测试数据定位：从程序集目录逐级向上找仓库根的 GameData/cards.json（双通道同源——
    /// headless 在 .zcode/tmp/…/bin 下、EditMode 在 Library/… 下，向上走均能命中仓库根）。
    /// 仓库里唯一允许碰文件路径的测试侧代码（目录纪律：Battle 程序集本体零 IO）。
    /// </summary>
    public static class TestDataPaths
    {
        public static string CardsJson()
        {
            DirectoryInfo dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
            {
                string candidate = Path.Combine(dir.FullName, "GameData", "cards.json");
                if (File.Exists(candidate)) return candidate;
            }
            throw new InvalidOperationException(
                $"未找到 GameData/cards.json（从 {AppContext.BaseDirectory} 向上 8 级）——测试数据缺失");
        }
    }
}
