using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Oyun sahnesinin alt barındaki en sağdaki menü butonu ve onun açtığı üç
// buton: ses+titreşim, müzik, ana menü. Butonlar sağdan sola kayarak,
// birbirinin ardına geliyor.
//
// Butonlarda yazı yok, durum sprite'tan okunuyor. Sprite verilmemişse buton
// kapalıyken soluyor - sprite'lar hazır olmadan da hangi ayarın kapalı
// olduğu anlaşılıyor.
//
// Ana menü butonu doğrudan çıkarmıyor: level yarıda bırakmak bir cana mal
// olduğu için önce onay paneli geliyor.
public class GameMenuUI : MonoBehaviour
{
    [Header("Menü")]
    [SerializeField] Button menuButton;

    [Tooltip("Üç butonun kabı - menü kapalıyken komple kapanıyor")]
    [SerializeField] RectTransform flyout;

    [Header("Açılan Butonlar (alttan üste)")]
    [Tooltip("Ses efektleri ve titreşimi BİRLİKTE açıp kapatıyor")]
    [SerializeField] Button sfxButton;
    [SerializeField] Button musicButton;

    [Tooltip("Ana menüye dön - önce onay paneli açılıyor")]
    [SerializeField] Button exitButton;

    [Header("Durum Sprite'ları (opsiyonel)")]
    [SerializeField] Sprite sfxOnSprite;
    [SerializeField] Sprite sfxOffSprite;
    [SerializeField] Sprite musicOnSprite;
    [SerializeField] Sprite musicOffSprite;

    [Header("Açılma Animasyonu")]
    [Tooltip("Butonlar kaç birim sağdan gelsin")]
    [SerializeField] float slideDistance = 320f;
    [SerializeField] float slideDuration = 0.28f;

    [Tooltip("Butonlar arası gecikme - arka arkaya gelsinler")]
    [SerializeField] float stagger = 0.05f;

    [Header("Çıkış Onayı")]
    [Tooltip("Bağlanmazsa onay atlanır ve doğrudan menüye dönülür")]
    [SerializeField] GameObject exitPanel;

    [Tooltip("Panelin ortasındaki kutu. Zıplama animasyonu buna uygulanıyor - " +
             "panelin kendisini ölçeklersek arkadaki karartma da ölçeklenir.")]
    [SerializeField] RectTransform exitBox;

    [SerializeField] TextMeshProUGUI exitInfoText;

    [Tooltip("Çıkışı onaylayan buton - can gider, ana menüye dönülür")]
    [SerializeField] Button exitConfirmButton;

    [Tooltip("Panelin sağ üstündeki çarpı - oyuna geri döner")]
    [SerializeField] Button exitCloseButton;

    // Butonların Inspector'da verilmiş yerleri. Animasyon bunların üzerine
    // çalışıyor, sahnedeki düzen koda gömülmüyor.
    RectTransform[] items;
    Vector2[] homePositions;

    bool open;

    void Start()
    {
        CacheItems();

        if (flyout != null) flyout.gameObject.SetActive(false);
        if (exitPanel != null) exitPanel.SetActive(false);

        if (menuButton != null)
            menuButton.onClick.AddListener(() => SetMenuOpen(!open));

        if (sfxButton != null) sfxButton.onClick.AddListener(ToggleSfx);
        if (musicButton != null) musicButton.onClick.AddListener(ToggleMusic);
        if (exitButton != null) exitButton.onClick.AddListener(OpenExitConfirm);

        if (exitConfirmButton != null) exitConfirmButton.onClick.AddListener(QuitNow);
        if (exitCloseButton != null) exitCloseButton.onClick.AddListener(CloseExitConfirm);

        RefreshToggleVisuals();
    }

    void CacheItems()
    {
        // Sıra önemli: dizideki ilk buton ilk geliyor. Alttan üste dizmek,
        // menü butonundan yukarı doğru açılıyormuş hissini veriyor.
        Button[] buttons = { sfxButton, musicButton, exitButton };

        items = new RectTransform[buttons.Length];
        homePositions = new Vector2[buttons.Length];

        for (int i = 0; i < buttons.Length; i++)
        {
            if (buttons[i] == null) continue;

            items[i] = (RectTransform)buttons[i].transform;
            homePositions[i] = items[i].anchoredPosition;
        }
    }

