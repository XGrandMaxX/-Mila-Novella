using UnityEngine;

namespace NovellaEngine.Runtime
{
    /// <summary>
    /// Метка-«заглушка» для объектов созданных пресетом сцены. Любой потомок
    /// помеченного объекта тоже считается preset-managed.
    /// Используется только в эдиторе (Studio дерево UI Forge), чтобы:
    ///   • запретить случайное переключение active у структурно важных объектов
    ///     (например MCCreationPanel — он должен оставаться выключенным,
    ///     иначе в gameplay сразу всплывёт «гардероб»);
    ///   • визуально отделять пользовательский контент от шаблонного.
    /// В рантайме компонент ничего не делает, на сборку не влияет.
    /// </summary>
    [DisallowMultipleComponent]
    public class NovellaPresetMarker : MonoBehaviour
    {
        // Свободный текст-метка для Studio. Например «MainMenu», «Gameplay»,
        // «Wardrobe» — позволяет в подсказках уточнить откуда объект.
        public string PresetName;
    }
}
