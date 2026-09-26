using System;
using System.Collections.Generic;

namespace LevelLab
{
    // Botun oyun gücü. Zorluk ölçümü ORTALAMA ile yapılıyor; diğer ikisi
    // "iyi oyuncu ne yapar, yeni başlayan ne yapar" aralığını görmek için.
    public enum BotSkill
    {
        Usta = 0,       // her hamlede en iyisini seçer
        Ortalama = 1,   // çoğunlukla iyi hamle, arada gözden kaçırır
        Acemi = 2,      // hamlelerin önemli kısmı gelişigüzel
        Rastgele = 3    // hiç düşünmeden - alt sınır: "level kendiliğinden geçiliyor mu"
    }

    // İnsan gibi oynayan (kusursuz olmayan) bot.
    //
    // Her geçerli takası tahtanın bir KOPYASINDA dener ve puanlar. Kopyanın zarı
    // gerçek oyunun zarından ayrı: bot yukarıdan düşecek kristalleri bilmiyor,
    // tıpkı oyuncu gibi. Mevcut kristallerden doğacak zinciri ise görüyor - bu
    // insana göre küçük bir avantaj.
    //
    // Puan: engele işleyen vuruş en baskın terim (levelin tek hedefi o), sonra
    // güç üretmek, sonra engele yakın oynamak. İnsan da böyle düşünüyor:
    // "engeli kırayım, olmuyorsa 4'lü kurayım, o da yoksa engelin dibinde oynayayım".
    public sealed class LabBot
    {
        const double HitWeight = 10;
        const double WinBonus = 1000;
        const double CrossWeight = 3;
        const double BombWeight = 4;
        const double ColorBombWeight = 6;
        const double ClearWeight = 0.05;
        const double DistanceWeight = 0.5;

        public BotSkill Skill { get; private set; }

        LabRng rng;

        readonly LabBoard trial = new LabBoard();
        readonly List<int> moveFrom = new List<int>(128);
        readonly List<int> moveTo = new List<int>(128);
        readonly List<double> scores = new List<double>(128);

        // Her karenin en yakın engele uzaklığı (Manhattan)
        int[] distance = Array.Empty<int>();
        readonly Queue<int> bfs = new Queue<int>();

        public LabBot(BotSkill skill, ulong seed)
        {
            Reset(skill, seed);
        }

        public void Reset(BotSkill skill, ulong seed)
        {
            Skill = skill;
            rng = new LabRng(seed);
        }

        // Bir hamle seçip oynar. Oynanabilir takas yoksa false.
        public bool PlayOne(LabBoard board)
        {
            CollectMoves(board);
            if (moveFrom.Count == 0) return false;

            int pick = Choose(board);
            board.TryMove(moveFrom[pick], moveTo[pick]);

            return true;
        }

        // Her kristal dört yöne sürüklenebilir. A'yı B'ye sürüklemek ile B'yi
        // A'ya sürüklemek farklı hamle: güç, parmağın bıraktığı karede doğuyor.
        void CollectMoves(LabBoard board)
        {
            moveFrom.Clear();
            moveTo.Clear();

            int w = board.Width, h = board.Height;

            for (int a = 0; a < w * h; a++)
            {
                if (!board.IsPlayable(a)) continue;

                int x = board.XOf(a), y = board.YOf(a);

                if (x + 1 < w) TryAdd(board, a, board.Idx(x + 1, y));
                if (x - 1 >= 0) TryAdd(board, a, board.Idx(x - 1, y));
                if (y + 1 < h) TryAdd(board, a, board.Idx(x, y + 1));
                if (y - 1 >= 0) TryAdd(board, a, board.Idx(x, y - 1));
            }
        }

        void TryAdd(LabBoard board, int a, int b)
        {
            if (!board.IsLegalMove(a, b)) return;

            moveFrom.Add(a);
            moveTo.Add(b);
        }

