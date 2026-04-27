using UnityEditor;
using UnityEngine;
using UnityEngine.Video;

namespace NovellaEngine.Editor.Tutorials
{
    /// <summary>
    /// Отрисовка визуальных подсказок туториала — 5 стилей.
    /// Все методы рисуют в текущий GUI-контекст (вызываются из IMGUIContainer окна).
    /// </summary>
    internal static class NovellaTutorialOverlay
    {
        // Видео-инфраструктура (один общий плеер на все шаги)
        private static GameObject _videoHost;
        private static VideoPlayer _videoPlayer;
        private static RenderTexture _videoRT;
        private static VideoClip _activeClip;

        // ─────────────── ОСНОВНОЙ DRAW ───────────────

        public static void Draw(EditorWindow window, NovellaTutorialStep step, Rect target, float introAnim, double stepStartTime)
        {
            float winW = window.position.width;
            float winH = window.position.height;

            // 1) Затемнение/обводка/палец/стрелка/тултип
            switch (step.HintStyle)
            {
                case ETutorialHintStyle.Spotlight:
                    DrawSpotlight(target, winW, winH, step.AccentColor, introAnim);
                    break;
                case ETutorialHintStyle.Outline:
                    DrawOutline(target, step.AccentColor, introAnim);
                    break;
                case ETutorialHintStyle.PointingFinger:
                    DrawSpotlight(target, winW, winH, step.AccentColor, introAnim * 0.6f);
                    DrawPointingFinger(target, introAnim);
                    break;
                case ETutorialHintStyle.Arrow:
                    DrawSpotlight(target, winW, winH, step.AccentColor, introAnim * 0.6f);
                    // стрелка рисуется в DrawTextPanel — там она знает положение панели
                    break;
                case ETutorialHintStyle.Tooltip:
                    DrawOutline(target, step.AccentColor, introAnim * 0.7f);
                    break;
            }
        }

        // ─────────────── SPOTLIGHT ───────────────

        private static void DrawSpotlight(Rect target, float w, float h, Color accent, float alpha)
        {
            if (target.width < 1 || target.height < 1)
            {
                EditorGUI.DrawRect(new Rect(0, 0, w, h), new Color(0, 0, 0, 0.78f * alpha));
                return;
            }

            Color dim = new Color(0, 0, 0, 0.78f * alpha);
            EditorGUI.DrawRect(new Rect(0, 0, w, target.y), dim);
            EditorGUI.DrawRect(new Rect(0, target.yMax, w, h - target.yMax), dim);
            EditorGUI.DrawRect(new Rect(0, target.y, target.x, target.height), dim);
            EditorGUI.DrawRect(new Rect(target.xMax, target.y, w - target.xMax, target.height), dim);

            DrawOutline(target, accent, alpha);
        }

        // ─────────────── OUTLINE (пульсация) ───────────────

        private static void DrawOutline(Rect target, Color accent, float alpha)
        {
            if (target.width < 1 || target.height < 1) return;

            float blink = (Mathf.Sin((float)EditorApplication.timeSinceStartup * 4.5f) + 1f) / 2f;
            Color outline = accent;
            outline.a = (0.55f + blink * 0.45f) * alpha;

            // Внешний glow
            Handles.color = outline;
            Handles.DrawSolidRectangleWithOutline(target, Color.clear, outline);

            // Двойная обводка для эффекта свечения
            Color glow = outline;
            glow.a *= 0.45f;
            Rect inflated = new Rect(target.x - 3, target.y - 3, target.width + 6, target.height + 6);
            Handles.color = glow;
            Handles.DrawSolidRectangleWithOutline(inflated, Color.clear, glow);
            Handles.color = Color.white;
        }

        // ─────────────── POINTING FINGER ───────────────

        private static void DrawPointingFinger(Rect target, float alpha)
        {
            if (alpha <= 0) return;
            float t = (float)(EditorApplication.timeSinceStartup % 1.6);
            float bounce = Mathf.Sin(t / 1.6f * Mathf.PI * 2f) * 8f;

            // Палец чуть ниже-правее верхне-левого угла цели, постоянно качается
            Vector2 pos = new Vector2(target.x - 36, target.y + target.height * 0.5f - 24 + bounce);

            Color old = GUI.color;
            GUI.color = new Color(1, 1, 1, alpha);

            var style = new GUIStyle { fontSize = 46 };
            GUI.Label(new Rect(pos.x, pos.y, 60, 60), "👆", style);

            GUI.color = old;
        }

        // ─────────────── ARROW (от панели к цели) ───────────────

