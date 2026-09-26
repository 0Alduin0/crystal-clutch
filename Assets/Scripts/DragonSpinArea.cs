using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.UI;

// Ekranda parmakla sağa sola sürükleyince ejderhayı kendi ekseninde çevirir,
// 360 derece. Hızlı kaydırıp bırakınca dönmeye devam edip yavaşça durur.
// İki parmakla pinch ve fare tekerleği yakınlaştırır (kamerayı DragonZoom sürüyor).
//
// Arayüzün EN ALTINDAKİ şeffaf, tam ekran bir Image'a konur: butonlar ve alt
// kart üstte kaldığı için kendi dokunuşlarını alıyor, boş yerde yapılan
// sürükleme buraya düşüyor. Olay sistemi üzerinden çalıştığı için dokunmatikte
// de editörde fareyle de aynı - projede eski Input API'si kapalı. Olay sistemi
// her parmağı ayrı pointer olarak veriyor, pinch de buradan çıkıyor.
[RequireComponent(typeof(Graphic))]
public class DragonSpinArea : MonoBehaviour, IPointerDownHandler, IPointerUpHandler,
    IBeginDragHandler, IDragHandler, IEndDragHandler, IScrollHandler
{
    [SerializeField] DragonView view;
    [SerializeField] DragonZoom zoom;

    [Tooltip("Ekranın bir ucundan öbür ucuna sürükleyince kaç derece dönsün")]
    [SerializeField] float degreesPerScreenWidth = 360f;

    [Tooltip("Parmak bu kadar saniyedir kıpırdamıyorsa bırakınca dönmesin - " +
             "oyuncu ejderhayı bir açıda durdurmak istiyor")]
    [SerializeField] float holdStillTime = 0.08f;

    float velocity;
    float lastDragTime;
    bool spinning;

    // Ekrana değen parmaklar (pointerId -> konum). İki parmak değince pinch
    // başlıyor ve döndürme duruyor. Biri kalkınca kalan parmak ejderhayı
    // birden çevirmesin diye hepsi kalkana kadar döndürme kapalı.
    readonly Dictionary<int, Vector2> pointers = new Dictionary<int, Vector2>();
    bool pinching;
    float pinchDistance;

    public void OnPointerDown(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left) return;

        pointers[eventData.pointerId] = eventData.position;
        if (pointers.Count < 2) return;

        pinching = true;
        pinchDistance = PinchDistance();
        StopSpin(0f);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        // Farenin bütün tuşları aynı pointerId'de: sağ tık bırakılınca sol tuşun kaydı silinmesin
        if (eventData.button != PointerEventData.InputButton.Left) return;

        pointers.Remove(eventData.pointerId);

        // Üç parmaktan biri kalktıysa kalan ikisinin mesafesinden devam
        if (pointers.Count >= 2) pinchDistance = PinchDistance();
        if (pointers.Count == 0) pinching = false;
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (pinching || view == null) return;

        velocity = 0f;
        lastDragTime = Time.unscaledTime;
        spinning = true;

        view.BeginSpin();
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (pointers.ContainsKey(eventData.pointerId)) pointers[eventData.pointerId] = eventData.position;

        if (pinching)
        {
            Pinch();
            return;
        }

        if (!spinning) return;

        // Parmak sağa gidince ejderhanın yüzü de sağa dönsün. Kamera -Z'den
        // bakıyor; Y ekseninde pozitif dönüş yüzü sola çeviriyor, o yüzden eksi.
        float degrees = -eventData.delta.x / Mathf.Max(1, Screen.width) * degreesPerScreenWidth;

        view.Spin(degrees);

        // Bırakırken fırlatma hızı: son hareketlerin yumuşatılmış hızı
        float dt = Mathf.Max(Time.unscaledTime - lastDragTime, 0.001f);
        velocity = Mathf.Lerp(velocity, degrees / dt, 0.5f);
        lastDragTime = Time.unscaledTime;
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (!spinning) return;

        bool heldStill = Time.unscaledTime - lastDragTime > holdStillTime;
        StopSpin(heldStill ? 0f : velocity);
    }

    public void OnScroll(PointerEventData eventData)
    {
        if (zoom != null) zoom.Scroll(eventData.scrollDelta.y);
    }

    void Pinch()
    {
        if (pointers.Count < 2) return;

        float distance = PinchDistance();

        if (zoom != null && pinchDistance > 1f) zoom.ZoomBy(distance / pinchDistance);

        pinchDistance = distance;
    }

    void StopSpin(float releaseVelocity)
    {
        if (!spinning) return;

        spinning = false;
        if (view != null) view.EndSpin(releaseVelocity);
    }

    // İlk iki parmak arası piksel mesafe
    float PinchDistance()
    {
        Vector2 first = default;
        int i = 0;

        foreach (Vector2 p in pointers.Values)
        {
            if (i++ == 0) first = p;
            else return Vector2.Distance(first, p);
        }

        return 0f;
    }

    // Parmak kalkma olayı bir şekilde gelmezse (bildirim perdesi, sistem hareketi)
    // kayıtta takılı parmak kalır: pinch açık kalıp döndürmeyi kalıcı kilitler.
    // Gerçekte hiçbir şey basılı değilse kaydı sıfırla.
    void Update()
    {
        if (pointers.Count == 0 || AnyPointerPressed()) return;

        pointers.Clear();
        pinching = false;
    }

    static bool AnyPointerPressed()
    {
        foreach (InputDevice device in InputSystem.devices)
        {
            if (device is Touchscreen touchscreen)
            {
                // Touchscreen.press sadece ilk parmak; ikinci parmak da sayılsın
                foreach (TouchControl touch in touchscreen.touches)
                    if (touch.press.isPressed) return true;
            }
            else if (device is Pointer pointer && pointer.press.isPressed)
            {
                return true;
            }
        }

        return false;
    }

    // Uygulama arkaya atılınca parmak kalkma olayı gelmeyebilir; takılı kalan
    // parmak döndürmeyi kilitlemesin
    void OnApplicationPause(bool paused)
    {
        if (!paused) return;

        pointers.Clear();
        pinching = false;
        StopSpin(0f);
    }

    void OnDisable()
    {
        pointers.Clear();
        pinching = false;
        StopSpin(0f);
    }
}