        int Choose(LabBoard board)
        {
            if (Skill == BotSkill.Rastgele) return rng.Range(moveFrom.Count);

            // Acemi: hamlelerin yarısına yakınını düşünmeden yapıyor
            if (Skill == BotSkill.Acemi && rng.NextDouble() < 0.4)
                return rng.Range(moveFrom.Count);

            Score(board);

            if (Skill == BotSkill.Usta) return ArgMax();

            // Softmax: puanı yüksek hamle çok daha olası ama tek seçenek değil.
            // Sıcaklık 4: bir engel vuruşu (10 puan) farkı ~12 kat olasılık demek.
            double temperature = Skill == BotSkill.Ortalama ? 4.0 : 7.0;

            return SoftMax(temperature);
        }

        void Score(LabBoard board)
        {
            scores.Clear();
            ComputeDistances(board);

            // Tüm adaylar AYNI zarla deneniyor: fark hamleden gelsin, şanstan değil
            ulong evalSeed = rng.Next();

            for (int k = 0; k < moveFrom.Count; k++)
            {
                trial.CopyFrom(board);
                trial.Rng = new LabRng(evalSeed);

                trial.TryMove(moveFrom[k], moveTo[k]);

                double s = HitWeight * (trial.HitsDealt - board.HitsDealt);

                if (trial.Won) s += WinBonus;

                s += CrossWeight * (trial.CrossMade - board.CrossMade);
                s += BombWeight * (trial.BombMade - board.BombMade);
                s += ColorBombWeight * (trial.ColorBombMade - board.ColorBombMade);
                s += ClearWeight * (trial.Cleared - board.Cleared);

                s -= DistanceWeight * Math.Min(distance[moveFrom[k]], distance[moveTo[k]]);

                scores.Add(s);
            }
        }

        int ArgMax()
        {
            double best = double.MinValue;
            int bestIndex = 0;
            int ties = 0;

            for (int k = 0; k < scores.Count; k++)
            {
                if (scores[k] > best)
                {
                    best = scores[k];
                    bestIndex = k;
                    ties = 1;
                }
                else if (scores[k] == best)
                {
                    // Eşitlikte rastgele - hep ilk bulunanı seçmek tahtanın
                    // sol altına takıntılı bir bot yaratırdı
                    ties++;
                    if (rng.Range(ties) == 0) bestIndex = k;
                }
            }

            return bestIndex;
        }

        int SoftMax(double temperature)
        {
            double max = double.MinValue;
            foreach (double s in scores) if (s > max) max = s;

            double sum = 0;
            for (int k = 0; k < scores.Count; k++)
            {
                double e = Math.Exp((scores[k] - max) / temperature);
                scores[k] = e;
                sum += e;
            }

            double r = rng.NextDouble() * sum;

            for (int k = 0; k < scores.Count; k++)
            {
                r -= scores[k];
                if (r <= 0) return k;
            }

            return scores.Count - 1;
        }

        // Çok kaynaklı BFS: her kareden en yakın engele adım sayısı
        void ComputeDistances(LabBoard board)
        {
            int n = board.Width * board.Height;
            if (distance.Length != n) distance = new int[n];

            bfs.Clear();

            for (int i = 0; i < n; i++)
            {
                if (board.Obstacle[i] != LabObstacle.None)
                {
                    distance[i] = 0;
                    bfs.Enqueue(i);
                }
                else
                {
                    distance[i] = int.MaxValue;
                }
            }

            // Engel kalmadıysa uzaklık anlamsız - hepsi 0
            if (bfs.Count == 0)
            {
                Array.Clear(distance, 0, n);
                return;
            }

            while (bfs.Count > 0)
            {
                int c = bfs.Dequeue();
                int x = board.XOf(c), y = board.YOf(c);
                int d = distance[c] + 1;

                if (x + 1 < board.Width) Relax(board.Idx(x + 1, y), d);
                if (x - 1 >= 0) Relax(board.Idx(x - 1, y), d);
                if (y + 1 < board.Height) Relax(board.Idx(x, y + 1), d);
                if (y - 1 >= 0) Relax(board.Idx(x, y - 1), d);
            }
        }

        void Relax(int i, int d)
        {
            if (distance[i] <= d) return;

            distance[i] = d;
            bfs.Enqueue(i);
        }
    }
}
