using System.Collections.Generic;
using UnityEngine;

namespace NovellaEngine.Data
{
    [CreateAssetMenu(fileName = "NewNovellaTree", menuName = "Novella Engine/Novella Tree")]
    public class NovellaTree : ScriptableObject
    {
        public string RootNodeID;
        public Vector2 StartPosition = new Vector2(50, 200);

        [SerializeReference]
        public List<NovellaNodeData> Nodes = new List<NovellaNodeData>();
    }
}