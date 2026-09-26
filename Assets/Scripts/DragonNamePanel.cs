using System;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Ejderha yumurtadan çıkınca açılan isim penceresi (Nest). Kapatma butonu yok:
// arkadaki karartma dokunuşları kesiyor, isim vermeden devam edilmiyor. Oyuncu
// sahneden çıkarsa bir dahaki girişte yine soruluyor (NestUI).
//
// Pencere sadece ismi toplayıp geri veriyor; kaydetmek NestUI'da. İsim bir
// kez yazılıyor, sonra değiştirilemiyor (SaveSystem.SetDragonName).
//
// Kurulumu: Match3 -> Yuva -> İsim penceresini kur (Nest açıkken)
public class DragonNamePanel : MonoBehaviour
{
    [Tooltip("Zıplama animasyonu buna uygulanıyor - kökü ölçeklersek arkadaki " +
             "karartma da ölçeklenir")]
    [SerializeField] RectTransform box;

    [SerializeField] TMP_InputField nameInput;
    [SerializeField] Button confirmButton;

    [Tooltip("İsim boşken çıkan uyarı - opsiyonel")]
    [SerializeField] TextMeshProUGUI hintText;

    [SerializeField] string emptyHint = "Ejderhana bir isim yaz";

    Action<string> onConfirm;

    // Dinleyiciler bağlandı mı. Panel sahnede kapalı başlıyor: Awake ilk
    // Show'a kadar hiç çalışmıyor, ona güvenemiyoruz.
    bool wired;

    // Show çağrıldı mı. Awake ilk Show'un SetActive(true) satırında çalışır -
    // oradaki "kendini kapat" satırı açılışı anında yerdi (ProfilePanel'deki durum).
    bool opening;

    // Karartmayla birlikte açılıp kapanan kök: bu objenin kendisi
    GameObject Root => gameObject;

    public bool IsOpen => Root.activeInHierarchy;

    void Awake()
    {
        EnsureWired();

        if (!opening && Root.activeSelf) Root.SetActive(false);
    }

    void EnsureWired()
    {
        if (wired) return;
        wired = true;

        if (confirmButton != null) confirmButton.onClick.AddListener(Confirm);

        if (nameInput != null)
        {
            nameInput.characterLimit = SaveSystem.MaxDragonNameLength;

            // Oyuncunun yazdığı etiket gibi işlenmesin: "<b>" yazan "<b>" görsün
            nameInput.richText = false;

            // Klavyedeki "Tamam" da onaylasın
            nameInput.onSubmit.AddListener(_ => Confirm());
            nameInput.onValueChanged.AddListener(_ => SetHint(null));
        }
    }

    // confirmed: temizlenmiş, boş olmayan isimle bir kez çağrılır
    public void Show(Action<string> confirmed)
    {
        EnsureWired();

        onConfirm = confirmed;

        if (nameInput != null) nameInput.text = string.Empty;
        SetHint(null);

        // SetActive'den ÖNCE işaretliyoruz: obje ilk kez aktifleşiyorsa Awake
        // tam bu satırda çalışacak ve bayrağı orada okuyacak.
        opening = true;
        Root.SetActive(true);

        Transform pop = box != null ? (Transform)box : Root.transform;
        pop.DOKill();
        pop.localScale = Vector3.zero;
        pop.DOScale(1f, 0.35f).SetEase(Ease.OutBack);
    }

    void Confirm()
    {
        if (!IsOpen || nameInput == null || onConfirm == null) return;

        string clean = SaveSystem.CleanDragonName(nameInput.text);

        // Fazla boşluklar gitti - oyuncu kaydedilecek halini görsün
        nameInput.text = clean;

        if (clean.Length == 0)
        {
            SetHint(emptyHint);

            if (box != null)
            {
                box.DOComplete();
                box.DOPunchPosition(Vector3.right * 20f, 0.35f, 10);
            }

            return;
        }

        // Hem buton hem klavyenin "Tamam"ı aynı karede gelebilir: tek sefer
        Action<string> done = onConfirm;
        onConfirm = null;

        Hide();
        done(clean);
    }

    void SetHint(string hint)
    {
        if (hintText == null) return;

        bool has = !string.IsNullOrEmpty(hint);

        hintText.text = has ? hint : string.Empty;
        hintText.gameObject.SetActive(has);
    }

    void Hide()
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
