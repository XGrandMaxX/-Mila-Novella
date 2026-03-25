using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using System.IO;
#endif
using NovellaEngine.Runtime;

namespace NovellaEngine.Data
{
    public interface INovellaDLCExecutable
    {
        /// <summary>
        /// Метод должен вернуть string - ID следующей ноды, куда идти графу
        /// </summary>
        /// <param name="player"></param>
        /// <returns></returns>
        string Execute(NovellaPlayer player);
    }
}

namespace NovellaEngine.Data
{
    [Serializable]
    public class DLCState
    {
        public string DLC_ID;
        public bool IsEnabled;
        public bool IsTrashed;
    }

    [CreateAssetMenu(fileName = "NovellaDLCSettings", menuName = "Novella Engine/DLC Settings")]
    public class NovellaDLCSettings : ScriptableObject
    {
        public List<DLCState> Modules = new List<DLCState>();

        private static NovellaDLCSettings _instance;
        public static NovellaDLCSettings Instance
        {
            get
            {
                if (_instance == null)
                {
#if UNITY_EDITOR
                    _instance = GetOrCreateSettings();
#else
                    _instance = Resources.Load<NovellaDLCSettings>("NovellaEngine/NovellaDLCSettings");
#endif
                }
                return _instance;
            }
        }

        public bool IsDLCEnabled(string dlcID)
        {
            var module = Modules.FirstOrDefault(m => m.DLC_ID == dlcID);
            return module == null || (module.IsEnabled && !module.IsTrashed);
        }

        public bool IsDLCTrashed(string dlcID)
        {
            var module = Modules.FirstOrDefault(m => m.DLC_ID == dlcID);
            return module != null && module.IsTrashed;
        }

#if UNITY_EDITOR
        public void SetDLCState(string dlcID, bool isEnabled)
        {
            var module = Modules.FirstOrDefault(m => m.DLC_ID == dlcID);
            if (module != null) module.IsEnabled = isEnabled;
            else Modules.Add(new DLCState { DLC_ID = dlcID, IsEnabled = isEnabled, IsTrashed = false });

            EditorUtility.SetDirty(this);
            AssetDatabase.SaveAssets();
        }

        public void SetDLCTrashed(string dlcID, bool isTrashed)
        {
            var module = Modules.FirstOrDefault(m => m.DLC_ID == dlcID);
            if (module != null)
            {
                module.IsTrashed = isTrashed;
                if (isTrashed) module.IsEnabled = false;
            }
            else Modules.Add(new DLCState { DLC_ID = dlcID, IsEnabled = false, IsTrashed = isTrashed });

            EditorUtility.SetDirty(this);
            AssetDatabase.SaveAssets();
        }

        public void RemoveDLCRecord(string dlcID)
        {
            Modules.RemoveAll(m => m.DLC_ID == dlcID);
            EditorUtility.SetDirty(this);
            AssetDatabase.SaveAssets();
        }

        public static NovellaDLCSettings GetOrCreateSettings()
        {
            var guids = AssetDatabase.FindAssets("t:NovellaDLCSettings");
            if (guids.Length > 0)
                return AssetDatabase.LoadAssetAtPath<NovellaDLCSettings>(AssetDatabase.GUIDToAssetPath(guids[0]));

            var settings = ScriptableObject.CreateInstance<NovellaDLCSettings>();

            string folderPath = "Assets/NovellaEngine/Resources/NovellaEngine";
            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
                AssetDatabase.Refresh();
            }

            AssetDatabase.CreateAsset(settings, folderPath + "/NovellaDLCSettings.asset");
            AssetDatabase.SaveAssets();
            return settings;
        }
#endif
    }
}