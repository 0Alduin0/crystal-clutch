using UnityEngine;

// Yuvada yakınlaştırma: iki parmakla pinch, editörde fare tekerleği. Girdiyi
// DragonSpinArea veriyor (döndürmeyle aynı tam ekran alan), bu sınıf sadece
// kamerayı sürüyor. Nest sahnesinin kamerasına konur.
//
// Kamera ileri taşınmıyor, görüş açısı (FOV) daralıyor: DragonView aşama
// atlarken kamerayı pozisyonundan sallıyor, pozisyona iki yer yazarsa sarsıntı
// ezilir. Arka plan Screen Space - Camera tuvali, FOV'dan etkilenmiyor.
//
// Yakınlaştıkça kamera ejderhanın ortasına döner: kadraj büyük ejderhalara göre
// ayarlı, yumurta ekranın ortasının altında kalıyor - düz FOV kısılsa kadrajdan
// çıkar. Sınır da modele göre: en yakında ejderha ekran yüksekliğinin maxFill
// kadarını kaplıyor, minik yumurtaya dev ejderhadan daha çok yaklaşılabiliyor.
[RequireComponent(typeof(Camera))]
public class DragonZoom : MonoBehaviour
{
    [SerializeField] DragonView view;

    [Tooltip("En yakında ejderha ekran yüksekliğinin ne kadarını kaplasın")]
    [SerializeField, Range(0.2f, 1f)] float maxFill = 0.75f;

    [Tooltip("Model ne kadar büyük olursa olsun en az bu kadar yakınlaşılabilsin")]
    [SerializeField] float minMaxZoom = 1.5f;

    [Tooltip("Fare tekerleğinin bir tıkı ne kadar yakınlaştırsın")]
    [SerializeField] float scrollStep = 1.15f;

    [SerializeField] float smoothTime = 0.1f;

    Camera cam;
    float baseFov;
    Quaternion baseRotation;

    // 1 = kurulumdaki kadraj. zoom hedef, shownZoom ekrandaki - yumuşakça yetişiyor.
    float zoom = 1f;
    float shownZoom = 1f;
    float zoomVelocity;
    float maxZoom;

    void Awake()
    {
        cam = GetComponent<Camera>();
        baseFov = cam.fieldOfView;
        baseRotation = transform.rotation;
        maxZoom = minMaxZoom;
    }

    // factor > 1 yakınlaştırır. Pinch: parmaklar arası mesafenin oranı.
    public void ZoomBy(float factor)
    {
        zoom = Mathf.Clamp(zoom * factor, 1f, maxZoom);
    }

    // Miktara değil yöne bakılıyor: bir tıkın değeri platforma göre 1 de 120 de gelebiliyor
    public void Scroll(float delta)
    {
        if (delta != 0f) ZoomBy(delta > 0f ? scrollStep : 1f / scrollStep);
    }

    void LateUpdate()
    {
        Vector3 focus = default;
        float height = 0f;
        bool hasFocus = view != null && view.TryGetFocus(out focus, out height);

        // Aşama atlayınca ejderha büyüyor: yeni sınır daha düşükse sığacak kadar geri çekil
        maxZoom = hasFocus ? MaxZoomFor(focus, height) : minMaxZoom;
        zoom = Mathf.Min(zoom, maxZoom);

        shownZoom = Mathf.SmoothDamp(shownZoom, zoom, ref zoomVelocity, smoothTime, Mathf.Infinity, Time.unscaledDeltaTime);

        float halfTan = Mathf.Tan(baseFov * 0.5f * Mathf.Deg2Rad) / shownZoom;
        cam.fieldOfView = 2f * Mathf.Atan(halfTan) * Mathf.Rad2Deg;

        // 1x'te kurulumdaki açı, yakınlaştıkça ejderhanın ortası (2x'te yarı yolda)
        Quaternion aim = hasFocus ? Quaternion.LookRotation(focus - transform.position) : baseRotation;
        transform.rotation = Quaternion.Slerp(baseRotation, aim, 1f - 1f / shownZoom);
    }

    // Sadece boya bakılıyor: genişlik ejderha dönerken değişir, sınır titremesin
    float MaxZoomFor(Vector3 focus, float height)
    {
        if (height < 0.001f) return minMaxZoom;

        float distance = Vector3.Distance(transform.position, focus);
        float visible = 2f * distance * Mathf.Tan(baseFov * 0.5f * Mathf.Deg2Rad);

        return Mathf.Max(minMaxZoom, maxFill * visible / height);
    }
}
