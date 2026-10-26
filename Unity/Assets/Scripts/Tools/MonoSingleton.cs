using System.Linq;
using UnityEngine;

namespace Tool
{
    /// <summary>
    /// MonoBehaviour 单例基类：场景物体的 Awake 自动注册首个实例并销毁重复实例；
    /// 访问 Instance 时场景里没有就自动创建一个。
    /// 子类【不要写 Awake】——会隐藏本类的注册逻辑（编译器 CS0108 警告即此因）；
    /// 初始化一律 override Init()，保证每个物体恰好执行一次。
    /// 禁域重载（Enter Play Options）适配：跨 Play 残留的静态 instance 是 Unity 假 null，
    /// getter 的 null 判断会触发重新查找，跨会话自愈。
    /// </summary>
    public class MonoSingleton<T> : MonoBehaviour where T : MonoSingleton<T>
    {
        static T instance;
        bool initialized; // Init 幂等：早于 Awake 被访问 / 假 null 重找回，都只跑一次

        public static T Instance
        {
            get
            {
                if (instance == null) // 已销毁对象在这里同样是 null（Unity 假 null 语义）
                {
                    instance = FindObjectsByType<T>(FindObjectsSortMode.None).FirstOrDefault();
                    if (instance == null)
                        instance = new GameObject(typeof(T).Name).AddComponent<T>(); // Awake 同步跑：注册 + Init 走那条路
                    else
                        instance.EnsureInit(); // 它自己的 Awake 还没跑或已跑过——幂等补齐
                }
                return instance;
            }
        }

        protected void Awake() // 非虚：子类不得隐藏；初始化钩子在 Init
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject); // 重复实例整物体销毁
                return;
            }
            instance = (T)this;
            EnsureInit();
        }

        /// <summary>首次注册或首次被访问时的初始化钩子，每个物体恰好一次。</summary>
        protected virtual void Init() { }

        void EnsureInit()
        {
            if (initialized) return;
            initialized = true;
            Init();
        }
    }
}
