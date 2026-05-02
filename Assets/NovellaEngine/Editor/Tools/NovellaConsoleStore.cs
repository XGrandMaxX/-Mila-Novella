// ════════════════════════════════════════════════════════════════════════════
// Novella Console Store — статический сборщик логов для встроенной консоли
// Studio. Подписан на Application.logMessageReceivedThreaded, копит сообщения
// в кольцевом буфере. Модуль NovellaConsoleModule визуализирует данные.
// ════════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace NovellaEngine.Editor
{
    [InitializeOnLoad]
    public static class NovellaConsoleStore
    {
        public struct LogEntry
        {
            public DateTime Time;
            public LogType  Type;
            public string   Message;
            public string   StackTrace;
            // Сжатый одиночный ключ для свёртки повторяющихся сообщений
            // (Type|Message|StackTrace). Считается ленивой Property.
            public string DedupKey => Type + "|" + Message + "|" + StackTrace;
        }

        // Кольцевой буфер с capacity. Запись с конца, при переполнении сбрасываем
        // самые старые. Не используем Queue, потому что хочется индексный доступ.
        private static readonly List<LogEntry> _entries = new List<LogEntry>(1024);
        private const int CAPACITY = 2000;

        public static IReadOnlyList<LogEntry> Entries => _entries;
        public static event Action OnChanged;

        // Дешёвый счётчик без копирования. UI вызывает его каждый кадр чтобы
        // понять — менять ли свой кэш. Полная Snapshot() поднимается только
        // когда счётчик отличается от прошлого.
        public static int Count
        {
            get { lock (_lock) { return _entries.Count; } }
        }

        // Подписка делается один раз при загрузке домена. Используем
        // logMessageReceivedThreaded чтобы захватывать в т.ч. логи из не-главного
        // потока (NovellaPlayer, корутины и т.п.). Потокобезопасность через lock.
        private static readonly object _lock = new object();

        static NovellaConsoleStore()
        {
            // Снимаем старые подписки на всякий случай (после reload скриптов
            // делегаты могут «остаться» если неаккуратно отписались).
            Application.logMessageReceivedThreaded -= OnLog;
            Application.logMessageReceivedThreaded += OnLog;
        }

        private static void OnLog(string condition, string stackTrace, LogType type)
        {
            var entry = new LogEntry
            {
                Time       = DateTime.Now,
                Type       = type,
                Message    = condition ?? "",
                StackTrace = stackTrace ?? ""
            };
            lock (_lock)
            {
                _entries.Add(entry);
                if (_entries.Count > CAPACITY)
                {
                    // Усечение в начало — sliceCount = первые N записей.
                    int slice = _entries.Count - CAPACITY;
                    _entries.RemoveRange(0, slice);
                }
            }
            // OnChanged может быть вызван из не-главного потока, но подписчики
            // (Editor UI) обычно проверяют флаг и Repaint в OnInspectorUpdate.
            // Здесь просто пробрасываем событие.
            try { OnChanged?.Invoke(); } catch { /* swallow — подписчик не должен ронять логгер */ }
        }

        public static void Clear()
        {
            lock (_lock) { _entries.Clear(); }
            try { OnChanged?.Invoke(); } catch { }
        }

        // Снимок копией (потокобезопасно). UI использует для итерации без блокировки.
        public static List<LogEntry> Snapshot()
        {
            lock (_lock) { return new List<LogEntry>(_entries); }
        }

        public static (int log, int warn, int error) CountByType()
        {
            int l = 0, w = 0, e = 0;
            lock (_lock)
            {
                for (int i = 0; i < _entries.Count; i++)
                {
                    switch (_entries[i].Type)
                    {
                        case LogType.Log:       l++; break;
                        case LogType.Warning:   w++; break;
                        case LogType.Error:
                        case LogType.Exception:
                        case LogType.Assert:    e++; break;
                    }
                }
            }
            return (l, w, e);
        }
    }
}
