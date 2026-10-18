using System.Runtime.CompilerServices;

// 测试程序集需要触达内部成员（管线/队列细节）做规则级验证。
[assembly: InternalsVisibleTo("Battle.Tests")]
