using UnityEngine;

// Oyundaki levellerin sırası. Tek bir asset olarak duruyor çünkü iki ayrı
// sahnenin de aynı listeye bakması gerekiyor: Game sahnesi hangi leveli
// yükleyeceğini, menü ise oyuncunun bitirip bitirmediğini buradan öğreniyor.
//
// Yeni level eklemek: Level_ asset'ini oluştur, bu listenin SONUNA ekle.
// Başka hiçbir yere dokunman gerekmiyor - sıra bu dizinin sırası.
//
// Oluşturmak için: Project → sağ tık → Create → Match3 → Level Set
[CreateAssetMenu(fileName = "LevelSet", menuName = "Match3/Level Set")]
public class LevelSet : ScriptableObject
{
    [Tooltip("Level 1'den başlayarak sıralı liste. Sıralama oyundaki sıradır.")]
    public LevelData[] levels;

    public int Count => levels != null ? levels.Length : 0;

    // index dışarı taşarsa null döner - çağıran taraf bunu "bölüm kalmadı"
    // diye okuyor, o yüzden sessizce clamp etmiyoruz.
    public LevelData Get(int index)
    {
        if (levels == null || index < 0 || index >= levels.Length) return null;
        return levels[index];
    }
}
