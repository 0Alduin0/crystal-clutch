using System;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Ana menüdeki market sayfası: dört power-up, her biri bir satır, her satırda
// 1x / 3x / 5x paketleri altınla satılıyor. Paket boyutları SaveSystem'de.
// En üstteki günlük sürprizin adetleri paketlerden bağımsız, rastgele (DailyGiftUI).
//
// Fiyat tabloyla değil formülle geliyor: power-up başına BİRİM fiyat + pakete
// göre indirim. 12 hücreli bir matrisi elle güncellemek zorunda kalmıyorsun.
//
// Stok SaveSystem'de duruyor, yani satın alınan power-up oyun sahnesindeki
// alt barda anında görünüyor.
public class MarketUI : MonoBehaviour
{
    [Serializable]
    public class Row
    {
        [Tooltip("Sadece Inspector'da ayırt etmek için - oyunda kullanılmıyor")]
        public string label;

        [Tooltip("Satırdaki power-up ikonu - powerUpIcons'tan geliyor, opsiyonel")]
        public Image icon;

        [Tooltip("Satırın üstünde ortada duran isim - powerUpNames'ten geliyor, opsiyonel")]
        public TextMeshProUGUI nameText;

        [Tooltip("Elde kaç tane var yazısı - opsiyonel")]
        public TextMeshProUGUI ownedText;

        [Tooltip("Satın alma butonları, paketlerle AYNI sırada: 1x, 3x, 5x")]
        public Button[] buyButtons;

        [Tooltip("Butonların altındaki fiyat yazıları - buyButtons ile aynı sırada")]
        public TextMeshProUGUI[] priceTexts;

        [Tooltip("Butonların üstündeki '3x' yazıları - buyButtons ile aynı sırada, opsiyonel")]
        public TextMeshProUGUI[] packTexts;
    }

    [Tooltip("Dört power-up, alt bardaki sırayla")]
    [SerializeField] Row[] rows = new Row[4];

    [Tooltip("Power-up ikonları, rows ile aynı sırada. Satır ikonu, onay penceresi ve günlük sürpriz buradan alıyor")]
    [SerializeField] Sprite[] powerUpIcons = new Sprite[4];

    [Header("Fiyatlandırma")]
    [Tooltip("Paket başına indirim çarpanı, paketlerle aynı sırada (1x, 3x, 5x): 1 = indirim yok, 0.8 = %20 indirim")]
    [SerializeField] float[] packDiscounts = { 1f, 0.9f, 0.8f };

    [Tooltip("Power-up başına BİR adedin fiyatı - rows ile aynı sırada")]
    [SerializeField] int[] unitPrices = { 100, 150, 200, 250 };

    [Tooltip("Onay penceresinde geçen isimler - rows ile aynı sırada")]
    [SerializeField] string[] powerUpNames = { "Power-up 1", "Power-up 2", "Power-up 3", "Power-up 4" };

    [Header("Onay Penceresi")]
    [Tooltip("Bağlanmazsa onay sorulmaz, tıklama doğrudan satın alır")]
    [SerializeField] GameObject confirmPanel;

    [Tooltip("Zıplama animasyonu buna uygulanıyor - paneli ölçeklersek karartma da ölçeklenir")]
    [SerializeField] RectTransform confirmBox;

    // Üç ayrı yazı, NotEnoughPanel'in satırlarıyla birebir aynı sırada:
    // başlık / vurgu / bilgi. İki pencere ikiz görünsün diye böldük; tek
    // kutuda "\n" ile alt alta yazınca font ve hizalama tutmuyordu.
    [Tooltip("Sabit başlık: 'Satın al?' - NotEnough'un başlık satırının karşılığı")]
    [SerializeField] TextMeshProUGUI confirmTitle;

    [Tooltip("Vurgu satırı: '3x Şimşek'")]
    [SerializeField] TextMeshProUGUI confirmText;

    [Tooltip("Bilgi satırı: '450 altın'")]
    [SerializeField] TextMeshProUGUI confirmPriceText;

