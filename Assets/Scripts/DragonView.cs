using DG.Tweening;
using UnityEngine;

// Yuva sahnesindeki 3D ejderha. Aşamanın modelini gösterir, beslendikçe
// büyütür, aşama atlayınca kutlama oynatır. Kural yok - ne göstereceğini
// NestUI söylüyor.
//
// Tripo'dan (ya da başka yerden) gelen model için: prefab'ın içinde bir
// Animator varsa ve controller'da "Eat", "Roar" ya da "Hatch" adında bir
// trigger parametresi YA DA state varsa onlar da oynar. Hiçbiri yoksa ejderha
// sadece tween'lerle canlanır - geçici model böyle çalışıyor.
public class DragonView : MonoBehaviour
{
    [Tooltip("Modelin konduğu nokta. Büyüme bunun ölçeğiyle yapılıyor, modelin " +
             "kendi ölçeğine dokunulmuyor.")]
    [SerializeField] Transform modelRoot;

    [Tooltip("Aşamanın sürekli efektinin (aura) konduğu yer")]
    [SerializeField] Transform effectRoot;

    [SerializeField] ParticleSystem stageUpBurst;
    [SerializeField] ParticleSystem feedBurst;

    [Tooltip("Aşama atlarken sarsılacak kamera")]
    [SerializeField] Camera stageCamera;

    [Header("Bekleme")]
    [Tooltip("Sağa sola yavaşça dönme açısı (derece)")]
    [SerializeField] float swayAngle = 22f;
    [SerializeField] float swayPeriod = 7f;
    [SerializeField] float bobHeight = 0.06f;
    [SerializeField] float bobDuration = 1.8f;

    [Header("Parmakla Çevirme")]
    [Tooltip("Parmak bırakıldıktan sonra dönüşün sönme hızı - büyük değer çabuk durur")]
    [SerializeField] float spinDamping = 3f;

    [Tooltip("Hızlı kaydırmada ulaşılabilecek en yüksek dönüş (derece/sn)")]
    [SerializeField] float maxSpinSpeed = 720f;

    [Header("Büyüme")]
    [SerializeField] float growDuration = 0.45f;

    GameObject currentModel;
    Renderer[] modelRenderers;
    GameObject currentSource;
    GameObject currentAmbient;
    GameObject currentAmbientSource;
    Animator animator;

    Tween bobTween;
    Vector3 rootBasePosition;
    float baseYaw;
    float targetScale = 1f;

    // Oyuncunun parmakla çevirdiği açı ve bıraktıktan sonraki dönüş hızı
    float userYaw;
    float spinVelocity;
    bool spinning;

    // Kendi kendine salınımın ağırlığı: sürüklerken 0'a iner, bırakınca
    // yavaşça 1'e döner - salınım parmağın altında kıpırdamasın
    float swayWeight = 1f;

    void Awake()
    {
        if (modelRoot == null) modelRoot = transform;

        rootBasePosition = modelRoot.localPosition;
        baseYaw = modelRoot.localEulerAngles.y;
    }

    void OnEnable()
    {
        StartBob();
    }

    void OnDisable()
    {
        bobTween?.Kill();
        modelRoot.DOKill();
        modelRoot.localPosition = rootBasePosition;
    }

    // Açı üç parçadan: başlangıç açısı + oyuncunun çevirdiği + kendiliğinden
    // salınım. Model hep aynı açıda dursa cansız görünür; salınım yavaşça bir
    // o yana bir bu yana baktırıyor.
    void Update()
    {
        float dt = Time.deltaTime;

        if (!spinning && spinVelocity != 0f)
        {
            userYaw += spinVelocity * dt;
            spinVelocity *= Mathf.Exp(-spinDamping * dt);

            if (Mathf.Abs(spinVelocity) < 1f) spinVelocity = 0f;
        }

        // Sürüklerken salınım hızla söner, bırakınca yavaşça geri gelir
        swayWeight = Mathf.MoveTowards(swayWeight, spinning ? 0f : 1f, dt * (spinning ? 4f : 0.4f));

        float sway = swayAngle > 0f && swayPeriod > 0f
            ? Mathf.Sin(Time.time * Mathf.PI * 2f / swayPeriod) * swayAngle * swayWeight
            : 0f;

        modelRoot.localRotation = Quaternion.Euler(0f, baseYaw + userYaw + sway, 0f);
    }

    // ==================== PARMAKLA ÇEVİRME ====================
    // DragonSpinArea çağırıyor

    public void BeginSpin()
    {
        spinning = true;
        spinVelocity = 0f;
    }

    public void Spin(float degrees)
    {
        userYaw += degrees;
    }

    // velocity: derece/sn - bırakırken parmak ne hızla gidiyordu
    public void EndSpin(float velocity)
    {
        spinning = false;
        spinVelocity = Mathf.Clamp(velocity, -maxSpinSpeed, maxSpinSpeed);
    }

    void StartBob()
    {
        bobTween?.Kill();

        if (bobHeight <= 0f) return;

        modelRoot.localPosition = rootBasePosition;
        bobTween = modelRoot.DOLocalMoveY(rootBasePosition.y + bobHeight, bobDuration)
                            .SetEase(Ease.InOutSine)
                            .SetLoops(-1, LoopType.Yoyo);
    }

    // ==================== GÖSTER ====================

