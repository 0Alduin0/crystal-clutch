using System.Collections.Generic;
using UnityEngine;

// Kristalleri Instantiate/Destroy etmek yerine geri dönüştürür.
// Mobilde GC duraklamalarını önler.
public class CrystalPool : MonoBehaviour
{
    public static CrystalPool Instance;

    [Tooltip("Sıra ÖNEMLİ: CrystalType enum'ıyla birebir aynı olmalı - Kızıl, Mor, Mavi, Yeşil")]
    [SerializeField] GameObject[] crystalPrefabs;

    [Tooltip("Her kristal çeşidinden kaç tane önceden üretilsin. 8x8 tahtada aynı anda " +
             "en fazla 64 kristal olur; renk bombası tek çeşitten çok sayıda kristali " +
             "birden patlattığı için çeşit başına biraz pay bırakılıyor.")]
    [SerializeField] int perTypeSize = 16;

    // Her kristal çeşidi için ayrı kuyruk
    Dictionary<CrystalType, Queue<Crystal>> pools = new Dictionary<CrystalType, Queue<Crystal>>();

    public int PrefabCount => crystalPrefabs != null ? crystalPrefabs.Length : 0;

    void Awake()
    {
        Instance = this;
        Prewarm();
    }

    // Oyun başında havuzu doldur
    void Prewarm()
    {
        for (int i = 0; i < crystalPrefabs.Length; i++)
        {
            CrystalType type = (CrystalType)i;
            Queue<Crystal> queue = new Queue<Crystal>(perTypeSize);
            pools[type] = queue;

            for (int n = 0; n < perTypeSize; n++)
            {
                GameObject obj = Instantiate(crystalPrefabs[i], transform);
                obj.SetActive(false);
                queue.Enqueue(obj.GetComponent<Crystal>());
            }
        }
    }

    Queue<Crystal> PoolFor(CrystalType type)
    {
        if (!pools.TryGetValue(type, out Queue<Crystal> queue))
        {
            queue = new Queue<Crystal>();
            pools[type] = queue;
        }

        return queue;
    }

    // Havuzdan bir kristal al
    public Crystal Get(CrystalType type, Vector3 position, Transform parent)
    {
        Queue<Crystal> queue = PoolFor(type);

        Crystal f = queue.Count > 0
            ? queue.Dequeue()
            // Havuz boşaldı - yenisini üret (güvenlik)
            : Instantiate(crystalPrefabs[(int)type]).GetComponent<Crystal>();

        Transform tf = f.transform;
        tf.SetParent(parent, false);
        tf.position = position;

        f.gameObject.SetActive(true);

        // Boyut/rotasyon/sprite'ı garantiye al. ResetCrystal artık tamamen O(1)
        // (takip edilen tween'leri kill ediyor, tüm listeyi taramıyor).
        f.ResetCrystal();
        f.Type = type;

        return f;
    }

    // Kristalyi havuza geri gönder
    public void Return(Crystal f)
    {
        if (f == null) return;

        // ResetCrystal çalışan tween'leri durdurup boyut/sprite/rotasyonu geri alır.
        // Eskiden burada ayrıca transform.DOKill() çağrılıyordu - ResetCrystal zaten
        // yapıyor, ikinci çağrı tüm aktif tween listesini boşuna bir kez daha tarıyordu.
        f.ResetCrystal();
        f.gameObject.SetActive(false);
        f.transform.SetParent(transform, false);

        PoolFor(f.Type).Enqueue(f);
    }
}
