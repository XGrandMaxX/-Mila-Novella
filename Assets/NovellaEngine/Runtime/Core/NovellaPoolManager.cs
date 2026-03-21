using System.Collections.Generic;
using UnityEngine;
using NovellaEngine.Data;

namespace NovellaEngine.Runtime
{
    public class NovellaPoolManager : MonoBehaviour
    {
        [Header("Containers")]
        private Transform _audioPoolsContainer;
        //TODL: В будущем здесь появятся:
        //private Transform _vfxPoolsContainer;
        //private Transform _uiPoolsContainer;

        [Header("Audio Pools")]
        private AudioSource _bgmSource;
        private List<AudioSource> _sfxPool = new List<AudioSource>();
        private List<AudioSource> _voicePool = new List<AudioSource>();

        public void InitializePools()
        {
            GameObject audioPoolsGO = new GameObject("[Audio Pools]");
            audioPoolsGO.transform.SetParent(this.transform);
            _audioPoolsContainer = audioPoolsGO.transform;

            GameObject bgmGO = new GameObject("BGM_Source");
            bgmGO.transform.SetParent(_audioPoolsContainer);
            _bgmSource = bgmGO.AddComponent<AudioSource>();
            _bgmSource.loop = true;
            _bgmSource.playOnAwake = false;

            for (int i = 0; i < 3; i++) CreateAudioSource("SFX", _sfxPool);
            for (int i = 0; i < 2; i++) CreateAudioSource("Voice", _voicePool);

            //TODO: Место для будущих пулов
            //InitVFXPools();
            //InitUIPools();
        }

        private AudioSource CreateAudioSource(string prefix, List<AudioSource> pool)
        {
            GameObject go = new GameObject($"{prefix}_Source_{pool.Count + 1}");
            go.transform.SetParent(_audioPoolsContainer);
            AudioSource src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            pool.Add(src);
            return src;
        }

        private AudioSource GetFreeAudioSource(List<AudioSource> pool, string prefix)
        {
            foreach (var src in pool)
            {
                if (!src.isPlaying) return src;
            }
            return CreateAudioSource(prefix, pool);
        }

        public void PlayAudio(AudioClip clip, float volume, EAudioChannel channel, bool loop = false)
        {
            if (clip == null) return;

            if (channel == EAudioChannel.BGM)
            {
                _bgmSource.clip = clip;
                _bgmSource.volume = volume;
                _bgmSource.loop = loop;
                _bgmSource.Play();
            }
            else if (channel == EAudioChannel.Voice)
            {
                AudioSource src = GetFreeAudioSource(_voicePool, "Voice");
                src.clip = clip; src.volume = volume; src.loop = loop; src.Play();
            }
            else
            {
                AudioSource src = GetFreeAudioSource(_sfxPool, "SFX");
                src.clip = clip; src.volume = volume; src.loop = loop; src.Play();
            }
        }

        public void StopAudio(EAudioChannel channel)
        {
            if (channel == EAudioChannel.BGM)
            {
                _bgmSource.Stop();
            }
            else if (channel == EAudioChannel.SFX)
            {
                foreach (var s in _sfxPool) s.Stop();
            }
            else if (channel == EAudioChannel.Voice)
            {
                foreach (var s in _voicePool) s.Stop();
            }
        }
    }
}