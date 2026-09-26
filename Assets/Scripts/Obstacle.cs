using DG.Tweening;
using UnityEngine;

// Bir karenin üstünde duran engelin GÖRSELİ. Burada kural yok - sadece sprite
// ve animasyon. Hangi karede hangi engel var, kaç canı kaldı: ObstacleGrid.
//
// Prefab kurulumu:
//   - Kök objede SpriteRenderer + bu script
//   - COLLIDER KOYMA. Koyarsan şeffaf engelin arkasındaki kristale dokunulamaz
//     (InputHandler yine de kristali bulacak şekilde yazıldı ama gereksiz iş).
//   - SpriteRenderer'ın Order in Layer'ı kristallerden BÜYÜK olmalı; katı engel
//     arkasındaki kristali tamamen örtmeli.
public class Obstacle : MonoBehaviour
{
    [Header("Görsel")]
    [Tooltip("Boş bırakırsan Awake'te otomatik bulunur.")]
    [SerializeField] SpriteRenderer visual;

    [Header("KATI engel (kristal yok, kare oynanamaz)")]
    [Tooltip("Hasar kademeleri. 0 = sapasağlam hâli, son eleman = kırılmak üzere. " +
             "Tek vuruşta kırılan engel için tek sprite yeterli.")]
    [SerializeField] Sprite[] solidStages;

    [Header("ŞEFFAF engel (arkasında kristal var, oynanır)")]
    [Tooltip("Hasar kademeleri. Sprite'ın ORTASI saydam olmalı - kristal buradan görünüyor.")]
    [SerializeField] Sprite[] transparentStages;

    [Header("BLOK (çok vuruşlu) - levelin temasına göre biri")]
    [Tooltip("Lav teması: sağlam, çatlak, çok çatlak. Kademeler levelin blok " +
             "vuruş sayısına orantılı dağılır.")]
    [SerializeField] Sprite[] lavaStages;

    [Tooltip("Buz teması: sağlam, çatlak, çok çatlak")]
    [SerializeField] Sprite[] iceStages;

    [Header("Kırılma Parçalarının Rengi")]
    [SerializeField] Color solidPopColor = new Color(0.62f, 0.55f, 0.45f);
    [SerializeField] Color transparentPopColor = new Color(0.75f, 0.92f, 1f);
    [SerializeField] Color lavaPopColor = new Color(1f, 0.5f, 0.15f);
    [SerializeField] Color icePopColor = new Color(0.7f, 0.9f, 1f);

    [Header("Animasyon")]
    [Tooltip("Vuruş yiyince (henüz kırılmadı) titreme miktarı")]
    [SerializeField] float hitPunch = 0.18f;
    [SerializeField] float hitDuration = 0.22f;

    [Tooltip("Katıdan şeffafa dönerken sıçrama miktarı")]
    [SerializeField] float downgradePunch = 0.3f;
    [SerializeField] float downgradeDuration = 0.3f;

    [Tooltip("Tamamen kırılırken küçülme süresi")]
    [SerializeField] float breakDuration = 0.22f;

    public ObstacleType Type { get; private set; } = ObstacleType.None;

    // Blok görselinin teması. ObstacleGrid SetState'ten ÖNCE atıyor.
    public BlockTheme Theme { get; set; }

    // Kırılınca uçacak parçaların rengi
    public Color PopColor
    {
        get
        {
            switch (Type)
            {
                case ObstacleType.Solid: return solidPopColor;
                case ObstacleType.Block: return Theme == BlockTheme.Buz ? icePopColor : lavaPopColor;
            }

            return transparentPopColor;
        }
    }

    Tween tween;
    Vector3 baseScale;

    void Awake()
    {
        if (visual == null) visual = GetComponentInChildren<SpriteRenderer>(true);
        baseScale = transform.localScale;
    }

    void KillTween()
    {
        tween?.Kill();
        tween = null;
    }

    // ==================== DURUM ====================

