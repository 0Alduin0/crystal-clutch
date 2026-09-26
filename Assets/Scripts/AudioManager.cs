using System.Collections.Generic;
using UnityEngine;

public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance;

    [SerializeField] AudioSource sfxSource;

    [Header("Sesler")]
    [SerializeField] AudioClip popClip;
    [SerializeField] AudioClip powerUpClip;

    [Tooltip("Engel kırılma sesi. Boş bırakırsan kristal pop sesi kullanılır.")]
    [SerializeField] AudioClip obstacleBreakClip;

    [SerializeField] AudioClip winClip;
    [SerializeField] AudioClip loseClip;

    [Header("Müzik")]
    [Tooltip("Kazanma/kaybetme sesi çalınca arka plan müziğinin sönme süresi. " +
             "0 = anında kesilir.")]
    [SerializeField] float musicFadeOut = 0.5f;

    [Header("SFX Kanalları")]
    [Tooltip("Kaç ses aynı anda üst üste binebilsin. Tek kaynak yetmiyor: " +
             "pitch AudioSource'un kendisine ait, yani yeni bir ses çalarken " +
             "hâlâ duyulan sesin tonu da kayıyor.")]
    [Range(1, 8)]
    [SerializeField] int sfxVoices = 4;

    AudioSource[] voices;
    int nextVoice;

    // ==================== KULLANICI AYARLARI ====================
    // HapticManager.HapticsEnabled ile aynı desen: static property + PlayerPrefs
    // + bellekte önbellek. Ayarlar menüsü hangi sahnedeyse oradan okunup
    // yazılabilsin diye static - AudioManager sadece Game sahnesinde var.

    const string SFX_KEY = "SfxEnabled";
    const string MUSIC_KEY = "MusicEnabled";

    static int sfxCache = -1;     // -1 = henüz okunmadı
    static int musicCache = -1;

    public static bool SfxEnabled
    {
        get
        {
            if (sfxCache < 0) sfxCache = PlayerPrefs.GetInt(SFX_KEY, 1);
            return sfxCache == 1;
        }
        set
        {
            sfxCache = value ? 1 : 0;
            PlayerPrefs.SetInt(SFX_KEY, sfxCache);
            PlayerPrefs.Save();

            if (Instance != null) Instance.ApplyAudioSettings();
        }
    }

    public static bool MusicEnabled
    {
        get
        {
            if (musicCache < 0) musicCache = PlayerPrefs.GetInt(MUSIC_KEY, 1);
            return musicCache == 1;
        }
        set
        {
            musicCache = value ? 1 : 0;
            PlayerPrefs.SetInt(MUSIC_KEY, musicCache);
            PlayerPrefs.Save();

            // Müzik AudioManager'da değil MusicPlayer'da: AudioManager sadece
            // Game sahnesinde var, ayar menüsü ise menü sahnesinde.
            MusicPlayer.RefreshMuteSetting();
        }
    }

    // Kaynakları sustur/aç. Ses seviyesini sıfırlamak yerine mute kullanıyoruz:
    // oyuncunun ayarladığı volume değerleri korunuyor, geri açınca aynen dönüyor.
    void ApplyAudioSettings()
    {
        if (voices == null) return;

        foreach (AudioSource v in voices)
            if (v != null) v.mute = !SfxEnabled;
    }

    // Aynı klip aynı karede bir kez çalsın. PlayOneShot sesleri ÜST ÜSTE
    // BİNDİRİYOR - tek patlamada 5 engel kırılınca aynı ses 5 kopya hâlinde
    // toplanıp ses seviyesini tavana vuruyordu.
    readonly Dictionary<AudioClip, int> lastPlayedFrame = new Dictionary<AudioClip, int>();

    void Awake()
    {
        Instance = this;

        // Inspector'da boş bırakılırsa kendi kaynağımızı kuruyoruz. SFX kendi
        // AudioSource'unda çalmalı: PlayPop pitch'i kaynağın kendisine yazıyor,
        // paylaşılan bir kaynakta çalan her şeyin tonunu kaydırırdı.
        if (sfxSource == null)
        {
            sfxSource = gameObject.AddComponent<AudioSource>();
            sfxSource.playOnAwake = false;
            sfxSource.loop = false;
        }

        BuildVoicePool();
        ApplyAudioSettings();
    }

    // Inspector'daki kaynağı ilk kanal yapıp yanına aynı ayarlarla kardeşler
    // ekler. Ayarları elle kopyalıyoruz: kullanıcının mixer grubu, ses seviyesi
    // gibi tercihleri yeni kanallarda da geçerli olmalı.
    void BuildVoicePool()
    {
        int count = Mathf.Max(1, sfxVoices);

        voices = new AudioSource[count];
        voices[0] = sfxSource;

        for (int i = 1; i < count; i++)
        {
            AudioSource v = sfxSource.gameObject.AddComponent<AudioSource>();

            v.clip = null;
            v.playOnAwake = false;
            v.loop = false;
            v.outputAudioMixerGroup = sfxSource.outputAudioMixerGroup;
            v.volume = sfxSource.volume;
            v.spatialBlend = sfxSource.spatialBlend;
            v.bypassEffects = sfxSource.bypassEffects;
            v.bypassListenerEffects = sfxSource.bypassListenerEffects;
            v.priority = sfxSource.priority;

            voices[i] = v;
        }
    }

    // Boşta olan ilk kanalı verir. Çalan bir kanalı seçmek onun pitch'ini
    // değiştirmek demek - duyulan ses ortasından tonu kayıyor.
    AudioSource TakeVoice()
    {
        for (int i = 0; i < voices.Length; i++)
        {
            AudioSource v = voices[(nextVoice + i) % voices.Length];

            if (v != null && !v.isPlaying)
            {
                nextVoice = (nextVoice + i + 1) % voices.Length;
                return v;
            }
        }

        // Hepsi meşgulse sırayla devam et
        AudioSource fallback = voices[nextVoice];
        nextVoice = (nextVoice + 1) % voices.Length;

        return fallback;
    }

    void Play(AudioClip clip, float pitch = 1f)
    {
        if (clip == null || voices == null) return;

        if (lastPlayedFrame.TryGetValue(clip, out int frame) && frame == Time.frameCount) return;
        lastPlayedFrame[clip] = Time.frameCount;

        AudioSource src = TakeVoice();
        if (src == null) return;

        src.pitch = pitch * Random.Range(0.96f, 1.04f);
        src.PlayOneShot(clip);
    }

    // Cascade derinleştikçe ses tizleşsin - zincirleme hissi verir
    public void PlayPop(int cascadeLevel = 1)
    {
        float pitch = 1f + (cascadeLevel - 1) * 0.12f;
        pitch = Mathf.Clamp(pitch, 1f, 2f);
        Play(popClip, pitch);
    }

    // Engel kırılınca. Ayrı klip atanmadıysa kristal pop sesine düşer; o durumda
    // aynı karedeki kristal patlaması sesiyle birleşir, iki kez çalmaz.
    public void PlayObstacleBreak()
    {
        Play(obstacleBreakClip != null ? obstacleBreakClip : popClip);
    }

    public void PlayPowerUp() => Play(powerUpClip);

    // Oyun sonu sesleri: müzik susup sahneyi bunlara bırakıyor.
    public void PlayWin()
    {
        StopMusic();
        Play(winClip);
    }

    public void PlayLose()
    {
        StopMusic();
        Play(loseClip);
    }

    // Arka plan müziğini kapat. Müziğin kendisi MusicPlayer'da yaşıyor;
    // burası sadece oyun sonu çağrılarının (PlayWin/PlayLose) girişi olarak
    // duruyor - çağrı yerlerini değiştirmeye gerek kalmasın.
    public void StopMusic()
    {
        if (MusicPlayer.Instance != null) MusicPlayer.Instance.Stop(musicFadeOut);
    }
}
