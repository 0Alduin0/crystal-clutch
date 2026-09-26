using System.Collections.Generic;
using UnityEngine;

// Elektrik ışınlarını havuzdan dağıtır.
public class LightningManager : MonoBehaviour
{
    public static LightningManager Instance;

    [SerializeField] LightningBolt boltPrefab;

    [Tooltip("Sadece renk bombasında kullanılıyor - 16 fazlasıyla yeterli")]
    [SerializeField] int poolSize = 16;

    Queue<LightningBolt> pool = new Queue<LightningBolt>();

    void Awake()
    {
        Instance = this;

        if (boltPrefab == null) return;

        for (int i = 0; i < poolSize; i++)
        {
            LightningBolt b = Instantiate(boltPrefab, transform);
            b.gameObject.SetActive(false);
            pool.Enqueue(b);
        }
    }

    public void Fire(Vector3 from, Vector3 to)
    {
        if (pool.Count == 0) return;

        LightningBolt b = pool.Dequeue();
        pool.Enqueue(b);   // döngüsel kullanım - süre dolunca kendini kapatıyor

        b.Fire(from, to);
    }
}