    // type = şu anki kademe, damageTaken = bu kademede kaç vuruş yediği (0 = taze),
    // totalHits = bu kademenin toplam canı.
    //
    // Sprite kademeleri cana ORANTILI dağılıyor: 3 görselli bir blok 3 canlıysa
    // her vuruş bir kademe, 5 canlıysa 0-0-1-1-2. Canla görsel sayısı eşit olmak
    // zorunda değil.
    public void SetState(ObstacleType type, int damageTaken, int totalHits)
    {
        Type = type;

        if (visual == null) visual = GetComponentInChildren<SpriteRenderer>(true);
        if (visual == null) return;

        Sprite[] stages = StagesFor(type);

        if (stages == null || stages.Length == 0)
        {
            WarnMissingSprite(type);
            return;
        }

        int index = totalHits > 0 ? damageTaken * stages.Length / totalHits : damageTaken;
        index = Mathf.Clamp(index, 0, stages.Length - 1);

        // Eleman var ama boş/Missing: silinmiş ya da yeniden import edilmiş
        // sprite'a bakan referans. Dizi dolu göründüğü için gözden kaçıyor.
        if (stages[index] == null)
        {
            WarnMissingSprite(type);
            return;
        }

        visual.sprite = stages[index];

        // Alfa'yı da geri al: kırılma animasyonu yarıda kalmış olabilir
        Color c = visual.color;
        c.a = 1f;
        visual.color = c;
    }

    Sprite[] StagesFor(ObstacleType type)
    {
        switch (type)
        {
            case ObstacleType.Solid: return solidStages;
            case ObstacleType.Block: return Theme == BlockTheme.Buz ? iceStages : lavaStages;
        }

        return transparentStages;
    }

    string SlotName(ObstacleType type)
    {
        switch (type)
        {
            case ObstacleType.Solid: return "Solid Stages";
            case ObstacleType.Block: return Theme == BlockTheme.Buz ? "Ice Stages" : "Lava Stages";
        }

        return "Transparent Stages";
    }

    // ==================== ANİMASYON ====================

    // Vuruş yedi ama kırılmadı - "dayandı" hissi
    public void PlayHit()
    {
        KillTween();

        transform.localScale = baseScale;
        tween = transform.DOPunchScale(baseScale * hitPunch, hitDuration, 8, 0.6f);
    }

    // Katı -> şeffaf. Sprite'ı SetState değiştiriyor, burası sadece sıçratıyor.
    public void PlayDowngrade()
    {
        KillTween();

        transform.localScale = baseScale;
        tween = transform.DOPunchScale(baseScale * downgradePunch, downgradeDuration, 10, 0.8f);
    }

    // Tamamen kırıldı: şişip dönerek yok olur, sonra objeyi siler
    public void PlayBreak()
    {
        KillTween();

        Sequence seq = DOTween.Sequence();

        seq.Append(transform.DOScale(baseScale * 1.25f, breakDuration * 0.3f).SetEase(Ease.OutQuad));
        seq.Append(transform.DOScale(0f, breakDuration * 0.7f).SetEase(Ease.InBack, 2f));
        seq.Join(transform.DORotate(new Vector3(0, 0, Random.Range(-120f, 120f)),
                                    breakDuration * 0.7f, RotateMode.FastBeyond360));

        if (visual != null)
            seq.Join(visual.DOFade(0f, breakDuration).SetEase(Ease.InQuad));

        seq.OnComplete(() => Destroy(gameObject));

        tween = seq;
    }

    void OnDestroy()
    {
        KillTween();
    }

    // Sprite atanmamış bir engel görünmez olur ve oyuncu "neden bu kare
    // oynanmıyor" diye bakakalır. Slot başına tek uyarı yeter.
    static readonly System.Collections.Generic.HashSet<int> warnedSlots =
        new System.Collections.Generic.HashSet<int>();

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
    void WarnMissingSprite(ObstacleType type)
    {
        // Blokta iki ayrı dizi var (lav, buz) - her biri kendi uyarısını alsın
        int key = (int)type * 10 + (type == ObstacleType.Block ? (int)Theme : 0);
        if (!warnedSlots.Add(key)) return;

        string slot = SlotName(type);

        Debug.LogWarning(
            $"[Obstacle] '{slot}' dizisi boş ya da içinde Missing sprite var - " +
            $"{type} engeli görünmeyecek. Obstacle prefab'ına sprite ekle.", this);
    }
}
