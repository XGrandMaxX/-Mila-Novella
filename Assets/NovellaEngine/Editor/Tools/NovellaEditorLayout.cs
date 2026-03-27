using UnityEditor;
using UnityEngine;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NovellaEngine.Editor
{
    public class NovellaTabState
    {
        public Dictionary<string, float> AnimProgress = new Dictionary<string, float>();
        private double _lastTime;

        public void Update()
        {
            double currentTime = EditorApplication.timeSinceStartup;
            float dt = (float)(currentTime - _lastTime);
            _lastTime = currentTime;

            bool needsRepaint = false;
            var keys = AnimProgress.Keys.ToList();

            foreach (var key in keys)
            {
                float target = key == _activeKey ? 1f : 0f;
                if (Mathf.Abs(AnimProgress[key] - target) > 0.005f)
                {
                    AnimProgress[key] = Mathf.Lerp(AnimProgress[key], target, dt * 15f);
                    needsRepaint = true;
                }
                else AnimProgress[key] = target;
            }

            if (needsRepaint) _onRepaint?.Invoke();
        }

        private string _activeKey;
        private Action _onRepaint;

        public void Initialize(Action repaintAction)
        {
            _onRepaint = repaintAction;
            _lastTime = EditorApplication.timeSinceStartup;
        }

        public void SetActive(string key) => _activeKey = key;

        public float GetProgress(string key)
        {
            if (!AnimProgress.ContainsKey(key)) AnimProgress[key] = 0f;
            return AnimProgress[key];
        }
    }

    public static class NovellaEditorLayout
    {
        public static bool DrawAnimatedTab(string key, string icon, string label, NovellaTabState state, Color activeColor, float normalWidth = 175f, float expandedWidth = 215f)
        {
            float progress = state.GetProgress(key);

            float currentWidth = Mathf.Lerp(normalWidth, expandedWidth, progress);
            Color inactiveColor = new Color(0.2f, 0.2f, 0.2f, 1f);

            GUI.backgroundColor = Color.Lerp(inactiveColor, activeColor, progress);

            GUIStyle tabStyle = new GUIStyle(GUI.skin.button)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize = 13,
                fontStyle = progress > 0.5f ? FontStyle.Bold : FontStyle.Normal
            };
            tabStyle.padding.left = (int)Mathf.Lerp(12f, 22f, progress);
            tabStyle.normal.textColor = Color.Lerp(new Color(0.8f, 0.8f, 0.8f), Color.white, progress);

            bool clicked = GUILayout.Button($"{icon} {label}", tabStyle, GUILayout.Width(currentWidth), GUILayout.Height(35));

            GUI.backgroundColor = Color.white;
            return clicked;
        }
    }
}