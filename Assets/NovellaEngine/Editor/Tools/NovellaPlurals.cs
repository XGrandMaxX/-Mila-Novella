// ════════════════════════════════════════════════════════════════════════════
// NovellaPlurals — корректные формы множественного числа для строк интерфейса.
// Без него получалось «1 ошибок», «3 ошибки», «5 ошибки» — больно глазам.
// Поддерживает русские правила (1 / 2-4 / 5+) с поправкой на 11-14, и
// английские (1 / many).
// ════════════════════════════════════════════════════════════════════════════

using NovellaEngine.Data;

namespace NovellaEngine.Editor
{
    public static class NovellaPlurals
    {
        public static string Errors(int n) => ToolLang.IsRU
            ? n + " " + RuForm(n, "ошибка", "ошибки", "ошибок")
            : n + " error" + (n == 1 ? "" : "s");

        public static string Warnings(int n) => ToolLang.IsRU
            ? n + " " + RuForm(n, "предупреждение", "предупреждения", "предупреждений")
            : n + " warning" + (n == 1 ? "" : "s");

        public static string Messages(int n) => ToolLang.IsRU
            ? n + " " + RuForm(n, "сообщение", "сообщения", "сообщений")
            : n + " message" + (n == 1 ? "" : "s");

        public static string Entries(int n) => ToolLang.IsRU
            ? n + " " + RuForm(n, "запись", "записи", "записей")
            : n + " entr" + (n == 1 ? "y" : "ies");

        // Русское правило: 1 — one, 2/3/4 — few, иначе many.
        // Поправка на 11/12/13/14 — это many, а не one/few (например 11 ошибок,
        // не «11 ошибка»).
        private static string RuForm(int n, string one, string few, string many)
        {
            int mod10  = n % 10;
            int mod100 = n % 100;
            if (mod100 >= 11 && mod100 <= 14) return many;
            if (mod10 == 1) return one;
            if (mod10 >= 2 && mod10 <= 4) return few;
            return many;
        }
    }
}
