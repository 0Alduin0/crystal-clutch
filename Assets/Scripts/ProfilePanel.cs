using System;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Oyuncu profili: fotoğraf, isim, ulaşılan bölüm, oynamaya başladığı gün,
// bugüne kadar kazandığı ejder yumurtası.
//
// Tek panel iki iş görüyor:
//   ShowSelf()      - üst bardaki profil ikonu ya da tablodaki kendi satırın.
//                     Bilgiler cihazdan okunuyor, isim değiştirilebiliyor.
//   ShowPlayer(...) - tablodaki başka bir oyuncu. Bilgiler sunucudan
//                     (skorun yanındaki metadata), sadece okunuyor.
//
// Yazılar sadece DEĞER ("12", "24 Eylül 2026"). "Bölüm", "Başlangıç" gibi
// etiketler sahnede sabit yazı olarak duruyor.
public class ProfilePanel : MonoBehaviour
{
    [Tooltip("Zıplama animasyonu buna uygulanıyor - kökü ölçeklersek arkadaki " +
             "karartma da ölçeklenir")]
    [SerializeField] RectTransform box;

    [SerializeField] Button closeButton;

    [Header("Bilgiler")]
    [SerializeField] Image avatar;
    [SerializeField] TextMeshProUGUI nameText;
    [SerializeField] TextMeshProUGUI levelText;

    [Tooltip("Oynamaya başladığı gün: '24 Eylül 2026'")]
    [SerializeField] TextMeshProUGUI sinceText;

    [Tooltip("Bugüne kadar KAZANDIĞI toplam yumurta - harcadıkça düşen bakiye değil")]
    [SerializeField] TextMeshProUGUI dragonEggText;

    [Tooltip("Oyuncunun kendi profil fotoğrafı")]
    [SerializeField] Sprite playerAvatar;

    [Tooltip("Bilinmeyen bilginin yerine yazılan - bu özellikten önce skor " +
             "göndermiş oyuncular ve internet yokken görünen yedek liste için")]
    [SerializeField] string unknownText = "-";

    [Header("İsim Değiştirme (sadece kendi profilinde)")]
    [Tooltip("İsim alanını ve kaydet butonunu kapsayan obje - başkasının " +
             "profilinde gizleniyor")]
    [SerializeField] GameObject nameEditRoot;

    [SerializeField] TMP_InputField nameInput;
    [SerializeField] Button saveNameButton;

    [Tooltip("İsim alanının altındaki kısa geri bildirim - opsiyonel")]
    [SerializeField] TextMeshProUGUI nameHintText;

    [SerializeField] string tooShortHint = "İsim en az {0} harf olmalı";
    [SerializeField] string savedHint = "Kaydedildi";

    static readonly string[] Months =
    {
        "Ocak", "Şubat", "Mart", "Nisan", "Mayıs", "Haziran",
        "Temmuz", "Ağustos", "Eylül", "Ekim", "Kasım", "Aralık"
    };

    // Dinleyiciler bağlandı mı. Awake'e güvenmiyoruz: panel sahnede kapalı
    // başlatılırsa Awake ilk Show'a kadar hiç çalışmaz.
    bool wired;

    // Show çağrıldı mı. Panel sahnede KAPALI duruyorsa Awake ilk Show'un
    // SetActive(true) satırında çalışır - oradaki "kendini kapat" satırı
    // açılışı anında yerdi. NotEnoughPanel'deki durumun aynısı.
    bool opening;

    // Karartmayla birlikte açılıp kapanan kök: bu objenin kendisi
    GameObject Root => gameObject;

    void Awake()
    {
        EnsureWired();

        if (!opening && Root.activeSelf) Root.SetActive(false);
    }

