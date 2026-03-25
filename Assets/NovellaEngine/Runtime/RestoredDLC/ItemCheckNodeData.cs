using UnityEngine;
using NovellaEngine.Data;

namespace NovellaEngine.DLC.Inventory
{
    [NovellaDLCNode("Check Inventory Item", "Item Check", "#2e8c62", "1.0", Description = "Test DLC")]
    [System.Serializable]
    public class ItemCheckNodeData : NovellaNodeBase
    {
        public override ENodeType NodeType => ENodeType.CustomDLC;

        public string ItemIDToNeed = "Key_01";
        public bool RemoveItemOnSuccess = true;

        public string TrueNextNodeID;
        public string FalseNextNodeID;
    }
}