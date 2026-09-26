using System.Collections;
using DG.Tweening;
using UnityEngine;

public class JuiceManager : MonoBehaviour
{
    public static JuiceManager Instance;

    [Header("Kamera")]
    [SerializeField] Camera gameCamera;
    [SerializeField] float shakeDuration = 0.18f;
    [SerializeField] float shakeStrength = 0.12f;

    [Header("Kamera Zoom")]
    [Tooltip("Büyük patlamalarda kamera hafif yakınlaşıp geri çekilir")]
    [SerializeField] bool useZoomPunch = true;
    [Tooltip("Ne kadar yakınlaşsın - 0.03 = %3")]
    [Range(0f, 0.15f)]
    [SerializeField] float zoomAmount = 0.04f;
    [SerializeField] float zoomInDuration = 0.08f;
    [SerializeField] float zoomOutDuration = 0.25f;

    [Header("Patlama Efekti")]
    [Tooltip("Tahtanın TAMAMI için TEK sistem - prefab değil, sahnedeki obje. " +
             "Her patlamada Emit ile o kristalin renginde parça enjekte ediliyor. " +
             "Emission > Rate over Time = 0 olmalı, parçacıklar sadece Emit ile doğar.")]
    [SerializeField] ParticleSystem shardEffect;

    [Tooltip("Bir kristal patlayınca kaç parça uçsun")]
    [Range(1, 12)]
    [SerializeField] int shardsPerPop = 3;

    [Header("Titreşim")]
    [Tooltip("Patlamalarda telefon titresin")]
    [SerializeField] bool useHaptics = true;

    [Header("Hit Stop")]
    [Tooltip("Büyük patlamalarda zaman çok kısa süre yavaşlar")]
    [SerializeField] bool useHitStop = true;

    [Tooltip("Zaman ölçeği. DİKKAT: bu değer tüm oyunu yavaşlatır - DOTween " +
             "animasyonları ve Board'ın beklemeleri de scaled time kullanıyor. " +
             "0.5 civarı darbe hissini verir ama oyunu ağır çekime sokmaz.")]
    [Range(0.2f, 0.9f)]
    [SerializeField] float hitStopScale = 0.5f;

    [Tooltip("Hit stop süresi (gerçek saniye)")]
    [SerializeField] float hitStopDuration = 0.04f;

    [Tooltip("Hit stop için gereken en az kristal sayısı")]
    [SerializeField] int hitStopMinCrystals = 14;

    [Tooltip("Hit stop için gereken en az zincir seviyesi")]
    [SerializeField] int hitStopMinCascade = 4;

    // Emit çağrıları arasında yeniden kullanılıyor. EmitParams bir struct:
    // bir alanı yazınca ilgili "apply" bayrağı da açılıyor, o yüzden bir kez
    // doldurup her patlamada sadece position/startColor güncellemek yeterli.
    ParticleSystem.EmitParams shardEmit;

    Transform camTransform;
    float baseOrthoSize;
    Coroutine hitStopRoutine;

    void Awake()
    {
        Instance = this;

        if (gameCamera == null) gameCamera = Camera.main;

        if (gameCamera != null)
        {
            camTransform = gameCamera.transform;
            baseOrthoSize = gameCamera.orthographicSize;
        }

        // Sistem sürekli açık kalmalı: Emit ile enjekte edilen parçacıklar ancak
        // sistem oynuyorken simüle ediliyor. Play On Awake tiki unutulursa diye
        // burada garantiye alıyoruz.
        if (shardEffect != null && !shardEffect.isPlaying)
            shardEffect.Play();

        WarnIfShardEffectUnusable();
    }

    // ==================== KAMERA ====================

    public void ShakeCamera(float multiplier = 1f)
    {
        if (camTransform == null) return;

        camTransform.DOComplete();
        camTransform.DOShakePosition(shakeDuration, shakeStrength * multiplier, 10, 90);
    }

    // Kamera hafif yakınlaşıp geri çekilir - darbe hissi
    public void PunchZoom(float multiplier = 1f)
    {
        if (!useZoomPunch || gameCamera == null) return;

        gameCamera.DOKill();

        float targetSize = baseOrthoSize * (1f - zoomAmount * multiplier);

        Sequence seq = DOTween.Sequence();
        seq.Append(gameCamera.DOOrthoSize(targetSize, zoomInDuration).SetEase(Ease.OutQuad));
        seq.Append(gameCamera.DOOrthoSize(baseOrthoSize, zoomOutDuration).SetEase(Ease.OutBack));
    }

