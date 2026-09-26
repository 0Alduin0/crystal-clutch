using UnityEngine;
using UnityEngine.UI;

// SafeArea'nın kardeşi ama işi farklı: objeyi KÜÇÜLTMEZ, üstündeki Layout
// Group'un padding'ine güvenli alan payını ekler.
//
// Neden lazım: alt sekme çubuğunu SafeArea'nın içine koyarsan çubuğun tamamı
// yukarı kayar ve altında home göstergesi kadar boşluk kalır. Oysa istenen,
// Royal Match'teki gibi ARKA PLANIN ekranın en dibine kadar gitmesi ama
// İÇERİĞİN (ikonlar, yazılar) göstergenin üstünde durması. Bu script tam
// olarak onu yapıyor.
//
// KULLANIM: Layout Group'u olan objeye ekle. Alt sekme çubuğu için:
//   TabBar  [Image, HorizontalLayoutGroup, SafeAreaPadding]   <- alta yapışık,
//   ├ Tab_0                                                      SafeArea DIŞINDA
//   ├ Tab_1
//   └ Tab_2
[RequireComponent(typeof(LayoutGroup))]
[DisallowMultipleComponent]
public class SafeAreaPadding : MonoBehaviour
{
    [Header("Hangi kenara pay eklensin")]
    [SerializeField] bool applyTop = false;
    [SerializeField] bool applyBottom = true;
    [SerializeField] bool applyLeft = false;
    [SerializeField] bool applyRight = false;

    [Header("Ek Boşluk")]
    [Tooltip("Güvenli alan payının ÜSTÜNE eklenecek sabit boşluk. Çentiksiz " +
             "cihazlarda güvenli alan payı 0 olur, içeriğin ekranın dibine " +
             "yapışmaması için buraya biraz değer vermek iyi olur.")]
    [SerializeField] int extraTop = 0;
    [SerializeField] int extraBottom = 20;
    [SerializeField] int extraLeft = 0;
    [SerializeField] int extraRight = 0;

    LayoutGroup group;
    Canvas canvas;
    Rect lastSafeArea;
    Vector2Int lastScreen;

    // scaleFactor'ı da izliyoruz. Awake'te CanvasScaler henüz çalışmamış
    // olabiliyor ve scaleFactor 1 dönüyor; o an bölme yapılmayınca padding
    // kat kat büyük çıkıp içeriği yukarı itiyordu. Awake sırası sabit
    // olmadığı için de sekmelerden yalnızca biri bozuluyordu.
    float lastScale = -1f;

    void Awake()
    {
        group = GetComponent<LayoutGroup>();
        canvas = GetComponentInParent<Canvas>();

        Apply();
    }

    void Update()
    {
        if (Screen.safeArea == lastSafeArea &&
            Screen.width == lastScreen.x &&
            Screen.height == lastScreen.y &&
            Mathf.Approximately(CurrentScale, lastScale))
            return;

        Apply();
    }

    float CurrentScale
    {
        get
        {
            if (canvas == null) canvas = GetComponentInParent<Canvas>();

            float scale = canvas != null ? canvas.scaleFactor : 1f;

            return scale > 0f ? scale : 1f;
        }
    }

    void Apply()
    {
        if (group == null) group = GetComponent<LayoutGroup>();
        if (group == null || Screen.width <= 0 || Screen.height <= 0) return;

        lastSafeArea = Screen.safeArea;
        lastScreen = new Vector2Int(Screen.width, Screen.height);

        // safeArea PİKSEL cinsinden, padding ise canvas birimi. CanvasScaler
        // devrede olduğu için ikisi aynı şey değil - scaleFactor ile çeviriyoruz.
        float scale = CurrentScale;
        lastScale = scale;

        Rect area = Screen.safeArea;

        int left   = applyLeft   ? Mathf.RoundToInt(area.xMin / scale) : 0;
        int right  = applyRight  ? Mathf.RoundToInt((Screen.width - area.xMax) / scale) : 0;
        int bottom = applyBottom ? Mathf.RoundToInt(area.yMin / scale) : 0;
        int top    = applyTop    ? Mathf.RoundToInt((Screen.height - area.yMax) / scale) : 0;

        group.padding = new RectOffset(
            left + extraLeft,
            right + extraRight,
            top + extraTop,
            bottom + extraBottom);

        LayoutRebuilder.MarkLayoutForRebuild((RectTransform)transform);
    }
}
