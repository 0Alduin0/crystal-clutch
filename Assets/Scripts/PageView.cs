using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Yatay sayfa kaydırıcı (Royal Match tarzı ana ekran).
// Alttaki sekmeye basınca ya da ekranı sağa/sola sürükleyince sayfa değişir.
//
// KURULUM:
//   Canvas
//   ├ Pager        [ScrollRect, RectMask2D, PageView]   <- tam ekran stretch
//   │  └ Content                                        <- konumunu script veriyor
//   │     ├ Page_0
//   │     ├ Page_1
//   │     └ Page_2
//   └ TabBar                                            <- Pager'DAN SONRA gelsin,
//      ├ Tab_0     [Button]                                yoksa sayfaların altında kalır
//      ├ Tab_1     [Button]
//      └ Tab_2     [Button]
//
// Content ve sayfaların pivot'u SOL kenarda (x = 0) olmalı - konum matematiği
// buna dayanıyor. Script zaten zorluyor, ama Inspector'da ters görürsen şaşırma.
//
// Sekme butonlarının OnClick'ine Inspector'dan bir şey bağlama; Start burada
// kendisi bağlıyor. İki yerden bağlarsan tıklama iki kez işler.
[RequireComponent(typeof(ScrollRect))]
public class PageView : MonoBehaviour, IBeginDragHandler, IEndDragHandler
{
    [Header("Sayfalar")]
    [SerializeField] RectTransform content;

    [Tooltip("Soldan sağa sırayla. Sıra ekrandaki sırayı belirler.")]
    [SerializeField] RectTransform[] pages;

    [Header("Sekmeler")]
    [Tooltip("pages ile AYNI sırada olmalı. Eksik bırakabilirsin - o sayfaya " +
             "sadece kaydırarak gidilir.")]
    [SerializeField] Button[] tabs;

    [Header("Davranış")]
    [Tooltip("Açılışta hangi sayfa. Ana sayfa ortadaysa 1.")]
    [SerializeField] int startPage = 1;

    [Tooltip("Sayfa çevirmek için ekran genişliğinin kaçta kaçı sürüklenmeli. " +
             "Küçük değer = daha hassas, kazara sayfa değişir.")]
    [Range(0.05f, 0.5f)]
    [SerializeField] float swipeThreshold = 0.2f;

    [SerializeField] float snapDuration = 0.25f;

    [Header("Sekme Görseli")]
    [Tooltip("Seçili sekmenin genişlik ağırlığı. Diğerleri 1 sayılır, yani " +
             "1.6 = seçili sekme diğerlerinin 1.6 katı genişlikte. Sekmeler " +
             "bitişik kalır; biri genişlerken komşuları daralır.")]
    [Range(1f, 3f)]
    [SerializeField] float selectedWidthWeight = 1.6f;

    [SerializeField] Color normalTabColor = new Color(0.82f, 0.82f, 0.82f, 1f);
    [SerializeField] Color selectedTabColor = new Color(1f, 0.78f, 0.25f, 1f);

    [Tooltip("Seçili sekmenin ikonu kaç katına büyüsün")]
    [Range(1f, 2f)]
    [SerializeField] float selectedIconScale = 1.2f;

    [SerializeField] float tabAnimDuration = 0.2f;

    ScrollRect scroll;
    RectTransform viewport;
    float pageWidth;
    int current;
    bool ready;
    Tween snapTween;

    LayoutElement[] tabLayouts;
    Image[] tabImages;      // sekmenin kök arka planı - rengi değişen
    Image[] tabIcons;       // sekmenin içindeki ikon - büyüyen
    TextMeshProUGUI[] tabLabels;   // ikonun altındaki isim - sadece seçilide görünen

    void Awake()
    {
        scroll = GetComponent<ScrollRect>();
        viewport = scroll.viewport != null ? scroll.viewport : (RectTransform)transform;

        // Snap'i biz yapıyoruz. ScrollRect'in ataleti açık kalırsa kendi
        // kaymasını sürdürürken bizim tween de çeker, sayfa titrer.
        scroll.horizontal = true;
        scroll.vertical = false;
        scroll.inertia = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
    }

    void Start()
    {
        if (content == null || pages == null || pages.Length == 0)
        {
            Debug.LogError("PageView: 'Content' ya da 'Pages' bağlanmamış.", this);
            enabled = false;
            return;
        }

        // Rect'ler daha oturmamış olabilir - sayfa genişliği buradan geliyor.
        Canvas.ForceUpdateCanvases();
        LayoutPages();

        CacheTabs();

        int tabCount = tabs != null ? Mathf.Min(tabs.Length, pages.Length) : 0;

        for (int i = 0; i < tabCount; i++)
        {
            int index = i;   // closure için kopyala
            if (tabs[i] != null) tabs[i].onClick.AddListener(() => GoTo(index));
        }

        ready = true;
        GoTo(startPage, false);
    }

