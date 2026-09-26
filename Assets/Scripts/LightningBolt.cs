using System.Collections;
using UnityEngine;

// Tek bir elektrik ışını. LineRenderer ile zikzak çizer, titreşir, söner.
// LightningManager tarafından havuzdan verilir.
[RequireComponent(typeof(LineRenderer))]
public class LightningBolt : MonoBehaviour
{
    [Header("Şekil")]
    [Tooltip("Işının kaç parçadan oluşacağı - fazlası daha kıvrımlı")]
    [SerializeField] int segments = 10;

    [Tooltip("Zikzakların yana sapma miktarı")]
    [SerializeField] float jaggedness = 0.25f;

    [SerializeField] float startWidth = 0.14f;
    [SerializeField] float endWidth = 0.06f;

    [Header("Zamanlama")]
    [Tooltip("Işının kaynaktan hedefe uzama süresi")]
    [SerializeField] float growDuration = 0.12f;

    [Tooltip("Uzadıktan sonra titreşerek bekleme süresi")]
    [SerializeField] float holdDuration = 0.12f;

    [Tooltip("Sönme süresi")]
    [SerializeField] float fadeDuration = 0.15f;

    [Tooltip("Saniyede kaç kez zikzak yeniden hesaplansın - çatırdama hissi")]
    [SerializeField] float crackleRate = 30f;

    [Header("Renk")]
    [SerializeField] Color coreColor = Color.white;
    [SerializeField] Color glowColor = new Color(0.5f, 0.85f, 1f);

    LineRenderer line;
    Coroutine running;

    void Awake()
    {
        line = GetComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.positionCount = segments;
        line.numCapVertices = 4;
        line.numCornerVertices = 2;
        line.textureMode = LineTextureMode.Stretch;
        line.alignment = LineAlignment.View;
    }

    public void Fire(Vector3 from, Vector3 to)
    {
        gameObject.SetActive(true);

        if (running != null) StopCoroutine(running);
        running = StartCoroutine(FireRoutine(from, to));
    }

    IEnumerator FireRoutine(Vector3 from, Vector3 to)
    {
        line.startWidth = startWidth;
        line.endWidth = endWidth;

        SetAlpha(1f);

        // --- 1. Uzama: ışın kaynaktan hedefe doğru büyür ---
        float t = 0f;
        float nextCrackle = 0f;

        while (t < growDuration)
        {
            t += Time.deltaTime;
            float progress = Mathf.Clamp01(t / growDuration);

            Vector3 currentEnd = Vector3.Lerp(from, to, progress);
            BuildBolt(from, currentEnd);

            yield return null;
        }

        // --- 2. Bekleme: yerinde çatırdar ---
        t = 0f;
        nextCrackle = 0f;

        while (t < holdDuration)
        {
            t += Time.deltaTime;
            nextCrackle -= Time.deltaTime;

            if (nextCrackle <= 0f)
            {
                BuildBolt(from, to);
                nextCrackle = 1f / crackleRate;
            }

            yield return null;
        }

        // --- 3. Sönme ---
        t = 0f;

        while (t < fadeDuration)
        {
            t += Time.deltaTime;
            float a = 1f - Mathf.Clamp01(t / fadeDuration);

            SetAlpha(a);

            // Sönerken de hafif titresin
            if (Random.value > 0.6f) BuildBolt(from, to);

            yield return null;
        }

        gameObject.SetActive(false);
        running = null;
    }

    // İki nokta arasında zikzaklı bir yol oluştur
    void BuildBolt(Vector3 from, Vector3 to)
    {
        Vector3 direction = to - from;
        Vector3 perpendicular = new Vector3(-direction.y, direction.x, 0f).normalized;

        for (int i = 0; i < segments; i++)
        {
            float progress = i / (float)(segments - 1);
            Vector3 point = Vector3.Lerp(from, to, progress);

            // Uçlarda sapma olmasın, ortada maksimum olsun
            float taper = Mathf.Sin(progress * Mathf.PI);
            float offset = Random.Range(-jaggedness, jaggedness) * taper;

            point += perpendicular * offset;
            point.z = -0.5f;   // kristallerin önünde çizilsin

            line.SetPosition(i, point);
        }
    }

    void SetAlpha(float a)
    {
        Color c1 = coreColor;  c1.a = a;
        Color c2 = glowColor;  c2.a = a;

        line.startColor = c1;
        line.endColor = c2;
    }
}