    // ==================== HIT STOP ====================

    // Zamanı çok kısa süre yavaşlat - vuruş hissi
    public void HitStop()
    {
        if (!useHitStop) return;

        if (hitStopRoutine != null)
        {
            StopCoroutine(hitStopRoutine);
            Time.timeScale = 1f;
        }

        hitStopRoutine = StartCoroutine(HitStopRoutine());
    }

    IEnumerator HitStopRoutine()
    {
        Time.timeScale = hitStopScale;
        yield return new WaitForSecondsRealtime(hitStopDuration);
        Time.timeScale = 1f;
        hitStopRoutine = null;
    }

    // ==================== PARTİKÜL ====================

    // Kristal patladığı yerde, o kristalin renginde parçalar fırlat.
    //
    // Havuz yok: tahtanın tamamı tek ParticleSystem kullanıyor ve her patlama
    // içine parçacık enjekte ediyor. Eski havuzlu sürüm büyük patlamalarda
    // kendini yiyordu - havuz 12'ydi ama bir mega roket 40 kristal patlatabiliyor,
    // 13. çağrıda ilk sistem geri gelip Clear() ile kendi taze parçacıklarını
    // siliyordu. Yani efekt tam da en çok görülmesi gereken anda kayboluyordu.
    public void PlayPopEffect(Vector3 position, Color tint)
    {
        if (shardEffect == null) return;

        shardEmit.position = EmitPosition(shardEffect, position);

        // Shape modülü bu noktanın ETRAFINA dağıtsın. Kapalıyken bütün parçalar
        // tam olarak aynı noktada doğuyor ve Shape modülü hiç çalışmıyor.
        shardEmit.applyShapeToPosition = true;

        shardEmit.startColor = tint;

        shardEffect.Emit(shardEmit, shardsPerPop);
    }

    // Renk verilmeyen çağrılar için - beyaz kalır
    public void PlayPopEffect(Vector3 position)
    {
        PlayPopEffect(position, Color.white);
    }

    // Bu efektin iki sessiz kaybolma yolu var ve ikisi de hata vermiyor:
    // alan boş kalırsa, ya da alana SAHNEDEKİ obje yerine PREFAB ASSET'i
    // sürüklenirse. Prefab asset simüle edilmez, render edilmez - Emit sessizce
    // hiçbir şey yapmaz. Ekranda efekt olmayınca insan shader'dan şüpheleniyor.
    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
    void WarnIfShardEffectUnusable()
    {
        if (shardEffect == null)
        {
            Debug.LogWarning("[JuiceManager] Shard Effect atanmamış - patlama " +
                             "parçaları hiç görünmeyecek.", this);
            return;
        }

        // Prefab asset'in sahnesi geçersizdir; sahnedeki bir örneğinki geçerli.
        if (!shardEffect.gameObject.scene.IsValid())
            Debug.LogWarning("[JuiceManager] Shard Effect alanına PREFAB sürüklenmiş. " +
                             "Prefabı önce sahneye at, sonra SAHNEDEKİ objeyi buraya " +
                             "sürükle - prefab asset emit edemez.", this);
    }

    // EmitParams.position sistemin simülasyon uzayında yorumlanıyor.
    // World ise dünya koordinatı doğrudan geçer; Local ise sisteme göre
    // çevirmek gerekiyor - yoksa parçalar tahtanın dışında doğar.
    static Vector3 EmitPosition(ParticleSystem ps, Vector3 world)
    {
        return ps.main.simulationSpace == ParticleSystemSimulationSpace.World
            ? world
            : ps.transform.InverseTransformPoint(world);
    }

    // ==================== TOPLU ETKİ ====================

    // Patlama büyüklüğüne göre uygun efektleri birlikte tetikle
    public void BigBlastFeedback(int crystalCount, int cascadeLevel)
    {
        float intensity = Mathf.Clamp01(crystalCount / 12f);

        ShakeCamera(0.5f + intensity * 1.5f);

        // Titreşim - patlama büyüklüğüne göre şiddet
        if (useHaptics && HapticManager.Instance != null)
            HapticManager.Instance.PlayForBlast(crystalCount);

        if (crystalCount >= 6)
            PunchZoom(0.6f + intensity);

        if (crystalCount >= hitStopMinCrystals || cascadeLevel >= hitStopMinCascade)
            HitStop();
    }

    void OnDestroy()
    {
        // Sahne değişirken zaman ölçeği takılı kalmasın
        Time.timeScale = 1f;
    }
}
