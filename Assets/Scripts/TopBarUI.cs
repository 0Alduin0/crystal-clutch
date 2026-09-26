using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Menüdeki üst bar: profil, altın, yıldız kristali, ayarlar.
// Sayılar SaveSystem'den okunuyor. Level sonunda kazanılan miktarlar Pending
// alanlarında bekliyor ve menü açılınca sayarak ekleniyor - oyuncu ödülün
// kasaya girdiğini görsün.
public class TopBarUI : MonoBehaviour
{
    public static TopBarUI Instance;

    // Ödül Game sahnesinde veriliyor, üst bar ise MainPages'te. Araya sahne
    // değişimi girdiği için normal alanla taşınamıyor, static olmak zorunda.
    // Animasyon oynadıktan sonra sıfırlanıyor.
    public static int PendingGold;
    public static int PendingDragonEgg;

    [Header("Yazılar")]
    [SerializeField] TextMeshProUGUI goldText;
    [SerializeField] TextMeshProUGUI dragonEggText;

    // Can burada YAZILMIYOR - LivesUI'ın işi. Can hem sayı hem geri sayım
    // olduğu için iki ayrı bileşenin aynı yazıya dokunması, sayacın bir
    // saniye eski değeri geri yazması demekti.

    [Header("Butonlar")]
    [Tooltip("Sol üstteki profil fotoğrafı - basınca oyuncunun kendi profili " +
             "açılıyor. Profile objesine Button ekleyip buraya sürükle.")]
    [SerializeField] Button profileButton;

    [SerializeField] ProfilePanel profilePanel;

    [SerializeField] Button settingsButton;

    [Tooltip("Ayarlar butonuna basınca açılacak panel - opsiyonel")]
    [SerializeField] GameObject settingsPanel;

    [Tooltip("Panel tam ekransa: açılırken zıplayacak iç kap (Safe). Boş " +
             "bırakırsan panelin kendisi ölçeklenir - tam ekran panelde bu " +
             "ekranın ortasından büyüyen bir kutu gibi durur.")]
    [SerializeField] RectTransform settingsContent;

    [SerializeField] Button closeSettingsButton;

    [Header("Animasyon")]
    [Tooltip("Kazanılan miktarın sayarak eklenme süresi")]
    [SerializeField] float countDuration = 0.6f;

    [Tooltip("Sayarken yazının zıplama miktarı")]
    [SerializeField] float punchScale = 0.2f;

    // Panelin karartmasını sönümlemek için gerekiyor. Elle eklemeyi unutmak
    // kolay olduğu için yoksa kendimiz ekliyoruz.
    CanvasGroup settingsGroup;

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        if (settingsPanel != null)
        {
            settingsGroup = settingsPanel.GetComponent<CanvasGroup>();

            if (settingsGroup == null)
                settingsGroup = settingsPanel.AddComponent<CanvasGroup>();

            settingsPanel.SetActive(false);
        }

        if (profileButton != null && profilePanel != null)
            profileButton.onClick.AddListener(profilePanel.ShowSelf);

        if (settingsButton != null)
            settingsButton.onClick.AddListener(() => ToggleSettings(true));

        if (closeSettingsButton != null)
            closeSettingsButton.onClick.AddListener(() => ToggleSettings(false));

        ShowInitial();
    }

    // ==================== SAYILAR ====================

    void ShowInitial()
    {
        int gold = SaveSystem.Gold;
        int stars = SaveSystem.DragonEgg;

        // Kazanılan miktarı geri çıkarıp ORADAN saymaya başlıyoruz. Böylece
        // "1240 -> 1285" gibi artışı oyuncu görüyor; doğrudan son değeri
        // yazsaydık ödül kasaya sessizce girmiş olurdu.
        Write(goldText, gold - PendingGold);
        Write(dragonEggText, stars - PendingDragonEgg);

        if (PendingGold > 0) CountTo(goldText, gold - PendingGold, gold);
        if (PendingDragonEgg > 0) CountTo(dragonEggText, stars - PendingDragonEgg, stars);

        PendingGold = 0;
        PendingDragonEgg = 0;
    }

    // Bakiye menü içinde değiştiyse (harcama, satın alma) çağır
    public void Refresh(bool animate = true)
    {
        int gold = SaveSystem.Gold;
        int stars = SaveSystem.DragonEgg;

        if (!animate)
        {
            Write(goldText, gold);
            Write(dragonEggText, stars);
            return;
        }

        CountTo(goldText, ReadShown(goldText), gold);
        CountTo(dragonEggText, ReadShown(dragonEggText), stars);
    }

    void CountTo(TextMeshProUGUI text, int from, int to)
    {
        if (text == null) return;

        DOTween.Kill(text);

        if (countDuration <= 0f || from == to)
        {
            Write(text, to);
            return;
        }

        int value = from;

        DOTween.To(() => value, x => { value = x; Write(text, x); }, to, countDuration)
               .SetEase(Ease.OutQuad)
               .SetId(text);

        text.transform.DOComplete();
        text.transform.DOPunchScale(Vector3.one * punchScale, 0.35f, 4);
    }

    static void Write(TextMeshProUGUI text, int value)
    {
        if (text != null) text.text = Mathf.Max(0, value).ToString();
    }

    // Ekranda yazan sayıyı geri oku - tween ortasında kesilirse oradan devam
    // etsin, sıçrama olmasın.
    static int ReadShown(TextMeshProUGUI text)
    {
        if (text == null) return 0;

        return int.TryParse(text.text, out int v) ? v : 0;
    }

    // ==================== AYARLAR ====================

    // Panel tam ekran kaplıyor: sıfırdan büyütmek yerine karartma sönümlenerek
    // geliyor, içerik hafif zıplıyor. Sıfırdan büyütseydik ekranın ortasından
    // açılan bir kutu gibi görünürdü - tam ekran sayfada yanlış durur.
    void ToggleSettings(bool open)
    {
        if (settingsPanel == null) return;

        Transform pop = settingsContent != null ? settingsContent : settingsPanel.transform;

        pop.DOKill();
        if (settingsGroup != null) settingsGroup.DOKill();

        if (open)
        {
            settingsPanel.SetActive(true);

            if (settingsGroup != null)
            {
                settingsGroup.blocksRaycasts = true;
                settingsGroup.alpha = 0f;
                settingsGroup.DOFade(1f, 0.2f);
            }

            pop.localScale = Vector3.one * 0.92f;
            pop.DOScale(1f, 0.3f).SetEase(Ease.OutBack);
            return;
        }

        if (settingsGroup == null)
        {
            settingsPanel.SetActive(false);
            return;
        }

        // Kapanırken tıklamayı hemen bırakıyoruz: sönme boyunca panel hâlâ
        // aktif ve raycast'i açık kalsaydı arkadaki sekmeleri yutardı.
        settingsGroup.blocksRaycasts = false;
        settingsGroup.DOFade(0f, 0.18f)
                     .OnComplete(() => settingsPanel.SetActive(false));
    }

    void OnDisable()
    {
        if (goldText != null) { DOTween.Kill(goldText); goldText.transform.DOKill(); }
        if (dragonEggText != null) { DOTween.Kill(dragonEggText); dragonEggText.transform.DOKill(); }

        if (settingsContent != null) settingsContent.DOKill();
        if (settingsGroup != null) settingsGroup.DOKill();
        if (settingsPanel != null) settingsPanel.transform.DOKill();
    }
}