    // ==================== YERLEŞİM ====================

    // Sayfaları viewport genişliğine göre yan yana diz. Elle konumlandırmak
    // yerine kodda yapıyoruz: farklı en/boy oranlarında tek doğru kaynak bu.
    // Genişlik Screen.width DEĞİL - CanvasScaler devrede, canvas birimi ≠ piksel.
    void LayoutPages()
    {
        pageWidth = viewport.rect.width;

        content.anchorMin = new Vector2(0f, 0f);
        content.anchorMax = new Vector2(0f, 1f);
        content.pivot     = new Vector2(0f, 0.5f);
        content.sizeDelta = new Vector2(pageWidth * pages.Length, 0f);

        for (int i = 0; i < pages.Length; i++)
        {
            RectTransform p = pages[i];
            if (p == null) continue;

            p.anchorMin = new Vector2(0f, 0f);
            p.anchorMax = new Vector2(0f, 1f);
            p.pivot     = new Vector2(0f, 0.5f);
            p.sizeDelta = new Vector2(pageWidth, 0f);
            p.anchoredPosition = new Vector2(i * pageWidth, 0f);
        }
    }

#if UNITY_EDITOR
    // Sayfaları editörde de çalışma anındaki boyut ve konumlarına oturtur.
    // Buna ihtiyaç var çünkü sayfalar normalde ancak Play'e basınca gerçek
    // boyutlarını alıyor; o zamana kadar içeriklerini doğru yerleştiremezsin.
    //
    // Kullanımı: Pager'daki PageView başlığına sağ tık -> "Sayfaları Diz".
    [ContextMenu("Sayfaları Diz (editör)")]
    void LayoutPagesInEditor()
    {
        if (content == null || pages == null || pages.Length == 0)
        {
            Debug.LogWarning("PageView: 'Content' ya da 'Pages' bağlanmamış.", this);
            return;
        }

        scroll = GetComponent<ScrollRect>();
        viewport = scroll.viewport != null ? scroll.viewport : (RectTransform)transform;

        LayoutPages();

        UnityEditor.EditorUtility.SetDirty(content);

        foreach (RectTransform page in pages)
            if (page != null) UnityEditor.EditorUtility.SetDirty(page);
    }
#endif

    // Ekran döndüğünde ya da çözünürlük değiştiğinde sayfa genişliği değişir.
    // Buraya gelindiğinde rect'ler zaten güncel - ForceUpdateCanvases çağırmak
    // layout'un ortasında yeniden layout tetiklemek olurdu.
    void OnRectTransformDimensionsChange()
    {
        if (!ready) return;

        LayoutPages();
        GoTo(current, false);
    }

    // ==================== SÜRÜKLEME ====================

    public void OnBeginDrag(PointerEventData eventData)
    {
        // Devam eden snap'i kes, yoksa parmakla tween aynı anda content'i çeker
        snapTween?.Kill();
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (!ready) return;

        // Parmağın bıraktığı yer, mevcut sayfanın hizasından ne kadar uzakta?
        // Pozitif = sağa doğru (sonraki sayfaya) sürüklenmiş.
        float dragged = -content.anchoredPosition.x - current * pageWidth;

        int target = current;

        if (dragged > pageWidth * swipeThreshold) target = current + 1;
        else if (dragged < -pageWidth * swipeThreshold) target = current - 1;

        GoTo(target);
    }

    // ==================== SAYFA GEÇİŞİ ====================

    // Sekme butonları ve dışarıdan çağrılar buraya gelir
    public void GoTo(int index) => GoTo(index, true);

    public void GoTo(int index, bool animate)
    {
        if (pages == null || pages.Length == 0) return;

        index = Mathf.Clamp(index, 0, pages.Length - 1);
        current = index;

        float targetX = -index * pageWidth;

        snapTween?.Kill();

        if (animate)
            snapTween = content.DOAnchorPosX(targetX, snapDuration).SetEase(Ease.OutCubic);
        else
            content.anchoredPosition = new Vector2(targetX, content.anchoredPosition.y);

        UpdateTabs(animate);
    }

    // ==================== SEKME GÖRSELİ ====================

