using UnityEngine;
using UnityEngine.UI;

// Ayarlar panelindeki üç buton: ses efektleri, müzik, titreşim.
//
// Toggle değil buton: oyun içindeki menüyle (GameMenuUI) aynı desen - üstünde
// yazı yok, açık/kapalı durumu sprite'tan okunuyor. Sprite verilmemişse buton
// kapalıyken soluyor, yani sprite'lar hazır olmadan da anlaşılır kalıyor.
//
// Ayarların kendisi burada TUTULMUYOR - her biri kendi sistemine ait ve
// PlayerPrefs'te yaşıyor (AudioManager.SfxEnabled, HapticManager.HapticsEnabled).
// Bu script sadece o değerleri ekrana yansıtıp geri yazıyor. Böylece ayar
// paneli hangi sahnede olursa olsun çalışıyor; AudioManager sadece Game
// sahnesinde var ama static property her yerden okunabiliyor.
//
// KURULUM: ayarlar paneline ekle, üç butonu bağla.
public class SettingsUI : MonoBehaviour
{
    [Header("Butonlar")]
    [SerializeField] Button sfxButton;
    [SerializeField] Button musicButton;
    [SerializeField] Button hapticsButton;

    [Header("Durum Sprite'ları (opsiyonel)")]
    [SerializeField] Sprite sfxOnSprite;
    [SerializeField] Sprite sfxOffSprite;
    [SerializeField] Sprite musicOnSprite;
    [SerializeField] Sprite musicOffSprite;

    [Header("Geri Bildirim")]
    [Tooltip("Titreşim açılınca örnek bir titreşim ver - oyuncu ayarın " +
             "çalıştığını hissetsin")]
    [SerializeField] bool previewHapticOnEnable = true;

    void Awake()
    {
        // Awake, obje ilk kez aktif olduğunda bir kez çalışıyor ve OnEnable'dan
        // hemen önce geliyor. Panel sahnede kapalı başlasa da sıra bozulmuyor.
        if (sfxButton != null) sfxButton.onClick.AddListener(ToggleSfx);
        if (musicButton != null) musicButton.onClick.AddListener(ToggleMusic);
        if (hapticsButton != null) hapticsButton.onClick.AddListener(ToggleHaptics);
    }

    void OnEnable()
    {
        // Panel her açıldığında güncel değerleri göster - ayar başka bir
        // yerden (oyun içi menü) değişmiş olabilir.
        Refresh();
    }

    // ==================== DEĞİŞİKLİKLER ====================

    void ToggleSfx()
    {
        AudioManager.SfxEnabled = !AudioManager.SfxEnabled;

        Refresh();
    }

    void ToggleMusic()
    {
        AudioManager.MusicEnabled = !AudioManager.MusicEnabled;

        Refresh();
    }

    void ToggleHaptics()
    {
        bool value = !HapticManager.HapticsEnabled;

        HapticManager.HapticsEnabled = value;

        // Açarken bir kez titret: ayarın çalıştığı ancak hissedilerek anlaşılır.
        if (value && previewHapticOnEnable && HapticManager.Instance != null)
            HapticManager.Instance.Play(HapticStrength.Light);

        Refresh();
    }

    // ==================== GÖRÜNÜM ====================

    void Refresh()
    {
        ApplyState(sfxButton, AudioManager.SfxEnabled, sfxOnSprite, sfxOffSprite);
        ApplyState(musicButton, AudioManager.MusicEnabled, musicOnSprite, musicOffSprite);
        // Titreşimin açık/kapalı ikonu yok: kapalıyken soluyor
        ApplyState(hapticsButton, HapticManager.HapticsEnabled, null, null);
    }

    static void ApplyState(Button button, bool on, Sprite onSprite, Sprite offSprite)
    {
        if (button == null || button.image == null) return;

        Sprite target = on ? onSprite : offSprite;

        if (target != null) button.image.sprite = target;

        // Sprite verilmediyse kapalı durum hiç belli olmazdı - soldurarak
        // gösteriyoruz. Sprite varsa renge dokunmuyoruz.
        Color color = button.image.color;
        color.a = target != null || on ? 1f : 0.45f;
        button.image.color = color;
    }
}
