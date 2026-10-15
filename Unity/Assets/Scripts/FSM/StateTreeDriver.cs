using System.Collections.Generic;
using UnityEngine;

namespace FSM
{
    /// <summary>把 StateTree 挂到 Unity 生命周期：AddComponent 后 Register 即自动 Tick</summary>
    public class StateTreeDriver : MonoBehaviour
    {
        private readonly List<StateTree> trees = new List<StateTree>();

        public T Register<T>(T tree) where T : StateTree
        {
            if (tree != null && !trees.Contains(tree)) trees.Add(tree);
            return tree;
        }

        public void Unregister(StateTree tree) => trees.Remove(tree);

        private void Update()
        {
            for (int i = 0; i < trees.Count; i++) trees[i].Tick();
        }

        private void OnDestroy()
        {
            trees.Clear();
        }
    }
}
