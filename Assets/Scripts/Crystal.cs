using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

// Tahtada doğabilecek buff'lar. Sayılar SABİT: prefab ve sahnelerde
// serileşmiş değerler var, sırayı değiştirirsek eski kayıtlar kayar.
public enum PowerType
{
    None = 0,       // normal kristal
    Cross = 1,      // 4'lü eşleşmeden doğar - takas edildiği karenin SATIRI + SÜTUNU
    ColorBomb = 2,  // 5'li DÜZ eşleşmeden doğar - multicolor
    Bomb = 3        // L ya da T şeklinden doğar - 3x3 alan
}

public class Crystal : MonoBehaviour
{
    // Oyun sırasında dolan durum - prefab'ta ayarlanacak bir şey değil,
    // Inspector'da görünmesin. Kristaller hep prefab'tan doğuyor, kopyalanmıyor.
    [System.NonSerialized] public int X;
    [System.NonSerialized] public int Y;
    [System.NonSerialized] public PowerType Power = PowerType.None;
    [System.NonSerialized] public CrystalType BombTargetType;

    public CrystalType Type;

    // Sprite'ı çizen renderer ('Visual' çocuk objesinde) ve normal hâlin
    // sprite'ı - ikisi de Awake'te bulunuyor
    SpriteRenderer visual;
    Sprite normalSprite;

    [Header("4'LÜ - bu kristale özel 4-way")]
    [Tooltip("Takas edildiği karenin hem satırını hem sütununu süpürür. " +
             "Yönü yok, tek sprite yeter.")]
    [SerializeField] Sprite crossSprite;

    [Header("L / T - bu kristale özel bomba")]
    [Tooltip("Takas edilince 3x3 alanı patlatır")]
    [SerializeField] Sprite bombSprite;

    [Header("5'Lİ - tüm kristallerde ORTAK renk bombası")]
    [Tooltip("Renk bombası tür bağımsız bir güç - Candy Crush'taki çikolata topu " +
             "gibi. Bilerek her kristalde aynı sprite.")]
    [SerializeField] Sprite colorBombSprite;

    [Header("Patlama Rengi")]
    [Tooltip("Kristal patlarken uçan parçaların rengi. Alpha'yı 0 bırakırsan " +
             "kristal türüne göre otomatik seçilir - prefab başına ayar gerekmez.")]
    [SerializeField] Color popColor = new Color(1f, 1f, 1f, 0f);

    [Header("Power-up Görsel Boyutu")]
    [Tooltip("4-way sprite'ı kristalyle aynı boyuttaysa 1 bırak")]
    [SerializeField] float crossVisualScale = 1f;

    [Tooltip("Bomba sprite'ının ölçeği")]
    [SerializeField] float bombVisualScale = 1f;

    [Tooltip("Ortak renk bombası sprite'ının ölçeği. Bomba görseli kristallerle " +
             "aynı tuvalde (200x200, PPU 100) çizildiği için 1 doğrusu. " +
             "Eski 5 Bomb.png 112x131'di ve 1.6 ile büyütülmesi gerekiyordu.")]
    [SerializeField] float colorBombVisualScale = 1f;

    [Header("İpucu")]
    [Tooltip("İpucu yanıp sönerken inilecek en düşük saydamlık")]
    [Range(0f, 0.8f)]
    [SerializeField] float hintFadeAlpha = 0.35f;

    [Header("Power-up Vurgusu")]
    [SerializeField] bool pulseWhenPowered = true;
    [SerializeField] float pulseScale = 1.08f;
    [SerializeField] float pulseSpeed = 0.7f;

    [Header("Squash & Stretch")]
    [Tooltip("Yere değince yassılsın")]
    [SerializeField] bool useSquashStretch = true;

    [Tooltip("Yere değince yassılma miktarı")]
    [Range(0f, 0.4f)]
    [SerializeField] float squashAmount = 0.22f;

    [Tooltip("Yere iniş toparlanma süresi. Toplam oturma = bu değerin 3 katı, " +
             "düşüş süresinden uzun olmamalı.")]
    [SerializeField] float squashDuration = 0.06f;

