using UnityEngine;
using System.Collections.Generic;

namespace NovellaEngine.Editor
{
    [System.Serializable]
    public class NovellaTip
    {
        [TextArea(2, 4)] public string Text_RU;
        [TextArea(2, 4)] public string Text_EN;
    }

    [CreateAssetMenu(fileName = "NovellaTipsData", menuName = "Novella Engine/Tips Data")]
    public class NovellaTipsData : ScriptableObject
    {
        [Space]
        public List<NovellaTip> Tips = new List<NovellaTip>();
    }
}