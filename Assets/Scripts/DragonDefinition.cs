using System;
using UnityEngine;

// Bir ejderhanın tarifi: büyüme aşamaları, her aşamaya kaç yumurta gerektiği,
// hangi modelle göründüğü. Ekrandaki adı oyuncu veriyor (SaveSystem.GetDragonName).
//
// Oluşturmak için: Project -> sağ tık -> Create -> Match3 -> Ejderha
//
// İlerleme tek bir sayıdan okunuyor: bu ejderhaya bugüne kadar kaç yumurta
// yedirildi (SaveSystem.GetDragonFed). Aşama da, aşama içindeki büyüme de o
// sayıdan hesaplanıyor - ayrıca "şu aşamada" diye bir kayıt tutmuyoruz ki
// yumurta maliyetleri sonradan değişince kayıtlar tutarsız kalmasın.
[CreateAssetMenu(fileName = "Dragon_", menuName = "Match3/Ejderha")]
public class DragonDefinition : ScriptableObject
{
    [Tooltip("Kayıt anahtarı. Yayına çıktıktan sonra DEĞİŞTİRME - oyuncuların " +
             "ilerlemesi bu isimle saklanıyor.")]
    public string id = "kizil";

    // Varsayılan: 95 yumurtada son aşama. Yumurtadan çıkmak 1 yumurta - ilk
    // beslemede çıkıyor ve isim soruluyor. Sonra aralar giderek uzuyor.
    [Tooltip("Sırayla: yumurta, yavru, çocuk... İlk aşama başlangıç hâli.")]
    public DragonStage[] stages =
    {
        new DragonStage { name = "Yumurta", eggsToNext = 1 },
        new DragonStage { name = "Yavru", eggsToNext = 10 },
        new DragonStage { name = "Çocuk", eggsToNext = 14 },
        new DragonStage { name = "Ergen", eggsToNext = 18 },
        new DragonStage { name = "Genç", eggsToNext = 22 },
        new DragonStage { name = "Yetişkin", eggsToNext = 30 },
        new DragonStage { name = "Yaşlı", eggsToNext = 1 }
    };

    public int StageCount => stages != null ? stages.Length : 0;

    // Son aşamaya ulaşmak için gereken toplam yumurta
    public int EggsToMax
    {
        get
        {
            int total = 0;
            for (int i = 0; i < StageCount - 1; i++) total += Mathf.Max(1, stages[i].eggsToNext);
            return total;
        }
    }

    // Bu kadar yumurta yemiş ejderha hangi aşamada
    public int StageAt(int fed)
    {
        int threshold = 0;

        for (int i = 0; i < StageCount - 1; i++)
        {
            threshold += Mathf.Max(1, stages[i].eggsToNext);
            if (fed < threshold) return i;
        }

        return Mathf.Max(0, StageCount - 1);
    }

    // Aşama başlangıcına kadar yenen yumurta
    public int StageStart(int stage)
    {
        int total = 0;
        for (int i = 0; i < stage && i < StageCount - 1; i++) total += Mathf.Max(1, stages[i].eggsToNext);
        return total;
    }

    // Aşama içindeki ilerleme, 0-1. Son aşamada hep 1.
    public float ProgressInStage(int fed)
    {
        int stage = StageAt(fed);
        if (stage >= StageCount - 1) return 1f;

        int need = Mathf.Max(1, stages[stage].eggsToNext);
        return Mathf.Clamp01((fed - StageStart(stage)) / (float)need);
    }

    // Sıradaki aşamaya kaç yumurta kaldı (son aşamada 0)
    public int EggsLeftInStage(int fed)
    {
        int stage = StageAt(fed);
        if (stage >= StageCount - 1) return 0;

        return StageStart(stage) + Mathf.Max(1, stages[stage].eggsToNext) - fed;
    }

    public bool IsGrown(int fed) => fed >= EggsToMax;

    // Aşamanın modeli: boşsa geriye doğru ilk dolu aşamanınki. Böylece tek bir
    // modeli ölçekle büyütmek de, her aşamaya ayrı model vermek de çalışıyor.
    public GameObject ModelFor(int stage)
    {
        for (int i = Mathf.Min(stage, StageCount - 1); i >= 0; i--)
            if (stages[i].model != null) return stages[i].model;

        return null;
    }
}

[Serializable]
public class DragonStage
{
    public string name = "Yumurta";

    [Tooltip("Bu aşamadan bir sonrakine geçmek için yenmesi gereken yumurta. " +
             "Son aşamada kullanılmıyor.")]
    [Min(1)]
    public int eggsToNext = 5;

    [Tooltip("Aşamanın 3D modeli (prefab). Boş bırakırsan bir önceki aşamanın " +
             "modeli kullanılır - aynı modeli ölçekle büyütmek için.")]
    public GameObject model;

    [Tooltip("Aşamaya girildiğindeki boyut")]
    public float scaleStart = 1f;

    [Tooltip("Aşama biterken ulaştığı boyut. Beslendikçe bu ikisi arasında " +
             "büyüyor - her yumurta gözle görülür bir fark yaratsın.")]
    public float scaleEnd = 1.15f;

    [Tooltip("Aşamada sürekli oynayan efekt (aura, kıvılcım) - opsiyonel")]
    public GameObject ambientEffect;
}