    Tween pulseTween;

    // İpucu animasyonu ayrı tutuluyor: nabız (power-up) ve hareket
    // tween'lerinden bağımsız başlayıp bitiyor.
    Tween hintTween;
    Vector3 hintBasePos;
    bool hinting;

    // Bir kristalde aynı anda tek bir hareket animasyonu çalışır: düşme, takas,
    // patlama, doğuş veya sarsıntı. transform.DOKill() yerine bu referansı kill
    // ediyoruz - DOKill tüm aktif tween listesini baştan sona tarıyor ve cascade
    // sırasında kristal başına bu tarama ciddi maliyet çıkarıyordu.
    // (DOTweenSettings'te defaultRecyclable kapalı, o yüzden bitmiş bir tween'e
    //  Kill() demek zararsız - başkasının tween'ini öldürme riski yok.)
    Tween moveTween;

    Vector3 rootBaseScale;
    Vector3 visualBaseScale;

    // Renderer normalde 'Visual' çocuk objesinde; kök objede olan bir prefab
    // gelirse ölçek çarpanı köke uygulanmalı, o yüzden ayırt ediyoruz.
    bool spriteIsOnRoot;

    void Awake()
    {
        // includeInactive: havuz kristali kapatmadan önce Awake çalışıyor ama
        // yine de garanti olsun - kapalı bir hiyerarşide de bulsun.
        if (visual == null) visual = GetComponentInChildren<SpriteRenderer>(true);

        rootBaseScale = transform.localScale;

        if (visual != null)
        {
            spriteIsOnRoot = (visual.transform == transform);
            visualBaseScale = visual.transform.localScale;

            if (normalSprite == null)
                normalSprite = visual.sprite;
        }
    }

    // Patlama parçalarının rengi.
    // Sprite'tan okumak texture'da Read/Write açık olmasını gerektirir, o yüzden
    // tablo tutuyoruz. Prefab'tan override edilebilir.
    public Color PopColor
    {
        get
        {
            if (popColor.a > 0.001f) return popColor;

            switch (Type)
            {
                case CrystalType.Kizil: return new Color(0.93f, 0.26f, 0.22f);
                case CrystalType.Mor:   return new Color(0.63f, 0.36f, 0.84f);
                case CrystalType.Mavi:  return new Color(0.35f, 0.72f, 0.95f);
                case CrystalType.Yesil: return new Color(0.25f, 0.78f, 0.35f);
            }

            return Color.white;
        }
    }

    // Power-up görselinin ölçek çarpanı. Her buff'ın sprite'ı ayrı tuvalde
    // çizilmiş olabiliyor, o yüzden üçü ayrı ayarlanıyor.
    float CurrentPowerScale
    {
        get
        {
            switch (Power)
            {
                case PowerType.Cross:     return crossVisualScale;
                case PowerType.Bomb:      return bombVisualScale;
                case PowerType.ColorBomb: return colorBombVisualScale;
            }

            return 1f;
        }
    }

    // Nabzın etrafında salınacağı temel boyut
    Vector3 CurrentRootBase
    {
        get
        {
            if (spriteIsOnRoot) return rootBaseScale * CurrentPowerScale;
            return rootBaseScale;
        }
    }

    void KillMove()
    {
        moveTween?.Kill();
        moveTween = null;

        // İpucu da bir hareket: düşme/takas/patlama başlarken kristal yerine
        // dönmeli, yoksa zıplamanın ortasında kalmış bir konumdan yola çıkar.
        if (hinting) StopHint();
    }

    void KillPulse()
    {
        pulseTween?.Kill();
        pulseTween = null;
    }

    // ==================== HAVUZ ====================