    // Genişliği scale ile DEĞİL, LayoutElement.flexibleWidth ile değiştiriyoruz.
    // Scale görsel bir hile: obje layout'ta hâlâ eski yerini kaplar, büyüyen
    // sekme komşularının üstüne biner ve dikeyde de taşar. flexibleWidth ise
    // Layout Group'a genişliği yeniden DAĞITTIRIR - seçili sekme genişlerken
    // komşuları daralır, hepsi bitişik kalır, yükseklik hiç değişmez.
    void CacheTabs()
    {
        int n = tabs != null ? tabs.Length : 0;

        tabLayouts = new LayoutElement[n];
        tabImages = new Image[n];
        tabIcons = new Image[n];
        tabLabels = new TextMeshProUGUI[n];

        for (int i = 0; i < n; i++)
        {
            if (tabs[i] == null) continue;

            LayoutElement le = tabs[i].GetComponent<LayoutElement>();
            if (le == null) le = tabs[i].gameObject.AddComponent<LayoutElement>();

            // Tercih edilen genişlik 0: böylece TÜM genişlik esnek paya kalır
            // ve dağıtım tamamen ağırlıklara göre olur. Sıfırlamazsak sekmenin
            // sprite'ı kendi boyutunu tercih edip payı bozar.
            le.preferredWidth = 0f;
            le.flexibleWidth = 1f;

            tabLayouts[i] = le;
            tabImages[i] = tabs[i].GetComponent<Image>();
            tabIcons[i] = FindIcon(tabs[i]);
            tabLabels[i] = tabs[i].GetComponentInChildren<TextMeshProUGUI>(true);

            // Yazı HER ZAMAN açık kalıyor; görünürlüğünü alfa ile yönetiyoruz.
            // Kapatsaydık Layout Group yazının yerini geri alır, ikon aşağı
            // kayar ve seçili sekme dikeyde oynuyormuş gibi görünürdü.
            if (tabLabels[i] != null) tabLabels[i].gameObject.SetActive(true);

            // Button'ın kendi Color Tint geçişi aynı Image'ın rengini sürüyor.
            // Açık kalırsa parmak değdiği an bizim seçili rengimizi ezer, sonra
            // "normal" rengine döndürür - seçili sekme sönük kalırdı.
            tabs[i].transition = Selectable.Transition.None;
        }
    }

    // İkon = sekmenin ÇOCUĞU olan ilk Image. Kökteki Image sekmenin arka planı,
    // onu atlıyoruz. İsimle aramıyoruz ki objeyi yeniden adlandırınca bozulmasın.
    static Image FindIcon(Button tab)
    {
        Image[] found = tab.GetComponentsInChildren<Image>(true);

        for (int i = 0; i < found.Length; i++)
            if (found[i].transform != tab.transform) return found[i];

        return null;
    }

    void UpdateTabs(bool animate)
    {
        if (tabs == null || tabLayouts == null) return;

        float dur = animate ? tabAnimDuration : 0f;

        for (int i = 0; i < tabs.Length; i++)
        {
            bool selected = (i == current);

            LayoutElement le = tabLayouts[i];

            if (le != null)
            {
                float weight = selected ? selectedWidthWeight : 1f;

                DOTween.Kill(le);

                // flexibleWidth setter'ı layout'u kirli işaretliyor, yani her
                // adımda Layout Group kendini yeniden hesaplıyor.
                if (dur > 0f)
                    DOTween.To(() => le.flexibleWidth, x => le.flexibleWidth = x, weight, dur)
                           .SetEase(Ease.OutCubic)
                           .SetId(le);
                else
                    le.flexibleWidth = weight;
            }

            Image img = tabImages[i];

            if (img != null)
            {
                Color targetColor = selected ? selectedTabColor : normalTabColor;

                img.DOKill();

                if (dur > 0f) img.DOColor(targetColor, dur);
                else img.color = targetColor;
            }

            // İkon büyümesi scale ile - layout'u etkilemediği için altındaki
            // yazıyı itmiyor, sadece kendi merkezinden şişiyor.
            Image icon = tabIcons[i];

            if (icon != null)
            {
                float iconScale = selected ? selectedIconScale : 1f;

                icon.transform.DOKill();

                if (dur > 0f)
                    icon.transform.DOScale(iconScale, dur).SetEase(Ease.OutBack, 2f);
                else
                    icon.transform.localScale = Vector3.one * iconScale;
            }

            // Yazı sadece seçili sekmede GÖRÜNÜYOR ama yeri hep ayrılı duruyor.
            // Böylece seçili/seçili değil arasında dikeyde hiçbir şey oynamıyor;
            // değişen tek şey sekmenin genişliği ve rengi.
            TextMeshProUGUI label = tabLabels[i];

            if (label != null)
            {
                label.DOKill();

                float targetAlpha = selected ? 1f : 0f;

                if (dur > 0f) label.DOFade(targetAlpha, dur);
                else label.alpha = targetAlpha;
            }
        }
    }

    void OnDisable()
    {
        snapTween?.Kill();

        if (tabLayouts == null) return;

        for (int i = 0; i < tabLayouts.Length; i++)
        {
            if (tabLayouts[i] != null) DOTween.Kill(tabLayouts[i]);
            if (tabImages[i] != null) tabImages[i].DOKill();
            if (tabIcons[i] != null) tabIcons[i].transform.DOKill();
            if (tabLabels[i] != null) tabLabels[i].DOKill();
        }
    }
}
