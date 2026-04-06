using System.Collections.Generic;
using UnityEngine;

namespace NovellaEngine.DLC.Wardrobe
{

    /// <summary>
    /// API для программиста. Работает так же, как NovellaVariables.
    /// Позволяет проверять, надевать и снимать одежду из любого скрипта.
    /// </summary>
    public static class NovellaWardrobeAPI
    {
        // Кэш для быстрого доступа
        private static Dictionary<string, string> _equippedItems = new Dictionary<string, string>();

        /// <summary>
        /// Надевает предмет на персонажа и сохраняет в PlayerPrefs.
        /// </summary>
        public static void EquipItem(string characterId, string layerName, string itemId)
        {
            string key = $"Wardrobe_{characterId}_{layerName}";
            _equippedItems[key] = itemId;

            PlayerPrefs.SetString(key, itemId);
            PlayerPrefs.Save();

            // В будущем тут можно вызывать ивент OnWardrobeChanged, 
            // чтобы сцена автоматически перерисовала куклу
        }

        /// <summary>
        /// Возвращает ID предмета, который сейчас надет на указанный слой персонажа.
        /// </summary>
        public static string GetEquippedItemID(string characterId, string layerName)
        {
            string key = $"Wardrobe_{characterId}_{layerName}";

            if (_equippedItems.TryGetValue(key, out string cachedId))
                return cachedId;

            string savedId = PlayerPrefs.GetString(key, "");
            _equippedItems[key] = savedId;
            return savedId;
        }

        /// <summary>
        /// Быстрая проверка для кода: Надет ли конкретный предмет?
        /// Пример: if (NovellaWardrobeAPI.IsItemEquipped("Hero", "Head", "Crown")) { ... }
        /// </summary>
        public static bool IsItemEquipped(string characterId, string layerName, string itemId)
        {
            return GetEquippedItemID(characterId, layerName) == itemId;
        }

        /// <summary>
        /// Снимает вещь со слоя (возвращает базовый спрайт из настроек персонажа)
        /// </summary>
        public static void UnequipItem(string characterId, string layerName)
        {
            EquipItem(characterId, layerName, "");
        }
    }
}