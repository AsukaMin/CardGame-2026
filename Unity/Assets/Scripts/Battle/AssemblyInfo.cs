using System.Runtime.CompilerServices;

// 测试程序集需要触达内部成员（Field / ApplyDamage 等）做规则级验证。
[assembly: InternalsVisibleTo("Battle.Tests")]
