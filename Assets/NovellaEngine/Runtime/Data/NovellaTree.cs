/// <summary>
/// Главный ScriptableObject графа новеллы. 
/// Теперь использует полиморфную сериализацию [SerializeReference] для поддержки любых кастомных нод (DLC).
/// </summary>
using System.Collections.Generic;
using UnityEngine;

namespace NovellaEngine.Data
{
    [CreateAssetMenu(fileName = "NewNovellaTree", menuName = "Novella Engine/Novella Tree")]
    public class NovellaTree : ScriptableObject
    {
        public string RootNodeID;
        public Vector2 StartPosition = new Vector2(50, 200); 
        public bool EnableAutoSave = false;
        public float AutoSaveInterval = 30f;

        [SerializeReference]
        public List<NovellaNodeBase> Nodes = new List<NovellaNodeBase>();

        [SerializeReference]
        public List<NovellaGroupData> Groups = new List<NovellaGroupData>();
    }
}