    void EnsureWired()
    {
        if (wired) return;
        wired = true;

        if (closeButton != null) closeButton.onClick.AddListener(Hide);
        if (saveNameButton != null) saveNameButton.onClick.AddListener(SaveName);

        // Klavyedeki "Tamam" da kaydetsin
        if (nameInput != null)
        {
            nameInput.characterLimit = SaveSystem.MaxPlayerNameLength;
            nameInput.onSubmit.AddListener(_ => SaveName());
        }
    }

    // ==================== AÇMA ====================

    public void ShowSelf()
    {
        Fill(playerAvatar, SaveSystem.PlayerName, SaveSystem.CurrentLevel + 1,
             SaveSystem.FirstPlayedAt.ToLocalTime().Date, SaveSystem.DragonEggsEarned);

        SetEditable(true);
        Open();
    }

    // since: DateTime.MinValue = bilinmiyor. dragonEggs: negatif = bilinmiyor.
    public void ShowPlayer(Sprite avatarSprite, string playerName, int level,
                           DateTime since, int dragonEggs)
    {
        Fill(avatarSprite, playerName, level, since, dragonEggs);

        SetEditable(false);
        Open();
    }

    void Fill(Sprite avatarSprite, string playerName, int level, DateTime since, int dragonEggs)
    {
        EnsureWired();

        if (avatar != null && avatarSprite != null) avatar.sprite = avatarSprite;

        if (nameText != null) nameText.text = playerName;
        if (levelText != null) levelText.text = level.ToString();

        if (sinceText != null)
            sinceText.text = since == DateTime.MinValue ? unknownText : FormatDate(since);

        if (dragonEggText != null)
            dragonEggText.text = dragonEggs < 0 ? unknownText : dragonEggs.ToString();
    }

    void SetEditable(bool editable)
    {
        if (nameEditRoot != null) nameEditRoot.SetActive(editable);

        if (editable && nameInput != null) nameInput.text = SaveSystem.PlayerName;

        SetHint(null);
    }

    void Open()
    {
        // SetActive'den ÖNCE işaretliyoruz: obje ilk kez aktifleşiyorsa Awake
        // tam bu satırda çalışacak ve bayrağı orada okuyacak.
        opening = true;
        Root.SetActive(true);

        Transform pop = box != null ? (Transform)box : Root.transform;
        pop.DOKill();
        pop.localScale = Vector3.zero;
        pop.DOScale(1f, 0.3f).SetEase(Ease.OutBack);
    }

    // Ay adlarını elle yazıyoruz: tr-TR kültürü her platformda (IL2CPP'nin
    // kırpılmış kültür verisiyle) gelmeyebiliyor, "September" yazmasın.
    static string FormatDate(DateTime date)
    {
        return date.Day + " " + Months[date.Month - 1] + " " + date.Year;
    }

    // ==================== İSİM ====================

    void SaveName()
    {
        if (nameInput == null) return;

        string clean = SaveSystem.CleanPlayerName(nameInput.text);

        // Boşluklar gitti - oyuncu kaydedilecek halini görsün
        nameInput.text = clean;

        if (clean.Length < SaveSystem.MinPlayerNameLength)
        {
            SetHint(string.Format(tooShortHint, SaveSystem.MinPlayerNameLength));
            return;
        }

        if (clean != SaveSystem.PlayerName)
        {
            SaveSystem.PlayerName = clean;
            LeaderboardService.PlayerNameChanged();

            if (nameText != null) nameText.text = clean;
        }

        SetHint(savedHint);
    }

    void SetHint(string hint)
    {
        if (nameHintText == null) return;

        bool has = !string.IsNullOrEmpty(hint);

        nameHintText.text = has ? hint : string.Empty;
        nameHintText.gameObject.SetActive(has);
    }

    // ==================== KAPAMA ====================

    public void Hide()
    {
        opening = false;

        if (box != null) box.DOKill();

        Root.transform.DOKill();
        Root.SetActive(false);
    }

    void OnDisable()
    {
        if (box != null) box.DOKill();

        transform.DOKill();
    }
}
