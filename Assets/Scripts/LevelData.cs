using UnityEngine;

// Her level ayrı bir asset dosyası olarak Project'te durur.
// Oluşturmak için: Project → sağ tık → Create → Match3 → Level
//
// LEVELİN HEDEFİ TEK: tahtadaki bütün engelleri kırmak. Kristal toplama hedefi
// yok - kristaller sadece engellere ulaşmanın aracı. Engel yerleşimi boş
// bırakılmış bir level kazanılamaz; Inspector uyarır.
[CreateAssetMenu(fileName = "Level_", menuName = "Match3/Level")]
public class LevelData : ScriptableObject
{
    [Header("Grid")]
    public int width = 8;
    public int height = 8;

    [Tooltip("Bu levelde kaç farklı kristal çeşidi kullanılsın. CrystalType sırasına göre ilk N tanesi kullanılır (zorluk ayarı)")]
    [Range(3, 4)]
    public int crystalVariety = 4;

    [Header("Kurallar")]
    public int maxMoves = 20;

    [Tooltip("Yukarıdan düşen yeni kristallerin ne kadarı TAMAMEN rastgele gelsin.\n\n" +
             "Kalanı, düştüğü yerde hazır eşleşme kurmayacak renkte gelir. Zincir " +
             "yine olur ama taşların kaymasından doğar, gökten yağmaz.\n\n" +
             "1 = eski davranış: 4 renkte bile tek hamle tahtayı defalarca patlatır.\n" +
             "0 = hiç şans yok.\n" +
             "Erken levellerde yüksek tut (havai fişek), ilerledikçe düşür.")]
    [Range(0f, 1f)]
    public float refillLuck = 0.2f;

    // ==================== ENGELLER ====================
    // Levelin tek hedefi bunlar. Inspector 2 boyutlu dizi çizemiyor, o yüzden
    // yerleşim satır satır string olarak tutuluyor. Elle yazmak zorunda değilsin:
    // LevelData'nın Inspector'ında tıklanabilir bir engel tahtası var.

    [Header("Engeller (levelin hedefi)")]
    [Tooltip("Satır başına bir metin, uzunluğu 'width' kadar.\n" +
             "İLK satır tahtanın EN ÜST sırası (ekranda gördüğün gibi).\n\n" +
             ".  = engel yok\n" +
             "s  = ŞEFFAF (arkasındaki kristal görünür, kare oynanır)\n" +
             "k  = KATI (kristal yok, kare oynanamaz)\n" +
             "b  = BLOK (katı gibi ama çok vuruşlu, kırılınca doğrudan boşalır)\n\n" +
             "Boş bırakırsan levelin hedefi olmaz ve level kazanılamaz.")]
    public string[] obstacleRows;

    [Tooltip("Şeffaf engel kaç vuruşta kırılıp kare normale dönsün")]
    [Range(1, 5)]
    public int transparentHits = 1;

    [Tooltip("Katı engel kaç vuruşta şeffafa dönsün")]
    [Range(1, 5)]
    public int solidHits = 1;

    [Tooltip("Blok kaç vuruşta kırılsın. Görselin 3 kademesi (sağlam, çatlak, çok " +
             "çatlak) vuruşlara orantılı dağılır - 3 vuruşta her vuruş bir kademe.")]
    [Range(1, 5)]
    public int blockHits = 3;

    [Tooltip("Blokların görünüşü. Kural ikisinde de aynı, sadece tema.")]
    public BlockTheme blockTheme = BlockTheme.Lav;

    public bool HasObstacleLayout => obstacleRows != null && obstacleRows.Length > 0;

    // Yerleşimdeki karakterler. Yazarken bunları kullan; okurken eş anlamlıları
    // da (X, #, 1, 2 ...) kabul ediyoruz - elle düzenleyen takılmasın.
    public const char CharNone = '.';
    public const char CharTransparent = 's';
    public const char CharSolid = 'k';
    public const char CharBlock = 'b';

    // (x, y) grid koordinatı. y = 0 tahtanın EN ALT sırası, satır dizisinde ise
    // ilk satır EN ÜST sıra - o yüzden çeviriyoruz.
    public ObstacleType GetObstacleAt(int x, int y)
    {
        if (!HasObstacleLayout) return ObstacleType.None;
        if (x < 0 || x >= width || y < 0 || y >= height) return ObstacleType.None;

        int row = height - 1 - y;
        if (row < 0 || row >= obstacleRows.Length) return ObstacleType.None;

        string line = obstacleRows[row];
        if (string.IsNullOrEmpty(line) || x >= line.Length) return ObstacleType.None;

        return ParseChar(line[x]);
    }

    public static ObstacleType ParseChar(char c)
    {
        switch (char.ToLowerInvariant(c))
        {
            case 's':
            case 'x':
            case 'ş':
            case '1':
                return ObstacleType.Transparent;

            case 'k':
            case '#':
            case '2':
                return ObstacleType.Solid;

            case 'b':
            case '3':
                return ObstacleType.Block;
        }

        return ObstacleType.None;
    }

    public static char ToChar(ObstacleType type)
    {
        switch (type)
        {
            case ObstacleType.Transparent: return CharTransparent;
            case ObstacleType.Solid:       return CharSolid;
            case ObstacleType.Block:       return CharBlock;
        }

        return CharNone;
    }
}
