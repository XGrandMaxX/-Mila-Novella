using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using NovellaEngine.Data;

namespace NovellaEngine.Runtime.UI
{
    public class NovellaMCCreationUI : MonoBehaviour
    {
        [Header("Characters List")]
        public List<NovellaCharacter> AvailableCharacters = new List<NovellaCharacter>();

        [Header("UI Containers")]
        public Transform AvatarMaskContainer;

        [Header("Character Switch Buttons")]
        public Button PrevCharBtn;
        public Button NextCharBtn;

        private int _currentCharIndex = 0;
        private GameObject _activeAvatar;
        private Image _avatarImage;

        void Start()
        {
            if (AvailableCharacters == null || AvailableCharacters.Count == 0) return;

            if (PrevCharBtn != null) PrevCharBtn.onClick.AddListener(() => ChangeCharacter(-1));
            if (NextCharBtn != null) NextCharBtn.onClick.AddListener(() => ChangeCharacter(1));

            BuildCurrentCharacter();
        }

        private void ChangeCharacter(int dir)
        {
            _currentCharIndex += dir;
            if (_currentCharIndex < 0) _currentCharIndex = AvailableCharacters.Count - 1;
            if (_currentCharIndex >= AvailableCharacters.Count) _currentCharIndex = 0;

            BuildCurrentCharacter();
        }

        private void BuildCurrentCharacter()
        {
            if (AvailableCharacters.Count == 0) return;

            NovellaCharacter currentChar = AvailableCharacters[_currentCharIndex];
            if (currentChar == null) return;

            if (_activeAvatar == null && AvatarMaskContainer != null)
            {
                _activeAvatar = new GameObject("AvatarImage");
                _activeAvatar.transform.SetParent(AvatarMaskContainer, false);
                var rt = _activeAvatar.AddComponent<RectTransform>();
                rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
                _avatarImage = _activeAvatar.AddComponent<Image>();
                _avatarImage.preserveAspect = true;
            }

            if (_avatarImage != null) _avatarImage.sprite = currentChar.DefaultSprite;

            // ВАЖНО: Сохраняем имя выбранного ассета, чтобы Гардероб нашел его в Resources/Characters/
            PlayerPrefs.SetString("MC_CharacterID", currentChar.name);
            PlayerPrefs.Save();
        }
    }
}