        public static void DrawArrowFromPanelToTarget(Rect panel, Rect target, Color accent, float alpha)
        {
            if (target.width < 1 || target.height < 1) return;

            Vector2 panelEdge = ClosestPointOnRect(panel, target.center);
            Vector2 targetEdge = ClosestPointOnRect(target, panel.center);

            Color c = accent;
            c.a *= alpha;

            Vector3 dir = (targetEdge - panelEdge).normalized;
            Vector3 mid = (panelEdge + targetEdge) * 0.5f;
            Vector3 perp = new Vector3(-dir.y, dir.x, 0);
            Vector3 startTangent = (Vector2)panelEdge + (Vector2)(dir * 30f) + (Vector2)(perp * 15f);
            Vector3 endTangent = (Vector2)targetEdge - (Vector2)(dir * 30f) + (Vector2)(perp * 15f);

            Handles.DrawBezier(panelEdge, targetEdge, startTangent, endTangent, new Color(0, 0, 0, 0.6f * alpha), null, 7f);
            Handles.DrawBezier(panelEdge, targetEdge, startTangent, endTangent, c, null, 4f);

            // Наконечник
            Vector2 right = new Vector2(-dir.y, dir.x);
            Vector2 a = targetEdge - (Vector2)(dir * 14f) + right * 8f;
            Vector2 b = targetEdge - (Vector2)(dir * 14f) - right * 8f;
            Handles.color = c;
            Handles.DrawAAConvexPolygon((Vector3)targetEdge, (Vector3)a, (Vector3)b);
            Handles.color = Color.white;
        }

        private static Vector2 ClosestPointOnRect(Rect r, Vector2 toward)
        {
            float x = Mathf.Clamp(toward.x, r.x, r.xMax);
            float y = Mathf.Clamp(toward.y, r.y, r.yMax);

            if (x > r.x && x < r.xMax && y > r.y && y < r.yMax)
            {
                float dl = x - r.x, dr = r.xMax - x, dt = y - r.y, db = r.yMax - y;
                float min = Mathf.Min(Mathf.Min(dl, dr), Mathf.Min(dt, db));
                if (min == dl) x = r.x;
                else if (min == dr) x = r.xMax;
                else if (min == dt) y = r.y;
                else y = r.yMax;
            }
            return new Vector2(x, y);
        }

        // ─────────────── ВИДЕО / GIF ───────────────

        public static void DrawVideoOrImage(Rect rect, NovellaTutorialStep step)
        {
            if (step.Video != null)
            {
                EnsureVideo(step.Video);
                if (_videoRT != null)
                {
                    GUI.DrawTexture(rect, _videoRT, ScaleMode.ScaleToFit);
                }
            }
            else if (step.Image != null)
            {
                if (step.ImageFrameCount > 1)
                {
                    int frame = (int)((EditorApplication.timeSinceStartup * step.ImageFPS) % step.ImageFrameCount);
                    float fw = 1f / step.ImageFrameCount;
                    Rect uv = new Rect(frame * fw, 0, fw, 1);
                    GUI.DrawTextureWithTexCoords(rect, step.Image, uv);
                }
                else
                {
                    GUI.DrawTexture(rect, step.Image, ScaleMode.ScaleToFit);
                }
            }
        }

        private static void EnsureVideo(VideoClip clip)
        {
            if (_videoPlayer != null && _activeClip == clip) return;
            DisposeVideo();

            _videoHost = EditorUtility.CreateGameObjectWithHideFlags("__NovellaTutorialVideo", HideFlags.HideAndDontSave);
            _videoPlayer = _videoHost.AddComponent<VideoPlayer>();
            _videoPlayer.playOnAwake = false;
            _videoPlayer.isLooping = true;
            _videoPlayer.audioOutputMode = VideoAudioOutputMode.None;
            _videoPlayer.renderMode = VideoRenderMode.RenderTexture;
            _videoPlayer.clip = clip;

            int rtW = (int)clip.width, rtH = (int)clip.height;
            if (rtW <= 0) rtW = 640; if (rtH <= 0) rtH = 360;
            _videoRT = new RenderTexture(rtW, rtH, 0);
            _videoRT.hideFlags = HideFlags.HideAndDontSave;
            _videoPlayer.targetTexture = _videoRT;

            _videoPlayer.Prepare();
            _videoPlayer.prepareCompleted += vp => vp.Play();
            _videoPlayer.Play();

            _activeClip = clip;
        }

        public static void DisposeVideo()
        {
            if (_videoPlayer != null) { _videoPlayer.Stop(); _videoPlayer = null; }
            if (_videoHost != null) { Object.DestroyImmediate(_videoHost); _videoHost = null; }
            if (_videoRT != null) { _videoRT.Release(); Object.DestroyImmediate(_videoRT); _videoRT = null; }
            _activeClip = null;
        }
    }
}
