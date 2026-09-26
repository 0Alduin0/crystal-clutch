using System;
using DG.Tweening;
using TMPro;
using UnityEngine;

// CANIN TEK SORUMLUSU. Sayıyı da, "sıradaki can" geri sayımını da, canın
// bittiğini söyleyen paneli de bu yazıyor.
//
// TopBarUI eskiden can sayısını kendisi yazıyordu; iki ayrı yerin aynı yazıya
// dokunması sayacın bir saniye eski değeri geri yazması demekti. Sahnede
// TopBar'daki "Lives Text" alanı BURAYA taşındı.
//
// Bileşen sahneden bağımsız: hem MainPages'e hem Game sahnesine konabilir.
// Game sahnesinde "TEKRAR DENE" canı kontrol edip bu panelin açılmasını
// isteyecek, o yüzden oraya da koymak gerekiyor.
public class LivesUI : MonoBehaviour
{
    // Sahne başına bir tane. GameManager ve MainMenuUI can kontrolü için
    // buna bakıyor; sahnede yoksa akış tıkanmıyor, sadece panel çıkmıyor.
    public static LivesUI Instance;

    [Header("Üst Bar")]
    [Tooltip("Can sayısı - eskiden TopBarUI'daki 'Lives Text' alanıydı")]
    [SerializeField] TextMeshProUGUI livesText;

    [Tooltip("Sıradaki cana kalan süre: '24:19'. Can doluysa gizleniyor.")]
    [SerializeField] TextMeshProUGUI timerText;

    [Tooltip("Can doluyken sayacın yerine ne yazsın. Boş bırakırsan sayaç " +
             "tamamen gizlenir.")]
    [SerializeField] string fullText = "DOLU";

    [Header("Canın Bitti Paneli")]
    [Tooltip("Can bitince açılan ortak pencere - altın uyarısıyla aynı panel. " +
             "Bağlanmazsa oyuna girilmez ama ekranda bir şey de çıkmaz, " +
             "oyuncuya bozuk hissettirir; bağla.")]
    [SerializeField] NotEnoughPanel notEnoughPanel;

    [Tooltip("Pencerede gösterilecek kalp resmi")]
    [SerializeField] Sprite lifeIcon;

    [Header("Altınla Doldurma")]
    [Tooltip("Canı tepeye çekmenin altın maliyeti")]
    [SerializeField] int refillGoldCost = 450;

    // Saniyede bir tazeleme. Her karede PlayerPrefs okumanın anlamı yok:
    // ApplyRegen string ayrıştırıyor ve mobilde bu ucuz değil.
    float tick;

    // Ekranda yazan değerler - değişmedikçe TMP'ye dokunmuyoruz
    int shownLives = -1;
    int shownSeconds = -1;

    // "Şu an DOLU yazıyor" işareti. Geri sayımın üretebileceği hiçbir
    // saniye değeriyle çakışmaması için negatif.
    const int FullSentinel = -2;

    void Awake()
    {
        Instance = this;
    }

    void OnDestroy()
    {
        // Sahne değişiminde eski sahnenin bileşeni Instance'ta asılı kalmasın
        if (Instance == this) Instance = null;
    }

    void Start()
    {
        // Panel kendi kapanışını ve kapatma butonunu yönetiyor; buradan
        // kurulacak bir şey kalmadı.
        Refresh();
    }

    void OnEnable()
    {
        // Sekme değişince / sahneye dönünce arka planda dolmuş canlar görünsün
        shownLives = -1;
        shownSeconds = -1;

        Refresh();
    }

    void Update()
    {
        tick += Time.unscaledDeltaTime;
        if (tick < 1f) return;

        tick = 0f;
        Refresh();
    }

    // ==================== DIŞARIYA AÇIK ====================

    public bool HasLife => SaveSystem.Lives > 0;

    // Levele girmeden ÖNCE çağrılır. Can varsa true; yoksa "canın bitti"
    // panelini açar ve false döner - çağıran taraf sahneyi yüklemesin.
    public bool TryStartLevel()
    {
        if (HasLife) return true;

        ShowNoLives();
        return false;
    }

    public void ShowNoLives()
    {
        Refresh();

        if (notEnoughPanel == null) return;

        // Canda "kaç tane eksik" diye bir kavram yok - tek bir can yeter, asıl
        // merak edilen ne zaman geleceği. O yüzden adet yerine geri sayımı
        // alt satıra veriyoruz.
        notEnoughPanel.Show(lifeIcon, "Canın bitti!", 0, NextLifeInfo(),
                            refillGoldCost + " ALTINA DOLDUR", BuyRefill, this);
    }

