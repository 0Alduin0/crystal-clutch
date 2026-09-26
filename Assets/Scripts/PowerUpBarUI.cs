using System;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Alt bardaki dört power-up butonu. Şu an sadece GÖRÜNTÜ işi yapıyor: stoğu
// yazıyor, stok bitince butonu söndürüyor ve basıldığında haber veriyor.
// Power-up'ın tahtada ne yaptığı buff sistemiyle gelecek; o sistem Used
// olayına abone olup TryUse ile stoktan düşecek.
//
// Stok SaveSystem'de duruyor - marketten alınan power-up burada anında
// görünüyor, sahne değişse de kayboluyor değil.
public class PowerUpBarUI : MonoBehaviour
{
    public static PowerUpBarUI Instance;

    [Serializable]
    public class Slot
    {
        public Button button;

        [Tooltip("Butonun sağ altındaki küçük adet yazısı")]
        public TextMeshProUGUI countText;
    }

    [Tooltip("Soldan sağa dört power-up")]
    [SerializeField] Slot[] slots = new Slot[4];

    [Tooltip("Stok bittiğinde butonun solukluğu")]
    [Range(0f, 1f)]
    [SerializeField] float emptyAlpha = 0.4f;

    [Tooltip("Hedef beklerken seçili butonun büyüme oranı - hangi power-up'ın " +
             "kullanılmak üzere olduğu belli olsun")]
    [Range(1f, 1.5f)]
    [SerializeField] float armedScale = 1.18f;

    // Buff sistemi buraya abone olacak: hangi power-up'a basıldı
    public event Action<int> Used;

    void Awake()
    {
        Instance = this;
    }

    void OnEnable()
    {
        // Sahneye dönerken market'ten alınanlar görünsün
        Refresh();
    }

    void Start()
    {
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] == null || slots[i].button == null) continue;

            // Döngü değişkenini doğrudan kapatmaya bırakmıyoruz - her
            // dinleyici kendi indeksini taşısın.
            int index = i;
            slots[i].button.onClick.AddListener(() => OnSlotClicked(index));
        }

        Refresh();
    }

    void OnSlotClicked(int index)
    {
        if (GetCount(index) <= 0) return;

        // Stoktan düşmek burada DEĞİL: power-up gerçekten kullanıldığında
        // (oyuncu hedefi seçtiğinde) düşmeli. Şimdilik sadece haber veriyoruz.
        Used?.Invoke(index);
    }

    public int GetCount(int index)
    {
        return SaveSystem.GetPowerUp(index);
    }

    public int SlotCount => slots != null ? slots.Length : 0;

    // Butonun bardaki slot indeksi, bulunamazsa -1.
    //
    // PowerUpController etkileri buton üzerinden eşliyor: Inspector'daki liste
    // sırasının bar sırasıyla aynı olma zorunluluğu böylece kalktı. Slot
    // indeksi hâlâ stok indeksi demek (SaveSystem.GetPowerUp) - butonu bara
    // koyduğun YER neyin harcanacağını belirliyor, listedeki sıran değil.
    public int IndexOf(Button button)
    {
        if (button == null || slots == null) return -1;

        for (int i = 0; i < slots.Length; i++)
            if (slots[i] != null && slots[i].button == button) return i;

        return -1;
    }

    // Stoktan bir tane düşer. Yoksa false döner - çağıran taraf power-up'ı
    // uygulamadan vazgeçsin.
    public bool TryUse(int index)
    {
        if (!SaveSystem.SpendPowerUp(index)) return false;

        WriteSlot(index);
        return true;
    }

    public void Refresh()
    {
        if (slots == null) return;

        for (int i = 0; i < slots.Length; i++) WriteSlot(i);
    }

    // Hedef bekleyen slot büyüsün, diğerleri normale dönsün. -1 = hiçbiri.
    // PowerUpController çağırıyor; bar hangi power-up'ın ne yaptığını bilmiyor,
    // sadece hangisinin seçili olduğunu gösteriyor.
    public void SetArmed(int index)
    {
        if (slots == null) return;

        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] == null || slots[i].button == null) continue;

            Transform t = slots[i].button.transform;
            float target = i == index ? armedScale : 1f;

            t.DOKill();

            // Ölçek zaten yerindeyse tween açmıyoruz: her seçim değişiminde
            // dört buton için tween kurmak boşuna GC.
            if (Mathf.Approximately(t.localScale.x, target))
            {
                t.localScale = Vector3.one * target;
                continue;
            }

            t.DOScale(target, 0.18f).SetEase(Ease.OutBack);
        }
    }

    void OnDisable()
    {
        if (slots == null) return;

        foreach (Slot slot in slots)
            if (slot != null && slot.button != null) slot.button.transform.DOKill();
    }

    void WriteSlot(int index)
    {
        Slot slot = slots[index];
        if (slot == null) return;

        int count = SaveSystem.GetPowerUp(index);

        if (slot.countText != null) slot.countText.text = count.ToString();

        if (slot.button == null) return;

        // interactable yerine sadece solduruyoruz ve tıklamayı elle
        // yutuyoruz: Button'ın disabled rengi sprite'ın kendi rengini
        // eziyor, ikonlar tanınmaz hale geliyor.
        if (slot.button.image != null)
        {
            Color color = slot.button.image.color;
            color.a = count > 0 ? 1f : emptyAlpha;
            slot.button.image.color = color;
        }

        if (slot.countText != null)
        {
            Color color = slot.countText.color;
            color.a = count > 0 ? 1f : emptyAlpha;
            slot.countText.color = color;
        }
    }
}
