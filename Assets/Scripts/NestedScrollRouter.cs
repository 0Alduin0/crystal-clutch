using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Dikey bir listeyi yatay sayfa kaydırıcının (PageView) içine koyunca ikisi
// birbirini yiyor: listeye parmak basıp yana kaydırdığında sayfa değişmiyor,
// çünkü içteki ScrollRect sürüklemeyi kapıyor.
//
// Bu script sürüklemenin İLK yönüne bakıp kimin işleyeceğine karar veriyor:
// dikeyse içteki liste kaydırıyor, yataysa olay üstteki sayfa kaydırıcıya
// devrediliyor. Karar sürükleme başında bir kez veriliyor, ortasında el
// değiştirmiyor - yoksa parmak titrediğinde sayfa da liste de zıplar.
//
// KURULUM: içteki ScrollRect'in CONTENT'ine ekle. Viewport'a ya da
// ScrollRect'in kendisine değil - EventSystem sürüklemeyi önce buradan
// geçirsin ki ScrollRect'ler kendiliğinden davranmasın.
//
// Sürükleme ancak bu objenin (ya da çocuklarının) üstünden başlarsa buraya
// geliyor. Liste kısaysa (liderlik tablosunda birkaç oyuncu) altında kalan
// boşlukta başlayan sürükleme doğrudan içteki ScrollRect'e gidiyordu: yana
// kaydırmak sayfayı değiştirmiyordu, liste de kaymıyordu. O yüzden Content
// en az görünür alan kadar uzatılıyor ve görünmez bir dokunma yüzeyi taşıyor.
public class NestedScrollRouter : MonoBehaviour,
    IBeginDragHandler, IDragHandler, IEndDragHandler
{
    ScrollRect inner;
    ScrollRect outer;
    bool routeToOuter;

    LayoutElement fill;
    RectTransform viewport;

    void Awake()
    {
        inner = GetComponentInParent<ScrollRect>();

        if (inner != null && inner.transform.parent != null)
            outer = inner.transform.parent.GetComponentInParent<ScrollRect>();

        CoverViewport();
    }

    // Content'e görünmez bir Image (dokunma yüzeyi) ve görünür alan kadar
    // asgari yükseklik. Kartlar çocuk olduğu için dokunuşu yine önce onlar alıyor.
    void CoverViewport()
    {
        if (inner == null) return;

        viewport = inner.viewport != null ? inner.viewport : (RectTransform)inner.transform;

        if (GetComponent<Graphic>() == null)
        {
            Image surface = gameObject.AddComponent<Image>();
            surface.color = new Color(0f, 0f, 0f, 0f);
            surface.raycastTarget = true;
        }

        fill = GetComponent<LayoutElement>();
        if (fill == null) fill = gameObject.AddComponent<LayoutElement>();
    }

    // Görünür alan ekran dönünce ya da SafeArea değişince değişebilir
    void LateUpdate()
    {
        if (fill == null || viewport == null) return;

        float h = viewport.rect.height;
        if (Mathf.Abs(fill.minHeight - h) > 0.5f) fill.minHeight = h;
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        // eventData.delta sürükleme başında çok küçük olabiliyor; parmağın
        // bastığı noktadan bu yana gittiği yol daha güvenilir bir yön verir.
        Vector2 travel = eventData.position - eventData.pressPosition;

        routeToOuter = outer != null && Mathf.Abs(travel.x) > Mathf.Abs(travel.y);

        if (routeToOuter)
        {
            // ExecuteEvents ile gönderiyoruz: o objede ScrollRect'in yanında
            // PageView de dinliyor, ikisinin de haberi olsun.
            ExecuteEvents.Execute(outer.gameObject, eventData, ExecuteEvents.beginDragHandler);
            return;
        }

        if (inner != null) inner.OnBeginDrag(eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (routeToOuter)
        {
            ExecuteEvents.Execute(outer.gameObject, eventData, ExecuteEvents.dragHandler);
            return;
        }

        if (inner != null) inner.OnDrag(eventData);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (routeToOuter)
        {
            ExecuteEvents.Execute(outer.gameObject, eventData, ExecuteEvents.endDragHandler);
            routeToOuter = false;
            return;
        }

        if (inner != null) inner.OnEndDrag(eventData);
    }
}
