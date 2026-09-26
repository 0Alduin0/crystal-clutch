using System;

namespace LevelLab
{
    // Engel katmanının simülasyondaki karşılığı. Sayılar ObstacleType'la aynı
    // ama ona bağlı değil: çekirdek Unity'siz de derlenebilsin diye (bkz. LabBoard).
    public enum LabObstacle : byte
    {
        None = 0,
        Transparent = 1,
        Solid = 2,
        Block = 3
    }

    // Bir levelin simülasyonun ihtiyaç duyduğu hâli. LevelData'dan ya da doğrudan
    // satırlardan kurulur - üretici de aday levelleri bununla tarif edecek.
    public sealed class LabLevel
    {
        public string Name = "";
        public int Width = 8;
        public int Height = 8;

        // OYUNDA GERÇEKTEN kullanılan çeşit sayısı. LevelData'daki değer değil:
        // Board onu kristal prefab sayısıyla kırpıyor (RefreshVariety).
        // Kurarken EffectiveVariety'den geçir.
        public int Variety = 4;

        public int MaxMoves = 20;
        public int TransparentHits = 1;
        public int SolidHits = 1;
        public int BlockHits = 3;

        // LevelData.refillLuck: yeni düşen kristallerin ne kadarı tamamen rastgele
        public double RefillLuck = 1.0;

        // y * Width + x. y = 0 tahtanın EN ALT sırası - Board'la aynı.
        public LabObstacle[] Obstacles = new LabObstacle[64];

        // Board.RefreshVariety'nin aynısı: prefab sayısıyla kırp, 3'ün altına inme
        public static int EffectiveVariety(int levelVariety, int crystalKinds)
        {
            return Math.Max(3, Math.Min(levelVariety, crystalKinds));
        }

        // rows: LevelData.obstacleRows - İLK satır tahtanın EN ÜST sırası
        // Sadece yerleşimi kurar; hamle, renk, vuruş gibi ayarları çağıran atar:
        //   LabLevel l = LabLevel.FromRows(...);  l.MaxMoves = 25; ...
        public static LabLevel FromRows(string name, int width, int height, string[] rows)
        {
            LabLevel level = new LabLevel
            {
                Name = name,
                Width = width,
                Height = height,
                Obstacles = new LabObstacle[width * height]
            };

            if (rows == null) return level;

            for (int y = 0; y < height; y++)
            {
                int row = height - 1 - y;
                if (row >= rows.Length || string.IsNullOrEmpty(rows[row])) continue;

                string line = rows[row];

                for (int x = 0; x < width && x < line.Length; x++)
                    level.Obstacles[y * width + x] = ParseChar(line[x]);
            }

            return level;
        }

        // LevelData.ParseChar'ın aynısı - eş anlamlı karakterler dahil
        public static LabObstacle ParseChar(char c)
        {
            switch (char.ToLowerInvariant(c))
            {
                case 's':
                case 'x':
                case 'ş':
                case '1':
                    return LabObstacle.Transparent;

                case 'k':
                case '#':
                case '2':
                    return LabObstacle.Solid;

                case 'b':
                case '3':
                    return LabObstacle.Block;
            }

            return LabObstacle.None;
        }

        public static char ToChar(LabObstacle o)
        {
            switch (o)
            {
                case LabObstacle.Transparent: return 's';
                case LabObstacle.Solid:       return 'k';
                case LabObstacle.Block:       return 'b';
            }

            return '.';
        }

        // LevelData.obstacleRows biçiminde geri yaz (üretici asset oluştururken)
        public string[] ToRows()
        {
            string[] rows = new string[Height];
            char[] line = new char[Width];

            for (int row = 0; row < Height; row++)
            {
                int y = Height - 1 - row;

                for (int x = 0; x < Width; x++)
                    line[x] = ToChar(Obstacles[y * Width + x]);

                rows[row] = new string(line);
            }

            return rows;
        }

        public int CountObstacles(LabObstacle type)
        {
            int n = 0;

            foreach (LabObstacle o in Obstacles)
                if (o == type) n++;

            return n;
        }

        public int CountAllObstacles()
        {
            int n = 0;

            foreach (LabObstacle o in Obstacles)
                if (o != LabObstacle.None) n++;

            return n;
        }

        // Levelin toplam "canı": hepsini kırmak için gereken vuruş sayısı.
        // Katı önce şeffafa dönüyor, o yüzden ikisinin toplamı kadar vuruş ister.
        // Blok doğrudan boşalıyor.
        public int TotalHitPoints
        {
            get
            {
                int t = Math.Max(1, TransparentHits);
                int s = Math.Max(1, SolidHits);
                int b = Math.Max(1, BlockHits);

                return CountObstacles(LabObstacle.Transparent) * t +
                       CountObstacles(LabObstacle.Solid) * (s + t) +
                       CountObstacles(LabObstacle.Block) * b;
            }
        }

        // Üretici varyant denerken: aynı level, ayrı dizi
        public LabLevel Clone()
        {
            LabLevel copy = (LabLevel)MemberwiseClone();
            copy.Obstacles = (LabObstacle[])Obstacles.Clone();
            return copy;
        }
    }

    // Simülasyonun zarı. System.Random yerine xorshift: tüm durum tek bir ulong,
    // yani kopyalamak bedava. Bot her aday hamleyi tahtanın bir kopyasında
    // denerken zarı da kopyalıyor.
    public struct LabRng
    {
        ulong state;

        public LabRng(ulong seed)
        {
            state = Mix(seed);
            if (state == 0) state = 0x9E3779B97F4A7C15UL;
        }

        public ulong Next()
        {
            // xorshift64*
            state ^= state >> 12;
            state ^= state << 25;
            state ^= state >> 27;
            return state * 0x2545F4914F6CDD1DUL;
        }

        // [0, n)
        public int Range(int n)
        {
            return (int)((Next() >> 33) % (ulong)n);
        }

        // [0, 1)
        public double NextDouble()
        {
            return (Next() >> 11) * (1.0 / (1UL << 53));
        }

        // splitmix64 - yan yana tohumlar (run 0, 1, 2...) birbirine benzemesin
        public static ulong Mix(ulong x)
        {
            x += 0x9E3779B97F4A7C15UL;
            x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
            x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
            return x ^ (x >> 31);
        }

        public static ulong Seed(ulong a, ulong b, ulong c)
        {
            return Mix(Mix(Mix(a) ^ b) ^ c);
        }
    }
}