    public void HideNoLives()
    {
        if (notEnoughPanel != null) notEnoughPanel.Hide();
    }

    string NextLifeInfo()
    {
        if (SaveSystem.Lives > 0) return "Can hazır!";

        int seconds = Mathf.CeilToInt((float)SaveSystem.TimeToNextLife.TotalSeconds);

        return "Sıradaki can: " + Format(seconds);
    }

    // ==================== ALTINLA DOLDURMA ====================

    void BuyRefill()
    {
        // Zaten doluysa parayı yakmayalım - panel açık kalırken arka planda
        // canlar dolmuş olabilir.
        if (SaveSystem.IsLivesFull)
        {
            HideNoLives();
            return;
        }

        // Yetmiyorsa SpendGold hiçbir şey harcamıyor ve false dönüyor. Eskiden
        // burada sessizce çıkıyorduk ve oyuncu butona basıp hiçbir şey olmadan
        // bakakalıyordu; artık aynı pencere altın uyarısına dönüyor.
        if (!SaveSystem.SpendGold(refillGoldCost))
        {
            if (notEnoughPanel != null)
                notEnoughPanel.ShowNotEnoughGold(refillGoldCost - SaveSystem.Gold);

            return;
        }

        SaveSystem.RefillLives();

        // Üst bardaki altın sayarak düşsün
        if (TopBarUI.Instance != null) TopBarUI.Instance.Refresh();

        if (AudioManager.Instance != null) AudioManager.Instance.PlayPowerUp();

        Refresh();
        HideNoLives();
    }

    // ==================== GÖRÜNÜM ====================

    public void Refresh()
    {
        int lives = SaveSystem.Lives;   // zamanla dolanlar burada işleniyor

        if (lives != shownLives)
        {
            shownLives = lives;

            if (livesText != null)
            {
                livesText.text = lives.ToString();
                livesText.transform.DOComplete();
                livesText.transform.DOPunchScale(Vector3.one * 0.2f, 0.3f, 3);
            }
        }

        WriteTimer(lives);
        WriteNoLivesPanel();
    }

    void WriteTimer(int lives)
    {
        bool full = lives >= SaveSystem.MaxLives;

        GameObject root = timerText != null ? timerText.gameObject : null;

        // Dolu ve gösterecek bir şey yok: sayacı komple kaldır
        if (full && string.IsNullOrEmpty(fullText))
        {
            if (root != null && root.activeSelf) root.SetActive(false);
            shownSeconds = -1;
            return;
        }

        if (root != null && !root.activeSelf) root.SetActive(true);

        if (timerText == null) return;

        if (full)
        {
            // Sentinel geri sayımla çakışmasın: gerçek bir sayı olan 0'ı
            // kullansaydık "00:00" anındaki yazı "DOLU" sanılıp tazelenmezdi.
            if (shownSeconds != FullSentinel)
            {
                shownSeconds = FullSentinel;
                timerText.text = fullText;
            }

            return;
        }

        int seconds = Mathf.CeilToInt((float)SaveSystem.TimeToNextLife.TotalSeconds);

        if (seconds == shownSeconds) return;

        shownSeconds = seconds;
        timerText.text = Format(seconds);
    }

    // Alt satırdaki geri sayımı tazeliyor - ama SADECE panel bizim içeriğimizi
    // gösteriyorsa. Pencere ortak: market altın uyarısını açtığında ya da
    // doldurmaya altın yetmediğinde sahip biz değiliz, üstüne yazmayacağız.
    void WriteNoLivesPanel()
    {
        if (notEnoughPanel == null || !notEnoughPanel.IsShowingFor(this)) return;

        notEnoughPanel.SetInfo(NextLifeInfo());
    }

    // 24:19 / 1:05:30. Saat sadece gerektiğinde yazılıyor - 30 dakikalık
    // aralıkta üç canlık bekleme bile 1.5 saat, o yüzden ikisi de lazım.
    static string Format(int totalSeconds)
    {
        if (totalSeconds < 0) totalSeconds = 0;

        TimeSpan t = TimeSpan.FromSeconds(totalSeconds);

        if (t.TotalHours >= 1)
            return string.Format("{0}:{1:00}:{2:00}", (int)t.TotalHours, t.Minutes, t.Seconds);

        return string.Format("{0:00}:{1:00}", t.Minutes, t.Seconds);
    }

    void OnDisable()
    {
        // Panel kendi tween'lerini kendi OnDisable'ında öldürüyor.
        if (livesText != null) livesText.transform.DOKill();
    }
}