    public void ResetCrystal()
    {
        Power = PowerType.None;

        KillPulse();
        KillMove();

        transform.localScale = rootBaseScale;
        transform.rotation = Quaternion.identity;

        if (visual == null) visual = GetComponentInChildren<SpriteRenderer>(true);

        if (visual != null)
        {
            // visual.transform'a hiçbir yerde tween atanmıyor, sadece localScale
            // doğrudan yazılıyor - bu yüzden burada DOKill'e gerek yok.
            if (!spriteIsOnRoot)
                visual.transform.localScale = visualBaseScale;

            if (normalSprite != null) visual.sprite = normalSprite;
            visual.color = Color.white;

            // Düşüş yarıda kesilmiş olabilir (patlama, tahta yenileme): kristal
            // görünmez hâlde havuza döner ve bir sonraki kullanımda hiç
            // belirmezdi. Havuza dönüşte her zaman geri aç.
            visual.enabled = true;
        }
    }

    // ==================== POWER-UP ====================

    // Buff'ların hiçbirinin artık yönü yok: 4-way zaten iki ekseni birden
    // süpürüyor, bomba 3x3, renk bombası tüm tahta. Eski FireHorizontal
    // plumbing'i bu yüzden kaldırıldı.
    public void SetPower(PowerType power)
    {
        Power = power;

        ApplyPowerVisual();
        StartPulse();
    }

    Sprite ResolvePowerSprite()
    {
        switch (Power)
        {
            case PowerType.Cross:     return crossSprite;
            case PowerType.Bomb:      return bombSprite;
            case PowerType.ColorBomb: return colorBombSprite;
        }

        return normalSprite;
    }

    void ApplyPowerVisual()
    {
        if (visual == null) visual = GetComponentInChildren<SpriteRenderer>(true);
        if (visual == null) return;

        if (Power == PowerType.None)
        {
            if (normalSprite != null) visual.sprite = normalSprite;
            ApplyVisualScale();
            return;
        }

        Sprite target = ResolvePowerSprite();

        if (target == null)
        {
            // Slot boş. Kristal kendi görselinde kalır - görünmez bir power-up
            // oyuncuyu yanıltır, o yüzden editörde bir kez uyarıyoruz.
            WarnMissingPowerSprite();
            target = normalSprite;
        }

        if (target != null) visual.sprite = target;

        ApplyVisualScale();
    }

    void ApplyVisualScale()
    {
        if (visual == null || spriteIsOnRoot) return;

        visual.transform.localScale = visualBaseScale * CurrentPowerScale;
    }

    // Aynı kristal için konsolu doldurmayalım - slot başına tek uyarı yeter
    static readonly HashSet<int> warnedSlots = new HashSet<int>();

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
    void WarnMissingPowerSprite()
    {
        int key = ((int)Type << 4) | (int)Power;
        if (!warnedSlots.Add(key)) return;

        string slot = Power switch
        {
            PowerType.Cross => "Cross Sprite",
            PowerType.Bomb  => "Bomb Sprite",
            _               => "Color Bomb Sprite"
        };

        Debug.LogWarning(
            $"[Crystal] {Type}: '{slot}' atanmamış - power-up normal kristal gibi " +
            $"görünecek. Prefab'daki Crystal bileşenini kontrol et.", this);
    }

    void StartPulse()
    {
        KillPulse();

        Vector3 b = CurrentRootBase;
        transform.localScale = b;

        if (Power == PowerType.None || !pulseWhenPowered) return;

        pulseTween = transform
            .DOScale(b * pulseScale, pulseSpeed)
            .SetLoops(-1, LoopType.Yoyo)
            .SetEase(Ease.InOutSine);
    }

    // Doğuş animasyonunun süresi. Tahta yenilenirken dalganın ne kadar
    // süreceğini hesaplamak için Board da okuyor.
    public const float SpawnAnimTime = 0.4f;