    [Tooltip("confirmTitle'a yazılan sabit metin")]
    [SerializeField] string confirmTitleLabel = "Satın al?";

    [Tooltip("Hangi power-up alınıyor - satırdaki ikon buraya kopyalanıyor, opsiyonel")]
    [SerializeField] Image confirmIcon;

    [SerializeField] Button confirmYesButton;
    [SerializeField] Button confirmCloseButton;

    [Header("Yetersiz Kaynak Penceresi")]
    [Tooltip("Altın yetmediğinde açılan ortak pencere. Bağlanmazsa tıklama " +
             "sessizce boşa gider.")]
    [SerializeField] NotEnoughPanel notEnoughPanel;

    // Onay penceresi açıkken hangi satır/paket bekliyor
    int pendingRow = -1;
    int pendingPack = -1;

    void OnEnable()
    {
        // Sayfa her açıldığında tazele: başka sekmede altın harcanmış olabilir.
        Refresh();
    }

    void Start()
    {
        for (int r = 0; r < rows.Length; r++)
        {
            Row row = rows[r];
            if (row == null || row.buyButtons == null) continue;

            for (int p = 0; p < row.buyButtons.Length; p++)
            {
                if (row.buyButtons[p] == null) continue;

                // Döngü değişkenlerini doğrudan kapatmaya bırakmıyoruz -
                // her dinleyici kendi satır/paket çiftini taşısın.
                int rowIndex = r;
                int packIndex = p;

                row.buyButtons[p].onClick.AddListener(() => Buy(rowIndex, packIndex));
            }
        }

        if (confirmPanel != null) confirmPanel.SetActive(false);

        if (confirmYesButton != null) confirmYesButton.onClick.AddListener(ConfirmPurchase);
        if (confirmCloseButton != null) confirmCloseButton.onClick.AddListener(CloseConfirm);

        Refresh();
    }

    // ==================== SATIN ALMA ====================

    // Butona basmak doğrudan satın almıyor: önce onay, altın yetmiyorsa uyarı.
    void Buy(int rowIndex, int packIndex)
    {
        int price = GetPrice(rowIndex, packIndex);
        int amount = GetPackSize(packIndex);

        if (price <= 0 || amount <= 0) return;

        if (SaveSystem.Gold < price)
        {
            OpenNoGold(price);
            return;
        }

        OpenConfirm(rowIndex, packIndex, amount, price);
    }

    void OpenConfirm(int rowIndex, int packIndex, int amount, int price)
    {
        pendingRow = rowIndex;
        pendingPack = packIndex;

        // Panel kurulmadıysa akış tıkanmasın, doğrudan al
        if (confirmPanel == null)
        {
            ConfirmPurchase();
            return;
        }

        if (confirmTitle != null) confirmTitle.text = confirmTitleLabel;

        if (confirmText != null) confirmText.text = amount + "x " + GetName(rowIndex);

        if (confirmPriceText != null) confirmPriceText.text = price + " altın";

        if (confirmIcon != null)
        {
            Sprite sprite = GetPowerUpIcon(rowIndex);
            if (sprite != null) confirmIcon.sprite = sprite;
        }

        Open(confirmPanel, confirmBox);
    }

    void ConfirmPurchase()
    {
        int rowIndex = pendingRow;
        int packIndex = pendingPack;

        CloseConfirm();

        int price = GetPrice(rowIndex, packIndex);
        int amount = GetPackSize(packIndex);

        if (price <= 0 || amount <= 0) return;

        // Onay ekranı açıkken bakiye değişmiş olabilir - son kontrol burada.
        // Yetmiyorsa SpendGold hiçbir şey harcamıyor ve false dönüyor.
        if (!SaveSystem.SpendGold(price))
        {
            OpenNoGold(price);
            return;
        }

        SaveSystem.AddPowerUp(rowIndex, amount);

        // Üst bardaki altın sayarak düşsün - harcamanın görüldüğü tek yer orası
        if (TopBarUI.Instance != null) TopBarUI.Instance.Refresh();

        PunchButton(rowIndex, packIndex);
        Refresh();
    }

