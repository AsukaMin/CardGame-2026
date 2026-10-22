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
            // 起点=测试程序集所在目录：headless 在 .zcode/tmp/…/bin、EditMode 在 Library/ScriptAssemblies——
            // 注意 AppContext.BaseDirectory 在编辑器下指向 Unity 安装目录（E:\...\Editor），不能用作起点
            string assemblyDir = Path.GetDirectoryName(typeof(TestDataPaths).Assembly.Location);
            DirectoryInfo dir = new DirectoryInfo(string.IsNullOrEmpty(assemblyDir) ? AppContext.BaseDirectory : assemblyDir);
            for (; dir != null; dir = dir.Parent)
            {
                string candidate = Path.Combine(dir.FullName, "GameData", "cards.json");
                if (File.Exists(candidate)) return candidate;
            }
            throw new InvalidOperationException(
                $"未找到 GameData/cards.json（从 {assemblyDir} 逐级向上至盘根）——测试数据缺失");
        }
    }
}