    // fed = ejderhanın bugüne kadar yediği yumurta. animate false ise anında.
    public void Show(DragonDefinition dragon, int fed, bool animate)
    {
        if (dragon == null || dragon.StageCount == 0) return;

        int stage = dragon.StageAt(fed);
        DragonStage s = dragon.stages[stage];

        SwapModel(dragon.ModelFor(stage));
        SwapAmbient(s.ambientEffect);

        targetScale = Mathf.Lerp(s.scaleStart, s.scaleEnd, dragon.ProgressInStage(fed));

        modelRoot.DOKill();
        StartBob();

        if (animate)
            modelRoot.DOScale(targetScale, growDuration).SetEase(Ease.OutBack);
        else
            modelRoot.localScale = Vector3.one * targetScale;
    }

    void SwapModel(GameObject source)
    {
        if (source == currentSource && currentModel != null) return;

        if (currentModel != null) Destroy(currentModel);

        currentSource = source;
        currentModel = null;
        modelRenderers = null;
        animator = null;

        if (source == null) return;

        // Rotasyon ve boyut prefab'tan geliyor (EjderPrefabs'ta elle ayarlı:
        // kameraya dönük, aşamaya göre boyutlu). Pozisyon sıfırlanıyor - prefab'lar
        // yan yana dizilip kaydedildiği için kökte o yerleşim kalmış.
        currentModel = Instantiate(source, modelRoot);
        currentModel.transform.localPosition = Vector3.zero;

        modelRenderers = currentModel.GetComponentsInChildren<Renderer>();
        animator = currentModel.GetComponentInChildren<Animator>();
    }

    // Modelin ekranda görünen ortası ve boyu: yakınlaşan kamera buraya dönüyor,
    // uçan yumurta buraya iniyor. Aşamalar çok farklı boyda (yumurta minik,
    // yaşlı dev), sabit bir nokta hepsine uymuyor.
    //
    // Salınımdan etkilenmiyor: Y ekseninde dönünce dikey sınırlar değişmez,
    // yatayda da modelin ekseni alınıyor. Zıplama çıkarılıyor - kamera
    // ejderhayla birlikte inip kalkmasın.
    public bool TryGetFocus(out Vector3 center, out float height)
    {
        center = default;
        height = 0f;

        if (modelRenderers == null) return false;

        Bounds bounds = default;
        bool any = false;

        foreach (Renderer r in modelRenderers)
        {
            if (r == null || !r.enabled || r is ParticleSystemRenderer) continue;

            if (any) bounds.Encapsulate(r.bounds);
            else { bounds = r.bounds; any = true; }
        }

        if (!any) return false;

        Vector3 rest = modelRoot.parent != null ? modelRoot.parent.TransformPoint(rootBasePosition) : rootBasePosition;

        center = new Vector3(rest.x, rest.y + bounds.center.y - modelRoot.position.y, rest.z);
        height = bounds.size.y;
        return true;
    }

    void SwapAmbient(GameObject source)
    {
        if (source == currentAmbientSource) return;

        if (currentAmbient != null) Destroy(currentAmbient);

        currentAmbientSource = source;
        currentAmbient = source != null ? Instantiate(source, effectRoot != null ? effectRoot : transform) : null;
    }

    // ==================== TEPKİLER ====================

    // Bir yumurta yedi: minik zıplama + kıvılcım
    public void PlayFeed()
    {
        modelRoot.DOComplete();
        modelRoot.DOPunchScale(Vector3.one * targetScale * 0.12f, 0.35f, 6, 0.6f);

        if (feedBurst != null) feedBurst.Play();

        TryAnimate("Eat");
    }

    // Aşama atladı: küçül, yeni aşamaya geç, patlayarak büyü
    public void PlayStageUp(DragonDefinition dragon, int fed)
    {
        int stage = dragon.StageAt(fed);
        DragonStage s = dragon.stages[stage];

        modelRoot.DOKill();
        bobTween?.Kill();

        targetScale = Mathf.Lerp(s.scaleStart, s.scaleEnd, dragon.ProgressInStage(fed));

        Sequence seq = DOTween.Sequence();

        // Kabuğa çekil...
        seq.Append(modelRoot.DOScale(modelRoot.localScale.x * 0.75f, 0.18f).SetEase(Ease.InBack));

        // ...yeni hâl patlayarak gelsin
        seq.AppendCallback(() =>
        {
            SwapModel(dragon.ModelFor(stage));
            SwapAmbient(s.ambientEffect);

            modelRoot.localScale = Vector3.one * targetScale * 0.3f;

            if (stageUpBurst != null) stageUpBurst.Play();

            if (stageCamera != null)
            {
                stageCamera.transform.DOComplete();
                stageCamera.transform.DOShakePosition(0.45f, 0.18f, 18, 90f, false, true);
            }

            TryAnimate(stage == 1 ? "Hatch" : "Roar");
        });

        seq.Append(modelRoot.DOScale(targetScale * 1.18f, 0.35f).SetEase(Ease.OutBack, 2f));
        seq.Append(modelRoot.DOScale(targetScale, 0.25f).SetEase(Ease.InOutSine));
        seq.OnComplete(StartBob);
    }

    // Controller'da bu adda trigger varsa tetikle, yoksa bu adda state varsa
    // ona geç. İkisi de yoksa sessizce geç - geçici modelde Animator yok.
    void TryAnimate(string name)
    {
        if (animator == null || animator.runtimeAnimatorController == null) return;

        foreach (AnimatorControllerParameter p in animator.parameters)
        {
            if (p.type == AnimatorControllerParameterType.Trigger && p.name == name)
            {
                animator.SetTrigger(name);
                return;
            }
        }

        int hash = Animator.StringToHash(name);
        if (animator.HasState(0, hash)) animator.CrossFade(hash, 0.1f);
    }
}
