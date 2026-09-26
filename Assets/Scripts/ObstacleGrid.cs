using System;
using UnityEngine;

// Tahtanın ENGEL KATMANI. Kristal gridinin (Board.crystals) üstünde duran ikinci
// bir grid: hangi karede hangi engel var, o kademede kaç vuruşu kaldı, hangi
// görsel objeyi kullanıyor.
//
// MonoBehaviour değil - sahnede karşılığı yok, Board bunu bir alan olarak tutuyor.
public class ObstacleGrid
{
    ObstacleType[,] types;
    int[,] hitsLeft;       // bu kademede kalan vuruş
    Obstacle[,] views;

    // Karenin BAŞLANGIÇTAKİ türü. Sayaçlar buna göre tutuluyor: katı bir engel
    // kırılınca şeffafa dönüyor ama "katı" sütununda sayılmaya devam ediyor.
    // Alternatifi, o an tahtada ne varsa onu saymaktı - o zaman bir taş kırılınca
    // şeffaf sayacı ARTAR, oyuncu ilerlediğini değil gerilediğini görürdü.
    ObstacleType[,] origins;

    int width;
    int height;

    int transparentHits = 1;
    int solidHits = 1;
    int blockHits = 3;

    // Engel kurarken lazım - Init'te alınıp saklanıyor
    GameObject prefab;
    Transform parent;
    Func<int, int, Vector3> gridToWorld;
    float zOffset;

    // Blokların görünüşü (levelden). Hedef göstergesi de ikonunu buna göre seçiyor.
    public BlockTheme BlockTheme { get; private set; }

    // Tahtada kalan engel sayısı (her tür sayılır).
    // Level modunda hedef bunu sıfıra indirmek.
    public int Remaining { get; private set; }

    // Level başında kaç engel vardı
    public int Total { get; private set; }

    // Tür bazında sayaçlar - üst bardaki gösterge bunları yazıyor.
    // "Kalan" değerler yalnızca AZALIR: bir kare ancak tamamen temizlendiğinde
    // kendi sütunundan düşer, ara kademeler sayacı oynatmaz.
    public int TotalTransparent { get; private set; }
    public int TotalSolid { get; private set; }
    public int TotalBlock { get; private set; }
    public int RemainingTransparent { get; private set; }
    public int RemainingSolid { get; private set; }
    public int RemainingBlock { get; private set; }

    public bool HasAny => Remaining > 0;

    // ==================== KURULUM ====================

    public void Init(int width, int height, GameObject prefab, Transform parent,
                     Func<int, int, Vector3> gridToWorld, float zOffset)
    {
        this.width = width;
        this.height = height;
        this.prefab = prefab;
        this.parent = parent;
        this.gridToWorld = gridToWorld;
        this.zOffset = zOffset;

        types = new ObstacleType[width, height];
        hitsLeft = new int[width, height];
        views = new Obstacle[width, height];
        origins = new ObstacleType[width, height];

        Remaining = Total = 0;
        TotalTransparent = TotalSolid = TotalBlock = 0;
        RemainingTransparent = RemainingSolid = RemainingBlock = 0;
    }

    // Yerleşimi level asset'inden kur
    public void ApplyLevelLayout(LevelData level)
    {
        if (level == null || !level.HasObstacleLayout) return;

        transparentHits = Mathf.Max(1, level.transparentHits);
        solidHits = Mathf.Max(1, level.solidHits);
        blockHits = Mathf.Max(1, level.blockHits);
        BlockTheme = level.blockTheme;

        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                Place(x, y, level.GetObstacleAt(x, y));

        // Level başındaki durum hedef göstergesinin referansı
        Total = Remaining;
        TotalSolid = RemainingSolid;
        TotalTransparent = RemainingTransparent;
        TotalBlock = RemainingBlock;
    }

    void Place(int x, int y, ObstacleType type)
    {
        if (!InBounds(x, y) || type == ObstacleType.None) return;

        types[x, y] = type;
        origins[x, y] = type;
        hitsLeft[x, y] = HitsFor(type);

        Remaining++;

        switch (type)
        {
            case ObstacleType.Solid: RemainingSolid++; break;
            case ObstacleType.Block: RemainingBlock++; break;
            default:                 RemainingTransparent++; break;
        }

        if (prefab == null)
        {
            WarnMissingPrefab();
            return;
        }

        GameObject obj = UnityEngine.Object.Instantiate(prefab, WorldPos(x, y),
                                                       Quaternion.identity, parent);
        obj.name = $"Obstacle_{x}_{y}";

        Obstacle view = obj.GetComponent<Obstacle>();

        if (view != null)
        {
            // Tema sprite seçiminden ÖNCE: SetState bloğun görselini buna göre alıyor
            view.Theme = BlockTheme;
            view.SetState(type, 0, HitsFor(type));
            views[x, y] = view;
        }
    }

