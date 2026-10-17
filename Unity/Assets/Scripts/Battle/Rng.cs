using System;
using System.Collections.Generic;

namespace Battle
{
    /// <summary>种子化随机源：战斗内一切随机（洗牌/投骰/AI 选择/随机目标）都必须走它，同一种子可完整复现（板块划分文档：方便复现 bug）。</summary>
    public sealed class Rng
    {
        readonly Random random;

        public Rng(int seed) => random = new Random(seed);

        /// <summary>[min, max) 整数。</summary>
        public int Next(int minInclusive, int maxExclusive) => random.Next(minInclusive, maxExclusive);

        /// <summary>[0, max) 整数。</summary>
        public int Next(int maxExclusive) => random.Next(maxExclusive);

        public double NextDouble() => random.NextDouble();

        /// <summary>等概率取一个；空列表抛异常。</summary>
        public T Pick<T>(IReadOnlyList<T> list)
        {
            if (list == null || list.Count == 0) throw new ArgumentException("随机取样的列表为空");
            return list[random.Next(list.Count)];
        }

        /// <summary>Fisher–Yates 洗牌。</summary>
        public void Shuffle<T>(IList<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
