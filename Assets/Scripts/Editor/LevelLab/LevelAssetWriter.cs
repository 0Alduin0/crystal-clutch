using System;
using System.IO;
using LevelLab;
using UnityEditor;
using UnityEngine;

// Üreticinin sonucunu LevelData asset'ine çevirip LevelSet'e koyar.
// Pencere de bunu kullanıyor; tek yerde dursun ki alan eşlemesi şaşmasın.
public static class LevelAssetWriter
{
    public const string Folder = "Assets/Levels";

    // Slotu doldurur. index 1'den başlar.
    //   - index LevelSet'in sonundaysa (Count + 1) sona ekler.
    //   - index mevcut bir slotsa yenisiyle DEĞİŞTİRİR. Eski asset dosyası
    //     silinmez: geri almak istersen LevelSet'te o slota eskisini sürükle.
    // Arada boşluk bırakacak bir index reddedilir.
    public static LevelData Write(LevelSet set, int index, GeneratedLevel g)
    {
        if (set == null) throw new ArgumentNullException(nameof(set));

        int count = set.Count;

        if (index < 1 || index > count + 1)
            throw new ArgumentOutOfRangeException(nameof(index),
                $"Level {index} yazılamaz: LevelSet'te {count} level var, en fazla {count + 1}. slot doldurulabilir.");

        bool replacing = index <= count;

        LevelData data = ScriptableObject.CreateInstance<LevelData>();
        Fill(data, g);

        string baseName = replacing ? $"Level_{index}_yeni" : $"Level_{index}";
        string path = AssetDatabase.GenerateUniqueAssetPath($"{Folder}/{baseName}.asset");

        data.name = Path.GetFileNameWithoutExtension(path);
        AssetDatabase.CreateAsset(data, path);

        Undo.RecordObject(set, "Level üret");

        if (replacing)
        {
            set.levels[index - 1] = data;
        }
        else
        {
            LevelData[] grown = new LevelData[count + 1];
            if (set.levels != null) Array.Copy(set.levels, grown, count);
            grown[count] = data;
            set.levels = grown;
        }

        EditorUtility.SetDirty(set);
        AssetDatabase.SaveAssets();

        return data;
    }

    // Board'un okuduğu her alan: LevelLabWindow.ToLab'ın tersi
    static void Fill(LevelData d, GeneratedLevel g)
    {
        LabLevel l = g.Level;

        d.width = l.Width;
        d.height = l.Height;
        d.crystalVariety = l.Variety;
        d.maxMoves = l.MaxMoves;
        d.refillLuck = (float)l.RefillLuck;
        d.obstacleRows = l.ToRows();
        d.transparentHits = l.TransparentHits;
        d.solidHits = l.SolidHits;
        d.blockHits = l.BlockHits;
        d.blockTheme = (BlockTheme)g.Spec.BlockTheme;
    }
}
