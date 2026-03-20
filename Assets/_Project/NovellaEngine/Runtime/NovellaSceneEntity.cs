using UnityEngine;

namespace NovellaEngine.Runtime
{
    [ExecuteAlways]
    public class NovellaSceneEntity : MonoBehaviour
    {
        public string LinkedNodeID;

        public void Initialize(string nodeID)
        {
            LinkedNodeID = nodeID;
            gameObject.hideFlags = HideFlags.NotEditable;
        }
    }
}