    // Dönerek ortaya çıkma. Power-up doğuşunda ve tahta yenilenirken kullanılır.
    // delay > 0 ise kristal o kadar bekleyip belirir - dalga efekti bununla kuruluyor.
    public void PlaySpawnAnimation(float delay = 0f)
    {
        KillPulse();
        KillMove();

        // Boyut hemen sıfırlanıyor: bekleme süresi boyunca kristal görünmez
        // kalmalı, yoksa dalga sırası gelmeden ekranda belirir.
        transform.localScale = Vector3.zero;
        transform.rotation = Quaternion.Euler(0, 0, -120f);

        Sequence seq = DOTween.Sequence();

        if (delay > 0f) seq.AppendInterval(delay);

        seq.Append(transform.DOScale(CurrentRootBase, SpawnAnimTime).SetEase(Ease.OutBack, 2f));
        seq.Join(transform.DORotate(Vector3.zero, SpawnAnimTime).SetEase(Ease.OutBack));
        seq.OnComplete(StartPulse);

        moveTween = seq;
    }

    // ==================== DÜŞME (SQUASH) ====================

    // Kristalyi hedefe düşür. Yere değince yassılır, sonra toparlanır.
    // hideAboveY: bu yüksekliğin ÜSTÜNDEYKEN kristal çizilmez. Tahtanın
    // dışında doğup aşağı düşen kristallerin yolda görünmemesi için.
    //
    // Sprite Mask yerine bunu kullanıyoruz: maske stencil buffer'a, oradan da
    // URP'nin 2D renderer'ına ve sprite materyalinin lit/unlit oluşuna bağlı.
    // Renderer'ı doğrudan kapatmak her kurulumda aynı çalışıyor ve mobilde
    // stencil buffer'ı açık tutma maliyetini de ortadan kaldırıyor.
    public void AnimateFall(Vector3 target, float duration, float delay = 0f,
                            float? hideAboveY = null)
    {
        KillPulse();
        KillMove();

        Vector3 b = CurrentRootBase;
        transform.localScale = b;

        bool hiding = false;

        if (hideAboveY.HasValue && visual != null &&
            transform.position.y > hideAboveY.Value)
        {
            visual.enabled = false;
            hiding = true;
        }

        Sequence seq = DOTween.Sequence();

        if (delay > 0f) seq.AppendInterval(delay);

        Tween move = transform.DOMove(target, duration)
                              .SetEase(useSquashStretch ? Ease.InQuad : Ease.OutBounce);

        // Tahtanın üst kenarını geçtiği KARE'de görünür ol. Süreye göre
        // hesaplamıyoruz: easing doğrusal değil, konuma bakmak tek doğru yol.
        if (hiding)
        {
            float limit = hideAboveY.Value;

            move.OnUpdate(() =>
            {
                if (visual == null || visual.enabled) return;
                if (transform.position.y <= limit) visual.enabled = true;
            });
        }

        seq.Append(move);

        if (useSquashStretch)
        {
            // Yere çarpma: yassıl, sonra toparlan.
            // Düşerken uzama (stretch) aşaması kaldırıldı - düşen kristal başına
            // bir tween daha az, tam tahta yenilenmesinde 64 tween tasarruf.
            Vector3 squashed = new Vector3(
                b.x * (1f + squashAmount),
                b.y * (1f - squashAmount),
                b.z);

            seq.Append(transform.DOScale(squashed, squashDuration).SetEase(Ease.OutQuad));
            seq.Append(transform.DOScale(b, squashDuration * 2f).SetEase(Ease.OutBack, 2.5f));
        }

        seq.OnComplete(StartPulse);

        moveTween = seq;
    }

    // Takas hareketi - hafif overshoot
    public void AnimateSwap(Vector3 target, float duration)
    {
        KillMove();

        // Yere iniş yassılması (squash) yarıda kalmış olabilir: AnimateFall'un
        // son iki adımı kristali ezip sonra tabana çekiyor ve bu, düşüş süresi
        // dolduktan SONRA bitiyor. Oyuncu tam o aralıkta kaydırırsa KillMove
        // tween'i ezilmiş boyutta kesiyordu ve kristal kalıcı olarak basık
        // kalıyordu - takas animasyonu boyutu hiç geri almıyor.
        transform.localScale = CurrentRootBase;

        moveTween = transform.DOMove(target, duration)
                             .SetEase(Ease.OutBack, 1.4f)
                             .OnComplete(StartPulse);
    }

