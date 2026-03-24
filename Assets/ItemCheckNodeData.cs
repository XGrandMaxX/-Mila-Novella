using UnityEngine;
using NovellaEngine.Data;
using NovellaEngine.Runtime;

namespace NovellaEngine.DLC.Inventory
{
    [NovellaDLCNode("Check Inventory Item", "Item Check", "#2e8c62", "1.0")]
    [System.Serializable]
    public class ItemCheckNodeData : NovellaNodeBase, INovellaDLCExecutable
    {
        public override ENodeType NodeType => ENodeType.CustomDLC;

        [Header("Item Settings")]
        public string ItemIDToNeed = "Key_01";
        public bool RemoveItemOnSuccess = true;

        [NovellaDLCOutput("🟢 True (Has Item)")]
        [HideInInspector]
        public string TrueNextNodeID;

        [NovellaDLCOutput("🔴 False (No Item)")]
        [HideInInspector]
        public string FalseNextNodeID;

        public string Execute(NovellaPlayer player)
        {
            bool hasItem = true;

            if (hasItem)
            {
                Debug.Log($"[DLC Inventory] Предмет {ItemIDToNeed} найден! Идем по зеленой ветке.");

                if (RemoveItemOnSuccess)
                {
                    Debug.Log($"[DLC Inventory] Предмет {ItemIDToNeed} удален из инвентаря.");
                }

                return TrueNextNodeID;
            }
            else
            {
                Debug.Log($"[DLC Inventory] Предмета {ItemIDToNeed} нет! Идем по красной ветке.");

                return FalseNextNodeID;
            }
        }
    }
}