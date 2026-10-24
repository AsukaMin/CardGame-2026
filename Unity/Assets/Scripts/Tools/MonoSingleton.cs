using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Tool
{
    ///<summary>
    ///µ¥ÀýÄ£Ê½
    ///</summary>
    public class MonoSingleton<T> : MonoBehaviour where T : MonoSingleton<T>
    {
        private static T instance;
        public static T Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = FindObjectsByType<T>(FindObjectsSortMode.None).First();
                    if (instance == null)
                    {
                        new GameObject("MonoSingleton of " + typeof(T)).AddComponent<T>();
                    }
                    else
                    {
                        instance.Init();
                    }
                }

                return instance;
            }
        }
        public void Awake()
        {
            if (instance == null)
            {
                instance = this as T;
                Init();
            }
        }
        public virtual void Init()
        {

        }
    }
}