    Vector3 WorldPos(int x, int y) => gridToWorld(x, y) + new Vector3(0, 0, zOffset);

    int HitsFor(ObstacleType type)
    {
        switch (type)
        {
            case ObstacleType.Solid:       return solidHits;
            case ObstacleType.Transparent: return transparentHits;
            case ObstacleType.Block:       return blockHits;
        }

        return 0;
    }

    // ==================== SORGU ====================

    bool InBounds(int x, int y) => x >= 0 && x < width && y >= 0 && y < height;

    public ObstacleType TypeAt(int x, int y)
    {
        if (types == null || !InBounds(x, y)) return ObstacleType.None;
        return types[x, y];
    }

    // Kareyi kapatan engel: katı ya da blok. Kristal yok, kare oynanmaz,
    // üstünden kristal düşemez.
    public bool IsBlocking(int x, int y)
    {
        ObstacleType type = TypeAt(x, y);
        return type == ObstacleType.Solid || type == ObstacleType.Block;
    }

    // Kırılırken uçacak parçaların rengi. HASARDAN ÖNCE okunmalı - engel
    // kırılınca görsel objesi yok ediliyor.
    public Color PopColorAt(int x, int y)
    {
        if (views == null || !InBounds(x, y) || views[x, y] == null) return Color.white;
        return views[x, y].PopColor;
    }

    // ==================== HASAR ====================

    // Bir kareye vuruş uygula. Kademe düşerse Solid -> Transparent -> None,
    // Block -> None.
    public ObstacleHit Damage(int x, int y)
    {
        if (types == null || !InBounds(x, y)) return ObstacleHit.None;

        ObstacleType current = types[x, y];
        if (current == ObstacleType.None) return ObstacleHit.None;

        hitsLeft[x, y]--;

        // Henüz kademe düşmedi - sadece çatladı
        if (hitsLeft[x, y] > 0)
        {
            Obstacle cracked = views[x, y];

            if (cracked != null)
            {
                int total = HitsFor(current);

                cracked.SetState(current, total - hitsLeft[x, y], total);
                cracked.PlayHit();
            }

            return ObstacleHit.Damaged;
        }

        ObstacleType next = current == ObstacleType.Solid
            ? ObstacleType.Transparent
            : ObstacleType.None;

        types[x, y] = next;
        hitsLeft[x, y] = HitsFor(next);

        Obstacle view = views[x, y];

        if (next == ObstacleType.None)
        {
            Remaining--;

            // Sayaçtan düşme anı: kare TAMAMEN temizlendi. Katı bir kare önce
            // şeffafa dönüyor ama o ara adımda hiçbir sayaç oynamıyor.
            switch (origins[x, y])
            {
                case ObstacleType.Solid: RemainingSolid--; break;
                case ObstacleType.Block: RemainingBlock--; break;
                default:                 RemainingTransparent--; break;
            }

            views[x, y] = null;

            if (view != null) view.PlayBreak();

            return ObstacleHit.Broken;
        }

        if (view != null)
        {
            view.SetState(next, 0, HitsFor(next));
            view.PlayDowngrade();
        }

        return ObstacleHit.Downgraded;
    }

    // Sahne kapanırken / tahta sıfırlanırken kalan görselleri temizle
    public void Clear()
    {
        if (views == null) return;

        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
            {
                if (views[x, y] == null) continue;

                UnityEngine.Object.Destroy(views[x, y].gameObject);
                views[x, y] = null;
            }

        Remaining = 0;
    }

    static bool warnedPrefab;

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
    static void WarnMissingPrefab()
    {
        if (warnedPrefab) return;
        warnedPrefab = true;

        Debug.LogWarning("[ObstacleGrid] Board'daki 'Obstacle Prefab' alanı boş - " +
                         "engeller çalışıyor ama GÖRÜNMÜYOR. Katı kareler oyuncuya " +
                         "sebepsiz kilitli görünür.");
    }
}
