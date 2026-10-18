using System;
using System.Collections.Generic;
using GameEvents;
using NUnit.Framework;

namespace GameEvents.Tests
{
    /// <summary>全局事件总线用例（《事件系统设计》本期清单）。</summary>
    [TestFixture]
    public class GameEventBusTests
    {
        struct FakeEvent { public int Value; }

        [SetUp]
        public void SetUp() => GameEventBus.ClearAll();

        [TearDown]
        public void TearDown() => GameEventBus.ClearAll();

        [Test]
        public void 订阅与接收()
        {
            int received = 0;
            GameEventBus.Subscribe<FakeEvent>(e => received = e.Value);
            GameEventBus.Send(new FakeEvent { Value = 42 });
            Assert.AreEqual(42, received);
        }

        [Test]
        public void 退订后不再接收()
        {
            int count = 0;
            void Handler(FakeEvent e) => count++;
            GameEventBus.Subscribe<FakeEvent>(Handler);
            GameEventBus.Unsubscribe<FakeEvent>(Handler);
            GameEventBus.Send(new FakeEvent());
            Assert.AreEqual(0, count);
        }

        [Test]
        public void 无订阅时发送不抛异常()
        {
            Assert.DoesNotThrow(() => GameEventBus.Send(new FakeEvent()));
        }

        [Test]
        public void 多订阅按订阅序派发()
        {
            var order = new List<int>();
            GameEventBus.Subscribe<FakeEvent>(e => order.Add(1));
            GameEventBus.Subscribe<FakeEvent>(e => order.Add(2));
            GameEventBus.Subscribe<FakeEvent>(e => order.Add(3));
            GameEventBus.Send(new FakeEvent());
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, order);
        }

        [Test]
        public void 同一处理器重复订阅去重()
        {
            int count = 0;
            void Handler(FakeEvent e) => count++;
            GameEventBus.Subscribe<FakeEvent>(Handler);
            GameEventBus.Subscribe<FakeEvent>(Handler);
            GameEventBus.Send(new FakeEvent());
            Assert.AreEqual(1, count);
        }

        [Test]
        public void 回调内退订自身安全()
        {
            int count = 0;
            Action<FakeEvent> handler = null;
            handler = e =>
            {
                count++;
                GameEventBus.Unsubscribe(handler);
            };
            GameEventBus.Subscribe(handler);
            GameEventBus.Send(new FakeEvent());
            GameEventBus.Send(new FakeEvent());
            Assert.AreEqual(1, count);
        }

        [Test]
        public void 回调内再Send抛异常()
        {
            GameEventBus.Subscribe<FakeEvent>(e => GameEventBus.Send(new FakeEvent()));
            Assert.Throws<InvalidOperationException>(() => GameEventBus.Send(new FakeEvent()));
        }
    }
}