    // ==================== MENÜ ====================

    void SetMenuOpen(bool value)
    {
        if (flyout == null || items == null) return;

        open = value;

        if (value) flyout.gameObject.SetActive(true);

        Tween last = null;

        for (int i = 0; i < items.Length; i++)
        {
            RectTransform item = items[i];
            if (item == null) continue;

            item.DOKill();

            Vector2 hidden = homePositions[i] + Vector2.right * slideDistance;

            if (value)
            {
                item.anchoredPosition = hidden;

                last = item.DOAnchorPos(homePositions[i], slideDuration)
                           .SetDelay(i * stagger)
                           .SetEase(Ease.OutBack);
            }
            else
            {
                last = item.DOAnchorPos(hidden, slideDuration * 0.7f).SetEase(Ease.InBack);
            }
        }

        // Kapanışta kabı ancak son buton ekrandan çıkınca gizliyoruz, yoksa
        // butonlar yerinde donup yok oluyor.
        if (!value && last != null)
            last.OnComplete(() => { if (!open && flyout != null) flyout.gameObject.SetActive(false); });
        else if (!value)
            flyout.gameObject.SetActive(false);
    }

    // ==================== AYARLAR ====================

    // Tek buton ikisini birden yönetiyor: oyun içinde "sesi kapat" derken
    // oyuncu titreşimi de kastediyor, ayrı ayrı buton koymak alt barı şişirir.
    void ToggleSfx()
    {
        bool value = !AudioManager.SfxEnabled;

        AudioManager.SfxEnabled = value;
        HapticManager.HapticsEnabled = value;

        RefreshToggleVisuals();
    }

    void ToggleMusic()
    {
        AudioManager.MusicEnabled = !AudioManager.MusicEnabled;

        RefreshToggleVisuals();
    }

    void RefreshToggleVisuals()
    {
        ApplyState(sfxButton, AudioManager.SfxEnabled, sfxOnSprite, sfxOffSprite);
        ApplyState(musicButton, AudioManager.MusicEnabled, musicOnSprite, musicOffSprite);
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

    // ==================== ÇIKIŞ ====================

    void OpenExitConfirm()
    {
        SetMenuOpen(false);

        // Panel kurulmadıysa akış tıkanmasın
        if (exitPanel == null)
        {
            QuitNow();
            return;
        }

        if (exitInfoText != null)
            exitInfoText.text = "Şimdi çıkarsan 1 can kaybedersin!";

        exitPanel.SetActive(true);

        Transform pop = exitBox != null ? exitBox : exitPanel.transform;
        pop.DOKill();
        pop.localScale = Vector3.zero;
        pop.DOScale(1f, 0.3f).SetEase(Ease.OutBack);

        // Panel açıkken tahta oynanmasın: panelin karartması UI tıklamasını
        // yutuyor ama tahta girdisi EventSystem'den geçmiyor, ayrıca kilitliyoruz.
        if (GameManager.Instance != null) GameManager.Instance.SetPaused(true);
    }

    void CloseExitConfirm()
    {
        if (exitPanel != null)
        {
            if (exitBox != null) exitBox.DOKill();
            exitPanel.transform.DOKill();
            exitPanel.SetActive(false);
        }

        if (GameManager.Instance != null) GameManager.Instance.SetPaused(false);
    }

    void QuitNow()
    {
        if (GameManager.Instance == null) return;

        GameManager.Instance.SetPaused(false);
        GameManager.Instance.QuitToMenu();
    }

    void OnDisable()
    {
        if (items == null) return;

        foreach (RectTransform item in items)
            if (item != null) item.DOKill();

        if (exitBox != null) exitBox.DOKill();
        if (exitPanel != null) exitPanel.transform.DOKill();
    }
}
