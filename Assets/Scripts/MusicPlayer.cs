using DG.Tweening;
using UnityEngine;
using UnityEngine.SceneManagement;

// Sahneler arası yaşayan tek müzik çalar: menüde bir parça, oyunda başka bir
// parça.
//
// Neden AudioManager'ın içinde değil: AudioManager sadece Game sahnesinde var
// ve sahneyle birlikte yok oluyor. SFX için doğru - ses efektleri zaten
// oyunun içinde. Müzik için değil: menüde de çalması, sahne geçişinde
// kesilmemesi gerekiyor. O yüzden ayrı ve kalıcı bir nesne.
//
// KURULUM YOK. Sahneye bir şey eklemene gerek yok - RuntimeInitializeOnLoadMethod
// ilk sahne açılmadan önce kendini kuruyor, klipleri Resources'tan okuyor.
// Yeni bir sahneye müzik eklemek için TrackFor() içine tek satır yeter.
public class MusicPlayer : MonoBehaviour
{
    public static MusicPlayer Instance { get; private set; }

    // Resources klasörüne göre göreli yollar - uzantı YAZILMAZ.
    // Dosyalar: Assets/Resources/Audio/MenuMusic.mp3, GameMusic.mp3
    const string MenuTrack = "Audio/MenuMusic";
    const string GameTrack = "Audio/GameMusic";

    // Eski AudioManager'daki müzik seviyesiyle aynı - efektlerin altında kalsın.
    const float Volume = 0.3f;

    // Sahne geçişinde parçalar birbirine karışsın. Sert kesme hem "tık"
    // sesi çıkarıyor hem de yükleme boşluğunu sessizlik olarak hissettiriyor.
    const float Crossfade = 0.8f;

    // Çapraz geçiş için iki kanal: biri sönerken diğeri açılıyor.
    AudioSource chanA;
    AudioSource chanB;
    AudioSource active;

    string currentTrack;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Bootstrap()
    {
        if (Instance != null) return;

        GameObject go = new GameObject("MusicPlayer");
        DontDestroyOnLoad(go);
        go.AddComponent<MusicPlayer>();
    }

    void Awake()
    {
        // Bootstrap tek örnek kuruyor ama biri sahneye elle de eklerse
        // iki müzik üst üste çalmasın.
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        chanA = CreateChannel();
        chanB = CreateChannel();
        active = chanA;

        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void Start()
    {
        // Emniyet kemeri: sceneLoaded ilk sahne için tetiklenmediyse (Editor'de
        // doğrudan Game sahnesine basıp oynatmak gibi) müzik hiç başlamazdı.
        if (currentTrack == null)
            Play(TrackFor(SceneManager.GetActiveScene().name));
    }

    void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;

        if (Instance == this) Instance = null;
    }

    AudioSource CreateChannel()
    {
        AudioSource src = gameObject.AddComponent<AudioSource>();

        src.playOnAwake = false;
        src.loop = true;
        src.volume = 0f;
        src.spatialBlend = 0f;   // 2D: dinleyicinin konumundan bağımsız
        src.mute = !AudioManager.MusicEnabled;

        return src;
    }

    // Hangi sahnede hangi parça çalıyor. Yeni sahne = yeni bir case.
    // Listede olmayan sahnede müzik susuyor.
    static string TrackFor(string sceneName)
    {
        switch (sceneName)
        {
            case "MainPages": return MenuTrack;
            case "Game":      return GameTrack;
            default:          return null;
        }
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // Additive sahneler (varsa popup/overlay) aktif parçayı değiştirmesin.
        if (mode == LoadSceneMode.Additive) return;

        Play(TrackFor(scene.name));
    }

    public void Play(string resourcePath)
    {
        if (string.IsNullOrEmpty(resourcePath))
        {
            Stop();
            return;
        }

        // Aynı parça zaten çalıyorsa dokunma. Retry sahneyi baştan yüklüyor
        // (GameManager.LoadScene) - müziğin de baştan başlaması gerekmiyor,
        // her denemede aynı 3 saniyeyi dinlemek yorucu olurdu.
        if (currentTrack == resourcePath && active != null && active.isPlaying) return;

        AudioClip clip = Resources.Load<AudioClip>(resourcePath);

        if (clip == null)
        {
            Debug.LogWarning($"[MusicPlayer] Klip bulunamadı: Resources/{resourcePath}");
            return;
        }

        currentTrack = resourcePath;

        AudioSource from = active;
        AudioSource to = active == chanA ? chanB : chanA;

        active = to;

        to.DOKill();
        to.clip = clip;
        to.volume = 0f;
        to.mute = !AudioManager.MusicEnabled;
        to.Play();

        // SetUpdate(true): oyun sonunda JuiceManager hit-stop'u timeScale'i
        // kısıyor, müzik geçişi ağır çekime düşmesin.
        to.DOFade(Volume, Crossfade).SetUpdate(true);

        FadeOutAndStop(from, Crossfade);
    }

    // Oyun sonu: sahneyi kazanma/kaybetme sesine bırak. Bir sonraki sahne
    // yüklendiğinde OnSceneLoaded müziği yeniden başlatıyor.
    public void Stop(float fade = 0.5f)
    {
        currentTrack = null;

        FadeOutAndStop(chanA, fade);
        FadeOutAndStop(chanB, fade);
    }

    // Sert kesmek yerine kısa bir sönme: dalganın ortasından kesilen ses
    // hoparlörde "tık" diye patlıyor.
    static void FadeOutAndStop(AudioSource src, float fade)
    {
        if (src == null || !src.isPlaying) return;

        src.DOKill();

        if (fade <= 0f)
        {
            src.Stop();
            return;
        }

        src.DOFade(0f, fade)
           .SetUpdate(true)
           .OnComplete(() =>
           {
               // Tween'in ortasında sahne değişmiş olabilir.
               if (src != null) src.Stop();
           });
    }

    // Ayarlar menüsündeki müzik butonu. Volume'u sıfırlamak yerine mute:
    // geri açınca ses seviyesi aynen dönüyor, fade tween'leriyle de çakışmıyor.
    public void ApplyMuteSetting()
    {
        bool mute = !AudioManager.MusicEnabled;

        if (chanA != null) chanA.mute = mute;
        if (chanB != null) chanB.mute = mute;
    }

    // AudioManager.MusicEnabled setter'ı buradan haber veriyor. Static:
    // ayar menüsü (SettingsUI) menü sahnesinde, orada AudioManager yok.
    public static void RefreshMuteSetting()
    {
        if (Instance != null) Instance.ApplyMuteSetting();
    }
}
