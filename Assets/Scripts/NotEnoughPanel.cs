using System;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// "Şu kaynağın yetmiyor" penceresi: altın, can, ileride ne gelirse.
//
// Eskiden her kaynağın kendi paneli vardı - MarketUI'da noGoldPanel, LivesUI'da
// noLivesPanel - ve açılış animasyonu iki yerde birebir kopyalanmıştı. Üçüncü
// bir kaynak eklemek üçüncü bir panel kurmak demekti.
//
// Artık tek pencere. Çağıran hangi ikonu, hangi yazıyı ve - varsa - hangi
// aksiyonu istediğini söylüyor:
//
//   panel.ShowNotEnoughGold(450);                      // düz uyarı
//   panel.Show(lifeIcon, "Canın bitti!", 1, "24:19",   // aksiyonlu
//              "450 ALTINA DOLDUR", BuyRefill);
//
// Aksiyon verilmezse buton gizleniyor, pencere düz uyarıya dönüyor.
public class NotEnoughPanel : MonoBehaviour
{
    [Tooltip("Karartmayla birlikte açılıp kapanan kök. Boş bırakırsan bu " +
             "objenin kendisi kullanılır.")]
    [SerializeField] GameObject root;

    [Tooltip("Zıplama animasyonu buna uygulanıyor - kökü ölçeklersek arkadaki " +
             "karartma da ölçeklenir")]
    [SerializeField] RectTransform box;

    [Tooltip("Eksik olan şeyin resmi: altın kesesi, kalp... Çağıran veriyor.")]
    [SerializeField] Image resourceIcon;

    [Tooltip("Üst satır: 'Yeterli altının yok!'")]
    [SerializeField] TextMeshProUGUI titleText;

    [Tooltip("Kaç tane eksik: '450'. Sıfır verilirse objesi gizleniyor.")]
    [SerializeField] TextMeshProUGUI amountText;

    [Tooltip("Alt satır: canın geri sayımı gibi canlı güncellenen bilgi. " +
             "Boşsa gizleniyor.")]
    [SerializeField] TextMeshProUGUI infoText;

    [Tooltip("Opsiyonel aksiyon butonu - 'ALTINA DOLDUR'. Çağıran aksiyon " +
             "vermezse gizleniyor.")]
    [SerializeField] Button actionButton;

    [Tooltip("Aksiyon butonunun üstündeki yazı")]
    [SerializeField] TextMeshProUGUI actionLabel;

    [SerializeField] Button closeButton;

    [Header("Altın Kısayolu")]
    [Tooltip("ShowNotEnoughGold bu ikonu kullanıyor - altın uyarısını çağıran " +
             "her yerin ayrı ayrı sprite taşımaması için burada duruyor")]
    [SerializeField] Sprite goldIcon;

    [SerializeField] string goldTitle = "Yeterli altının yok!";

    [Tooltip("Altın uyarısındaki butonun yazısı - sadece pencereyi kapatıyor")]
    [SerializeField] string goldActionText = "TAMAM";

    // Aksiyon butonuna basılınca çalışacak iş. Her Show çağrısı bunu
    // yeniliyor, böylece panel bir öncekinin işini taşımıyor.
    Action pendingAction;

    // Dinleyiciler bağlandı mı. Awake'e güvenmiyoruz: panel sahnede kapalı
    // başlatılırsa Awake hiç çalışmaz ve kapatma butonu sessizce ölür.
    bool wired;

    // Show çağrıldı mı. Panel sahnede KAPALI duruyorsa Awake ilk Show'un
    // SetActive(true) satırında çalışır - oradaki "kendini kapat" satırı da
    // açılışı anında yer, ilk tıklama boşa giderdi. Bu bayrak o iki durumu
    // ayırıyor: açılışta kapat, ama açılmak üzereyken dokunma.
    bool opening;

    // Paneli en son kim açtı. Tek pencereyi birden fazla script kullanıyor;
    // canlı güncelleme yapan taraf (can geri sayımı) yazmadan önce panelin
    // hâlâ KENDİ içeriğini gösterdiğinden emin olmalı. Sadece "açık mı" diye
    // bakmak yetmiyor: market altın uyarısını açtığında can sayacı üstüne
    // "Can hazır!" yazıyordu.
    Component owner;

    GameObject Root => root != null ? root : gameObject;

    public bool IsOpen => Root.activeSelf;

    public bool IsShowingFor(Component candidate) => IsOpen && owner == candidate;

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
        if (actionButton != null) actionButton.onClick.AddListener(RunAction);
    }

    // ==================== AÇMA ====================

    // missing: kaç tane eksik. Sıfır verirsen sayı satırı gizleniyor - canda
    // olduğu gibi "adet" kavramının anlamsız olduğu yerler için.
    public void Show(Sprite icon, string title, int missing, string info = null,
                     string actionText = null, Action onAction = null,
                     Component showingFor = null)
    {
        EnsureWired();

        pendingAction = onAction;
        owner = showingFor;

        if (resourceIcon != null)
        {
            resourceIcon.sprite = icon;
            resourceIcon.gameObject.SetActive(icon != null);
        }

        if (titleText != null) titleText.text = title;

        if (amountText != null)
        {
            amountText.text = missing > 0 ? missing.ToString() : string.Empty;
            amountText.gameObject.SetActive(missing > 0);
        }

        SetInfo(info);

        bool hasAction = onAction != null && actionButton != null;

        if (actionButton != null) actionButton.gameObject.SetActive(hasAction);
        if (hasAction && actionLabel != null) actionLabel.text = actionText;

        // SetActive'den ÖNCE işaretliyoruz: obje ilk kez aktifleşiyorsa Awake
        // tam bu satırda çalışacak ve bayrağı orada okuyacak.
        opening = true;
        Root.SetActive(true);

        Transform pop = box != null ? (Transform)box : Root.transform;
        pop.DOKill();
        pop.localScale = Vector3.zero;
        pop.DOScale(1f, 0.3f).SetEase(Ease.OutBack);
    }

    // Altın uyarısının kısayolu: ikonu ve başlığı panelin kendisi biliyor.
    //
    // Aksiyon olarak Hide veriyoruz: yapacak bir şey yok ama buton hiç
    // çıkmayınca pencere yarım görünüyor, oyuncu sağ üstteki X'i aramak
    // zorunda kalıyor.
    public void ShowNotEnoughGold(int missing)
    {
        Show(goldIcon, goldTitle, missing, null, goldActionText, Hide);
    }

    // Panel açıkken alt satırı tazelemek için - can geri sayımı bunu saniyede
    // bir çağırıyor.
    public void SetInfo(string info)
    {
        if (infoText == null) return;

        bool has = !string.IsNullOrEmpty(info);

        infoText.text = has ? info : string.Empty;

        if (infoText.gameObject.activeSelf != has) infoText.gameObject.SetActive(has);
    }

    // ==================== KAPAMA ====================

    public void Hide()
    {
        pendingAction = null;
        owner = null;
        opening = false;

        if (box != null) box.DOKill();

        Root.transform.DOKill();
        Root.SetActive(false);
    }

    void RunAction()
    {
        Action action = pendingAction;
        if (action == null) return;

        // Aksiyon paneli kapatabilir ya da yeniden açabilir - can doldurmaya
        // altın yetmediğinde olduğu gibi. Referansı ÖNCE temizliyoruz ki hem
        // çift tıklama iki kez harcamasın hem de aksiyonun kendi Show çağrısı
        // üzerine yazabilsin.
        pendingAction = null;

        action();
    }

    void OnDisable()
    {
        if (box != null) box.DOKill();

        transform.DOKill();
    }
}