    // Renk bombasının ışını değince minik sarsıntı
    public void PlayShake(float duration, float strength)
    {
        KillMove();

        // Yarıda kesilmiş bir squash burada da takılı kalabilir
        transform.localScale = CurrentRootBase;

        moveTween = transform.DOShakePosition(duration, strength, 20, 90, false, false);
    }

    // ==================== İPUCU ====================

    // Oyuncu bir süre hamle yapmayınca "buraya bak" demek için: yanıp söner
    // ve hafifçe zıplar. Tween döngüde, Board'ın tekrar tekrar çağırması
    // gerekmiyor - StopHint gelene kadar oynar.
    public void PlayHint(float hopHeight, float cycleDuration)
    {
        if (hinting) return;   // zaten oynuyor, baştan başlatıp titretme

        KillMove();
        KillPulse();

        hinting = true;
        hintBasePos = transform.position;

        // İpucu sadece konum ve saydamlık oynatıyor; boyutu düzeltmezsek
        // yarıda kalmış bir squash ipucu boyunca ekranda basık durur.
        transform.localScale = CurrentRootBase;

        Sequence seq = DOTween.Sequence();

        // Zıpla ve in
        seq.Append(transform.DOMoveY(hintBasePos.y + hopHeight, cycleDuration * 0.3f)
                            .SetEase(Ease.OutQuad));
        seq.Append(transform.DOMoveY(hintBasePos.y, cycleDuration * 0.3f)
                            .SetEase(Ease.InQuad));
        seq.AppendInterval(cycleDuration * 0.4f);

        // Yanıp sönme zıplamayla BİRLİKTE aksın - Insert(0) olmazsa Join
        // en son eklenen parçaya yapışır ve sadece beklemeyle çakışır.
        if (visual != null)
            seq.Insert(0f, visual.DOFade(hintFadeAlpha, cycleDuration * 0.3f)
                                 .SetLoops(2, LoopType.Yoyo));

        seq.SetLoops(-1);

        hintTween = seq;
    }

    public void StopHint()
    {
        if (!hinting) return;

        hinting = false;

        hintTween?.Kill();
        hintTween = null;

        // Zıplamanın ortasında kesilmiş olabiliriz - kareye geri otur
        transform.position = hintBasePos;

        if (visual != null)
        {
            Color c = visual.color;
            c.a = 1f;
            visual.color = c;
        }

        StartPulse();   // power-up nabzı varsa geri gelsin
    }

    // ==================== PATLAMA ====================

    // Anticipation (hazırlık) + patlama. Bitince onComplete çağrılır.
    // delay > 0 ise patlama o kadar gecikir - tahta yenilenirken çapraz dalga
    // bu gecikmeyle kuruluyor.
    public void PlayDestroy(float duration, bool anticipation, System.Action onComplete,
                            float delay = 0f)
    {
        KillPulse();
        KillMove();

        Vector3 b = CurrentRootBase;

        // Birleşme sonrası şişmiş bir power-up'ı tabana geri çekmiyoruz: önce
        // küçülüp sonra tekrar patlaması gözle görülür bir zıplama üretiyordu.
        if (transform.localScale.x > b.x) b = transform.localScale;

        transform.localScale = b;

        Sequence seq = DOTween.Sequence();

        if (delay > 0f) seq.AppendInterval(delay);

        // 1. Hazırlık: minik titreme - "bir şey olacak" hissi
        if (anticipation)
            seq.Append(transform.DOShakeScale(duration * 0.25f, 0.25f, 12, 90f, false));

        // 2. Şişme
        seq.Append(transform.DOScale(b * 1.3f, duration * 0.25f).SetEase(Ease.OutQuad));

        // 3. Dönerek yok olma
        seq.Append(transform.DOScale(0f, duration * 0.5f).SetEase(Ease.InBack, 2f));
        seq.Join(transform.DORotate(new Vector3(0, 0, Random.Range(-200f, 200f)),
                                    duration * 0.5f, RotateMode.FastBeyond360));

        seq.OnComplete(() => onComplete?.Invoke());

        moveTween = seq;
    }

    void OnDisable()
    {
        KillPulse();
        StopHint();
    }
}
