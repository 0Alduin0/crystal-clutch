using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class UIManager : MonoBehaviour
{
    public static UIManager Instance;

    [Header("Üst Bar")]
    [SerializeField] TextMeshProUGUI movesText;

    [Header("Hedef Göstergesi (engeller)")]
    [Tooltip("Hedef ikonu slotları. Levelde bulunan her engel TÜRÜ için bir slot " +
             "kullanılır (şeffaf, katı, blok); kullanılmayanlar gizlenir. Slotlar " +
             "baştan sırayla doldurulur - levelde katı engel yoksa sonraki tür " +
             "onun yerine geçer, ortada boşluk kalmaz. Üç tür için 3 slot gerekir.")]
    [SerializeField] Image[] goalIcons;

    [Tooltip("İkonun yanındaki KALAN sayısı metinleri - goalIcons ile aynı sırada")]
    [SerializeField] TextMeshProUGUI[] goalCounts;

    [Tooltip("Şeffaf engelin hedef ikonu")]
    [SerializeField] Sprite transparentObstacleIcon;

    [Tooltip("Katı engelin hedef ikonu")]
    [SerializeField] Sprite solidObstacleIcon;

    [Tooltip("Lav temalı bloğun hedef ikonu")]
    [SerializeField] Sprite lavaBlockIcon;

    [Tooltip("Buz temalı bloğun hedef ikonu")]
    [SerializeField] Sprite iceBlockIcon;

    [Header("Kazanma Paneli")]
    [SerializeField] GameObject winPanel;
    [SerializeField] Button nextLevelButton;

    [Header("Altınla Devam Paneli")]
    [Tooltip("Hamle bitince kaybetme ekranından ÖNCE çıkan teklif paneli. " +
             "Bağlanmazsa teklif atlanır ve doğrudan kaybetme ekranı açılır.")]
    [SerializeField] GameObject continuePanel;

    [Tooltip("Maliyeti yazan metin - sadece sayı, yanına altın ikonu koy")]
    [SerializeField] TextMeshProUGUI continueCostText;

    [Tooltip("Bu paraya ne kazanıldığını yazan metin: '+5 HAMLE'")]
    [SerializeField] TextMeshProUGUI continueMovesText;

    [SerializeField] Button continueYesButton;

    [Tooltip("Sağ üstteki çarpı. Basınca level kaybedilmiş sayılır: can gider, " +
             "kaybetme ekranı açılır.")]
    [SerializeField] Button continueCloseButton;

    [Header("Kaybetme Paneli")]
    [SerializeField] GameObject losePanel;
    [SerializeField] Button retryButton;

    [Header("Hamle Kalmadı Uyarısı")]
    [Tooltip("Tahtada hamle kalmayınca kısa süre görünen uyarı - opsiyonel")]
    [SerializeField] GameObject noMovesBanner;
    [SerializeField] TextMeshProUGUI noMovesBannerText;

    // Hedef türleri. Slotlara bu sırayla yerleşiyorlar.
    const int GoalTransparent = 0;
    const int GoalSolid = 1;
    const int GoalBlock = 2;
    const int GoalKinds = 3;

    // Sayaçlar gerçekten değiştiyse yaz ve punch animasyonu oynat.
    // -1 = henüz hiç yazılmadı.
    readonly int[] lastLeft = { -1, -1, -1 };

    // Slot düzeni toplamlara göre bir kez kuruluyor; toplam değişmedikçe
    // SetActive/sprite ataması tekrarlanmıyor.
    readonly int[] setupTotals = { -1, -1, -1 };
    BlockTheme setupTheme;

    // Hangi tür hangi slotta - levelde tek tür varsa o 0. slota oturuyor
    readonly int[] slotOf = { -1, -1, -1 };

    // Çağrı başına dizi ayırmamak için
    readonly int[] leftBuffer = new int[GoalKinds];
    readonly int[] totalBuffer = new int[GoalKinds];

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        if (winPanel != null) winPanel.SetActive(false);
        if (losePanel != null) losePanel.SetActive(false);
        if (continuePanel != null) continuePanel.SetActive(false);
        if (noMovesBanner != null) noMovesBanner.SetActive(false);

        if (nextLevelButton != null)
            nextLevelButton.onClick.AddListener(() => GameManager.Instance.NextLevel());

        if (continueYesButton != null)
            continueYesButton.onClick.AddListener(() => GameManager.Instance.ContinueWithGold());

        if (continueCloseButton != null)
            continueCloseButton.onClick.AddListener(() => GameManager.Instance.GiveUp());

        if (retryButton != null)
            retryButton.onClick.AddListener(() => GameManager.Instance.RestartLevel());
    }

    public void UpdateMoves(int moves)
    {
        if (movesText == null) return;

        movesText.text = moves.ToString();
        movesText.transform.DOComplete();
        movesText.transform.DOPunchScale(Vector3.one * 0.25f, 0.25f, 3);

        // Az hamle kaldıysa kırmızıya dön
        movesText.color = moves <= 3 ? Color.red : Color.white;
    }

    // ==================== HEDEF: ENGELLER ====================
    // Levelin tek hedefi tahtadaki bütün engelleri kırmak. Üst barda her engel
    // TÜRÜ için ayrı bir ikon + KALAN sayısı duruyor ve bu sayılar yalnızca azalır.
    //
    // Sayım karenin BAŞLANGIÇ türüne göre: katı bir kare kırılınca şeffafa
    // dönüyor ama katı sütununda kalmaya devam ediyor, ancak tamamen
    // temizlenince düşüyor. Yoksa taş kırdıkça şeffaf sayacı artardı.
    public void UpdateObstacleGoal(int transparentLeft, int solidLeft, int blockLeft,
                                   int transparentTotal, int solidTotal, int blockTotal,
                                   BlockTheme blockTheme)
    {
        leftBuffer[GoalTransparent] = transparentLeft;
        leftBuffer[GoalSolid] = solidLeft;
        leftBuffer[GoalBlock] = blockLeft;

        totalBuffer[GoalTransparent] = transparentTotal;
        totalBuffer[GoalSolid] = solidTotal;
        totalBuffer[GoalBlock] = blockTotal;

        // Slot düzeni toplamlara bağlı. Toplam değişmesi = yeni level ya da
        // tahtanın geç kurulması (Board.Start ile GameManager.Start sırası
        // garanti değil, ilk çağrı 0/0 ile gelebiliyor).
        bool changed = blockTheme != setupTheme;

        for (int k = 0; k < GoalKinds; k++)
            if (totalBuffer[k] != setupTotals[k]) changed = true;

        if (changed)
        {
            for (int k = 0; k < GoalKinds; k++) setupTotals[k] = totalBuffer[k];
            setupTheme = blockTheme;

            SetupGoalSlots();

            // Slotlar yeniden kuruldu - sayaçlar bir kez daha yazılsın
            for (int k = 0; k < GoalKinds; k++) lastLeft[k] = -1;
        }

        for (int k = 0; k < GoalKinds; k++)
        {
            if (leftBuffer[k] == lastLeft[k]) continue;

            lastLeft[k] = leftBuffer[k];
            WriteGoalSlot(slotOf[k], leftBuffer[k]);
        }
    }

    // Levelde bulunan türleri baştan sırayla slotlara yerleştirir, kalan
    // slotları gizler. Tek tür varsa 0. slota oturur - sabit konumlu bir
    // düzende ortada boş kutu kalmasın.
    void SetupGoalSlots()
    {
        int next = 0;

        for (int k = 0; k < GoalKinds; k++)
            slotOf[k] = setupTotals[k] > 0 ? next++ : -1;

        // Sahnede yeterli slot yoksa sessizce bir türü göstermemek oyuncuyu
        // "neyi kırmam lazım" diye bırakır
        if (goalIcons == null || next > goalIcons.Length)
            WarnNotEnoughSlots(next);

        if (goalIcons == null) return;

        for (int i = 0; i < goalIcons.Length; i++)
        {
            if (goalIcons[i] == null) continue;

            // İkonun kendisi değil, slot kutusu açılıp kapanıyor: sayaç metni
            // de aynı kutunun içinde duruyor.
            Transform slot = goalIcons[i].transform.parent;
            if (slot != null) slot.gameObject.SetActive(i < next);
        }

        AssignGoalIcon(slotOf[GoalTransparent], transparentObstacleIcon);
        AssignGoalIcon(slotOf[GoalSolid], solidObstacleIcon);
        AssignGoalIcon(slotOf[GoalBlock], setupTheme == BlockTheme.Buz ? iceBlockIcon : lavaBlockIcon);
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
    void WarnNotEnoughSlots(int needed)
    {
        int have = goalIcons != null ? goalIcons.Length : 0;

        Debug.LogWarning($"[UIManager] Bu levelde {needed} engel türü var ama hedef " +
                         $"göstergesinde {have} slot bağlı - fazlası görünmeyecek. " +
                         $"Goal Icons / Goal Counts dizilerine slot ekle.", this);
    }

    void AssignGoalIcon(int slot, Sprite icon)
    {
        if (slot < 0 || icon == null) return;
        if (goalIcons == null || slot >= goalIcons.Length || goalIcons[slot] == null) return;

        goalIcons[slot].sprite = icon;
    }

    void WriteGoalSlot(int slot, int remaining)
    {
        if (slot < 0) return;
        if (goalCounts == null || slot >= goalCounts.Length || goalCounts[slot] == null) return;

        goalCounts[slot].text = remaining <= 0 ? "✓" : remaining.ToString();

        goalCounts[slot].transform.DOComplete();
        goalCounts[slot].transform.DOPunchScale(Vector3.one * 0.2f, 0.2f, 3);
    }

    // Bitiş bonusu başlarken
    public void ShowNoMovesBanner()
    {
        if (noMovesBanner == null) return;

        noMovesBanner.SetActive(true);

        if (noMovesBannerText != null)
            noMovesBannerText.text = "HAMLE KALMADI!";

        noMovesBanner.transform.DOKill();
        noMovesBanner.transform.localScale = Vector3.zero;
        noMovesBanner.transform.DOScale(1f, 0.35f).SetEase(Ease.OutBack);
    }

    public void HideNoMovesBanner()
    {
        if (noMovesBanner == null) return;

        noMovesBanner.transform.DOKill();
        noMovesBanner.transform.DOScale(0f, 0.25f)
                     .SetEase(Ease.InBack)
                     .OnComplete(() => noMovesBanner.SetActive(false));
    }

    // hasNextLevel false ise bu son leveldi: panel "hepsini bitirdin" moduna
    // geçiyor. Butonun kendisi zaten NextLevel'a bağlı, o da sıradaki level
    // yoksa menüye dönüyor - burada sadece yazısını doğru söyletiyoruz.
    public void ShowWin(bool hasNextLevel)
    {
        if (winPanel == null) return;

        winPanel.SetActive(true);

        SetNextButtonLabel(hasNextLevel ? "SONRAKİ" : "MENÜYE DÖN");

        winPanel.transform.localScale = Vector3.zero;
        winPanel.transform.DOScale(1f, 0.4f).SetEase(Ease.OutBack);
    }

    // Butonun içindeki TMP yazısını bul ve değiştir. Ayrı bir alan olarak
    // serialize etmiyoruz - buton prefabında yazı hep çocuk obje oluyor.
    void SetNextButtonLabel(string text)
    {
        if (nextLevelButton == null) return;

        TextMeshProUGUI label = nextLevelButton.GetComponentInChildren<TextMeshProUGUI>(true);
        if (label != null) label.text = text;
    }

    // ==================== ALTINLA DEVAM ====================

    // Teklifi göster. Panel bağlanmamışsa false döner ve GameManager doğrudan
    // kaybetme akışına geçer - oyuncu ekranda kilitli kalmasın.
    public bool ShowContinueOffer(int cost, int movesGranted, int playerGold)
    {
        if (continuePanel == null) return false;

        continuePanel.SetActive(true);

        if (continueCostText != null) continueCostText.text = cost.ToString();

        if (continueMovesText != null) continueMovesText.text = "+" + movesGranted + " HAMLE";

        // Altın yetmiyorsa "Evet" tıklanamaz olsun - basıp hiçbir şey olmaması
        // oyuncuya bozuk hissettirir.
        if (continueYesButton != null)
            continueYesButton.interactable = playerGold >= cost;

        continuePanel.transform.localScale = Vector3.zero;
        continuePanel.transform.DOScale(1f, 0.4f).SetEase(Ease.OutBack);

        return true;
    }

    public void HideContinueOffer()
    {
        if (continuePanel == null) return;

        continuePanel.transform.DOKill();
        continuePanel.SetActive(false);
    }

    public void ShowLose()
    {
        if (losePanel == null) return;

        losePanel.SetActive(true);

        losePanel.transform.localScale = Vector3.zero;
        losePanel.transform.DOScale(1f, 0.4f).SetEase(Ease.OutBack);
    }
}
