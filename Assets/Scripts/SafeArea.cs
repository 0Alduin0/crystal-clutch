using UnityEngine;

// Üstüne konduğu RectTransform'u cihazın güvenli alanına oturtur: çentik,
// durum çubuğu, alttaki home göstergesi gibi ekranın "yenen" kısımlarının
// dışında kalır.
//
// KULLANIM: Canvas'ın DOĞRUDAN çocuğu olan, tam ekran stretch bir boş obje
// oluştur, bu script'i ona ekle, kaymaması gereken UI'ı (TopBar, TabBar)
// içine taşı. Kendi anchor'ları bu kaba göre çalışmaya devam eder.
//
//   Canvas
//   ├ Background        <- safe area DIŞINDA, kenardan kenara
//   ├ Pager             <- safe area DIŞINDA, tam ekran
//   └ SafeArea  [SafeArea]
//      ├ TopBar
//      └ TabBar
//
// Script objenin anchor'larını her karede kendisi yazıyor - bu objenin
// RectTransform'unu elle ayarlamaya çalışma, ezer.
[RequireComponent(typeof(RectTransform))]
[DisallowMultipleComponent]
public class SafeArea : MonoBehaviour
{
    [Header("Hangi kenarlar kısılsın")]
    [Tooltip("Çentik / durum çubuğu - dikey oyunda neredeyse her zaman açık olmalı")]
    [SerializeField] bool applyTop = true;

    [Tooltip("Home göstergesi (iPhone) / gezinme çubuğu (Android)")]
    [SerializeField] bool applyBottom = true;

    [Tooltip("Yan kenarlar. Dikey oyunda genelde gereksiz; yatay oyunda ya da " +
             "kavisli ekranlarda açmak isteyebilirsin.")]
    [SerializeField] bool applyLeft = false;
    [SerializeField] bool applyRight = false;

    RectTransform rect;
    Rect lastSafeArea;
    Vector2Int lastScreen;

    void Awake()
    {
        rect = GetComponent<RectTransform>();
        Apply();
    }

    void Update()
    {
        // Ekran döndürme, katlanabilir cihazın açılması, editörde çözünürlük
        // değiştirme - hepsinde safeArea değişiyor ve bunun bir olayı yok,
        // yoklamak zorundayız. Karşılaştırma bedava, iş sadece değişince yapılıyor.
        if (Screen.safeArea == lastSafeArea &&
            Screen.width == lastScreen.x &&
            Screen.height == lastScreen.y)
            return;

        Apply();
    }

    void Apply()
    {
        if (rect == null) rect = GetComponent<RectTransform>();
        if (Screen.width <= 0 || Screen.height <= 0) return;

        Rect area = Screen.safeArea;

        lastSafeArea = area;
        lastScreen = new Vector2Int(Screen.width, Screen.height);

        // safeArea piksel cinsinden, anchor 0-1 aralığında - çeviriyoruz.
        Vector2 min = area.position;
        Vector2 max = area.position + area.size;

        min.x /= Screen.width;
        min.y /= Screen.height;
        max.x /= Screen.width;
        max.y /= Screen.height;

        // Kapatılan kenarlarda ekranın ucuna kadar git
        if (!applyLeft) min.x = 0f;
        if (!applyBottom) min.y = 0f;
        if (!applyRight) max.x = 1f;
        if (!applyTop) max.y = 1f;

        rect.anchorMin = min;
        rect.anchorMax = max;

        // Anchor'lara tam otur - araya pay bırakma
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}
