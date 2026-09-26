using System;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Marketin en üstündeki satır: günlük sürpriz. Alttaki satırlarla aynı düzen -
// üç buton - ama ücretsiz; power-up da adedi de (1x-5x) rastgele. Kutuda ne
// olduğu baştan görünüyor, sürpriz kutusu yok: basınca direkt stoğa giriyor,
// butonun altında fiyatın yerinde duran "ÜCRETSİZ" yazısı 24 saatlik geri
// sayıma dönüyor. Sayım bitince kutuya yeni rastgele power-up ve adet geliyor.
//
// Her kutunun sayacı ayrı. Durum SaveSystem'de (bkz. GÜNLÜK SÜRPRİZ).
public class DailyGiftUI : MonoBehaviour
{
    [Serializable]
    public class GiftSlot
    {
        public Button button;

        [Tooltip("Kutudaki power-up'ın resmi")]
        public Image icon;

        [Tooltip("Kutudaki adet: '3x' - opsiyonel")]
        public TextMeshProUGUI amountText;

        [Tooltip("Butonun altındaki yazı: alınmamışsa 'ÜCRETSİZ', alınmışsa geri sayım")]
        public TextMeshProUGUI countdownText;
    }

    [Tooltip("Üç kutu, soldan sağa. Adetleri rastgele (1x-5x)")]
    [SerializeField] GiftSlot[] slots = new GiftSlot[SaveSystem.DailyGiftSlots];

    [Header("Görünüm")]
    [Tooltip("Alınmış kutudaki resmin solukluğu")]
    [Range(0f, 1f)]
    [SerializeField] float claimedAlpha = 0.55f;

    [Tooltip("Alınmamış kutunun altında, diğer satırlardaki fiyatın yerinde")]
    [SerializeField] string freeLabel = "ÜCRETSİZ";

    // İkonlar marketten geliyor; alınan power-up'ın satırındaki "elde kaç
    // tane var" yazısı da onun Refresh'iyle güncelleniyor.
    MarketUI market;

    // Refresh'te okunan durum. Update her karede sadece sayacı işleyen
    // kutulara bakıyor, geri sayımı da saniye değişince yazıyor.
    //
    // Kutunun açılacağı an da Refresh'te bir kez hesaplanıyor: kalan süreyi
    // her karede SaveSystem'den okumak Android'de kare başına birkaç PlayerPrefs
    // (JNI) çağrısı ve çöp string demekti - market sayfası kapalıyken bile.
    bool[] claimed;
    int[] shownSeconds;
    DateTime[] readyAt;

    void Awake()
    {
        market = GetComponentInParent<MarketUI>();

        claimed = new bool[slots.Length];
        shownSeconds = new int[slots.Length];
        readyAt = new DateTime[slots.Length];
    }

    void OnEnable()
    {
        Refresh();
    }

    void Start()
    {
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] == null || slots[i].button == null) continue;

            // Döngü değişkenini doğrudan kapatmaya bırakmıyoruz - her
            // dinleyici kendi kutusunu taşısın.
            int index = i;
            slots[i].button.onClick.AddListener(() => Claim(index));
        }
    }

    void Update()
    {
        for (int i = 0; i < claimed.Length; i++)
        {
            if (!claimed[i]) continue;

            TimeSpan left = readyAt[i] - DateTime.UtcNow;

            // 24 saat doldu: kutuya yeni power-up geldi
            if (left <= TimeSpan.Zero)
            {
                Refresh();
                return;
            }

            WriteCountdown(i, left);
        }
    }

    // ==================== ALMA ====================

    void Claim(int slot)
    {
        if (!SaveSystem.ClaimDailyGift(slot)) return;

        Button button = slots[slot].button;
        button.transform.DOComplete();
        button.transform.DOPunchScale(Vector3.one * 0.2f, 0.35f, 5);

        Refresh();

        if (market != null) market.Refresh();
    }

    // ==================== GÖRÜNÜM ====================

    public void Refresh()
    {
        for (int i = 0; i < claimed.Length; i++)
        {
            GiftSlot slot = slots[i];
            claimed[i] = false;

            if (slot == null) continue;

            claimed[i] = SaveSystem.IsDailyGiftClaimed(i);
            shownSeconds[i] = -1;

            if (slot.icon != null)
            {
                Sprite sprite = market != null ? market.GetPowerUpIcon(SaveSystem.GetDailyGiftItem(i)) : null;
                if (sprite != null) slot.icon.sprite = sprite;

                Color color = slot.icon.color;
                color.a = claimed[i] ? claimedAlpha : 1f;
                slot.icon.color = color;
            }

            if (slot.amountText != null)
                slot.amountText.text = SaveSystem.GetDailyGiftAmount(i) + "x";

            if (claimed[i])
            {
                TimeSpan left = SaveSystem.GetTimeToDailyGift(i);
                readyAt[i] = DateTime.UtcNow + left;
                WriteCountdown(i, left);
            }
            else if (slot.countdownText != null) slot.countdownText.text = freeLabel;
        }
    }

    void WriteCountdown(int slot, TimeSpan left)
    {
        TextMeshProUGUI text = slots[slot].countdownText;
        if (text == null) return;

        // Yukarı yuvarla: alır almaz 23:59:59 değil 24:00:00 görünsün
        int seconds = Mathf.CeilToInt((float)left.TotalSeconds);

        if (seconds == shownSeconds[slot]) return;

        shownSeconds[slot] = seconds;
        text.text = Format(seconds);
    }

    static string Format(int seconds)
    {
        if (seconds < 0) seconds = 0;

        return string.Format("{0:00}:{1:00}:{2:00}", seconds / 3600, seconds / 60 % 60, seconds % 60);
    }

    void OnDisable()
    {
        foreach (GiftSlot slot in slots)
            if (slot != null && slot.button != null) slot.button.transform.DOKill();
    }
}