    void CloseConfirm()
    {
        pendingRow = -1;
        pendingPack = -1;

        Close(confirmPanel, confirmBox);
    }

    // price: paketin tam fiyatı. Panele EKSİK olan kadarını gösteriyoruz -
    // "450 altın lazım" değil, "120 altının eksik". Oyuncunun bakması gereken
    // sayı bu.
    void OpenNoGold(int price)
    {
        CloseConfirm();

        if (notEnoughPanel == null) return;

        notEnoughPanel.ShowNotEnoughGold(price - SaveSystem.Gold);
    }

    static void Open(GameObject panel, RectTransform box)
    {
        if (panel == null) return;

        panel.SetActive(true);

        Transform pop = box != null ? box : panel.transform;
        pop.DOKill();
        pop.localScale = Vector3.zero;
        pop.DOScale(1f, 0.3f).SetEase(Ease.OutBack);
    }

    static void Close(GameObject panel, RectTransform box)
    {
        if (panel == null) return;

        if (box != null) box.DOKill();

        panel.transform.DOKill();
        panel.SetActive(false);
    }

    string GetName(int rowIndex)
    {
        if (powerUpNames == null || rowIndex < 0 || rowIndex >= powerUpNames.Length)
            return "Power-up";

        return powerUpNames[rowIndex];
    }

    public Sprite GetPowerUpIcon(int index)
    {
        if (powerUpIcons == null || index < 0 || index >= powerUpIcons.Length) return null;

        return powerUpIcons[index];
    }

    void PunchButton(int rowIndex, int packIndex)
    {
        Button button = GetButton(rowIndex, packIndex);
        if (button == null) return;

        button.transform.DOComplete();
        button.transform.DOPunchScale(Vector3.one * 0.15f, 0.25f, 4);
    }

    // ==================== FİYAT ====================

    public int GetPrice(int rowIndex, int packIndex)
    {
        if (unitPrices == null || rowIndex < 0 || rowIndex >= unitPrices.Length) return 0;

        int size = GetPackSize(packIndex);
        if (size <= 0) return 0;

        float discount = packDiscounts != null && packIndex < packDiscounts.Length
                       ? packDiscounts[packIndex]
                       : 1f;

        return Mathf.RoundToInt(unitPrices[rowIndex] * size * discount);
    }

    static int GetPackSize(int packIndex) => SaveSystem.GetPackSize(packIndex);

    Button GetButton(int rowIndex, int packIndex)
    {
        if (rows == null || rowIndex < 0 || rowIndex >= rows.Length) return null;

        Row row = rows[rowIndex];
        if (row == null || row.buyButtons == null) return null;
        if (packIndex < 0 || packIndex >= row.buyButtons.Length) return null;

        return row.buyButtons[packIndex];
    }

    // ==================== GÖRÜNÜM ====================

    public void Refresh()
    {
        if (rows == null) return;

        for (int r = 0; r < rows.Length; r++)
        {
            Row row = rows[r];
            if (row == null) continue;

            if (row.nameText != null) row.nameText.text = GetName(r);

            if (row.icon != null)
            {
                Sprite sprite = GetPowerUpIcon(r);
                if (sprite != null) row.icon.sprite = sprite;
            }

            if (row.ownedText != null) row.ownedText.text = "x" + SaveSystem.GetPowerUp(r);

            if (row.buyButtons == null) continue;

            for (int p = 0; p < row.buyButtons.Length; p++)
            {
                int price = GetPrice(r, p);

                if (row.priceTexts != null && p < row.priceTexts.Length && row.priceTexts[p] != null)
                    row.priceTexts[p].text = price.ToString();

                if (row.packTexts != null && p < row.packTexts.Length && row.packTexts[p] != null)
                    row.packTexts[p].text = GetPackSize(p) + "x";
            }
        }
    }

    void OnDisable()
    {
        if (rows == null) return;

        foreach (Row row in rows)
        {
            if (row == null || row.buyButtons == null) continue;

            foreach (Button button in row.buyButtons)
                if (button != null) button.transform.DOKill();
        }

        if (confirmBox != null) confirmBox.DOKill();
    }
}
