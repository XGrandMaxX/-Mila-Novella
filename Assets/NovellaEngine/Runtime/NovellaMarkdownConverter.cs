using System.Text.RegularExpressions;

namespace NovellaEngine.Runtime
{
    // Конвертер markdown ↔ TMP rich-text. Используется в редакторе диалогов
    // (вставка тегов через тулбар) и в NovellaPlayer (перед передачей текста
    // в TextMeshProUGUI). Цель — дать писателю короткий и понятный синтаксис
    // вместо «<b>...</b>», который перегружает текст и плохо читается.
    //
    // Поддерживаемый markdown:
    //   **жирно**         → <b>жирно</b>
    //   _курсив_          → <i>курсив</i>
    //   ~подчёркнутый~    → <u>подчёркнутый</u>
    //
    // ВАЖНО: цвет (<color=#HEX>) и размер (<size=N>) остаются в TMP-формате
    // как в asset, так и при отображении — для них нужны конкретные значения,
    // и их markdown-эквивалент был бы громоздким. Toolbar в редакторе для
    // них вставляет TMP-теги напрямую.
    //
    // Существующие реплики где автор уже писал TMP-теги (`<b>...</b>` и т.п.)
    // продолжают работать — конвертер их не трогает (TMP их и так понимает).
    public static class NovellaMarkdownConverter
    {
        // Bold: **text** → <b>text</b>. Регексп жадный по минимуму внутри
        // одной строки чтобы `**A** и **B**` дал два отдельных <b>, а не
        // один на весь span. Не пересекается с italic (там ОДНА `*`).
        private static readonly Regex BoldRx =
            new Regex(@"\*\*([^*\n]+?)\*\*", RegexOptions.Compiled);

        // Italic: _text_ → <i>text</i>. Через подчёркивание, чтобы НЕ
        // конфликтовать с bold (`**`) и underline (`~`). Markdown часто
        // даёт `*italic*` тоже — но мы упрощаем чтобы не угадывать
        // одиночную/двойную звёздочку.
        private static readonly Regex ItalicRx =
            new Regex(@"_([^_\n]+?)_", RegexOptions.Compiled);

        // Underline: ~text~ → <u>text</u>. Тильда выбрана потому что в
        // классическом markdown это strikethrough (~~text~~ обычно), но
        // у нас strikethrough не нужен, а underline в визуальной новелле
        // встречается чаще, поэтому занимаем простую короткую форму.
        private static readonly Regex UnderlineRx =
            new Regex(@"~([^~\n]+?)~", RegexOptions.Compiled);

        // Markdown → TMP. Безопасно для пустых/null строк. TMP-теги
        // в исходнике пропускаются как есть.
        public static string MarkdownToTmp(string md)
        {
            if (string.IsNullOrEmpty(md)) return md ?? "";
            string r = md;
            r = BoldRx.Replace(r, "<b>$1</b>");
            r = ItalicRx.Replace(r, "<i>$1</i>");
            r = UnderlineRx.Replace(r, "<u>$1</u>");
            return r;
        }

        // TMP → Markdown (опционально, для миграции). Используется если
        // нужно показать существующую TMP-разметку в редакторе как markdown.
        // Для color/size делает PASS-THROUGH (оставляет TMP-теги как есть).
        public static string TmpToMarkdown(string tmp)
        {
            if (string.IsNullOrEmpty(tmp)) return tmp ?? "";
            string r = tmp;
            r = Regex.Replace(r, @"<b>(.+?)</b>",  "**$1**");
            r = Regex.Replace(r, @"<i>(.+?)</i>",  "_$1_");
            r = Regex.Replace(r, @"<u>(.+?)</u>",  "~$1~");
            return r;
        }
    }
}
