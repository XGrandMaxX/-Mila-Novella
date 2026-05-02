// ════════════════════════════════════════════════════════════════════════════
// NovellaSocialIconsImporter — автоматически настраивает TextureImporter для
// PNG-иконок соцсетей. Юзеру достаточно просто перетащить файл в папку
// Assets/NovellaEngine/Editor/Resources/SocialIcons — настройки импорта
// (альфа, без сжатия, без MIP) проставятся сами.
// ════════════════════════════════════════════════════════════════════════════

using UnityEditor;

namespace NovellaEngine.Editor
{
    public class NovellaSocialIconsImporter : AssetPostprocessor
    {
        private const string ICON_DIR = "Assets/NovellaEngine/Editor/Resources/SocialIcons/";

        // OnPreprocessTexture вызывается ДО импорта текстуры — самый ранний
        // момент когда можно поправить TextureImporter настройки.
        private void OnPreprocessTexture()
        {
            if (string.IsNullOrEmpty(assetPath)) return;
            if (!assetPath.StartsWith(ICON_DIR)) return;

            var importer = (TextureImporter)assetImporter;

            // Нужна прозрачная альфа, без сжатия — иконки маленькие, качество важнее.
            importer.textureType        = TextureImporterType.Default;
            importer.alphaSource        = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled      = false;
            importer.npotScale          = TextureImporterNPOTScale.None;
            importer.wrapMode           = UnityEngine.TextureWrapMode.Clamp;
            importer.filterMode         = UnityEngine.FilterMode.Bilinear;
            importer.maxTextureSize     = 256;

            // Несжатый RGBA32 для всех платформ — на 256×256 это копейки памяти,
            // а качество важно, иконку видно вблизи в окне.
            var defaults = importer.GetDefaultPlatformTextureSettings();
            defaults.format             = TextureImporterFormat.RGBA32;
            defaults.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SetPlatformTextureSettings(defaults);
        }

        // OnPostprocessAllAssets — после того как .png сохранён, дропаем
        // кэш иконок, чтобы окно жалобы взяло свежую версию без перезапуска.
        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFromAssetPaths)
        {
            bool changed = false;
            foreach (var p in imported) if (p != null && p.StartsWith(ICON_DIR)) { changed = true; break; }
            if (!changed)
            {
                foreach (var p in deleted) if (p != null && p.StartsWith(ICON_DIR)) { changed = true; break; }
            }
            if (changed) NovellaSocialIcons.Reload();
        }
    }
}
