using System;
using System.Collections.Generic;

namespace LevelLab
{
    // PowerType'ın simülasyondaki karşılığı - sayılar aynı
    public enum LabPower : byte
    {
        None = 0,
        Cross = 1,
        ColorBomb = 2,
        Bomb = 3
    }

    // Board.cs'in KURALLARININ animasyonsuz, Unity'siz kopyası. Bot bununla
    // saniyede binlerce hamle oynuyor.
    //
    // ÖNEMLİ: Board'da bir kural değişirse burası da değişmeli. Her metodun
    // başında Board'daki karşılığı yazıyor; aynı sırayla, aynı koşullarla
    // yazıldı. Bilinçli farklar yalnızca şunlar:
    //   - Animasyon, ses, efekt yok.
    //   - Kristal nesnesi yok: kare başına tip + güç tutuluyor. Board'daki
    //     HashSet<Crystal>'lar burada kare maskesi. Bir cascade adımının
    //     içinde kristaller yer değiştirmediği için ikisi aynı şey.
    //   - Havuzdan gelen kristalin eski BombTargetType'ı bilinemiyor; güç
    //     doğarken rastgele bir renk veriyoruz (sadece nadir kombolarda okunuyor).
    //   - Alt bardaki marketten alınan power-up'lar yok - zorluk onlarsız ölçülüyor.
    public sealed class LabBoard
    {
        const int SettleStepLimit = 60;    // Board.SettleStepLimit
        const int LayoutRetryLimit = 30;   // Board.LayoutRetryLimit

        sealed class Group
        {
            public readonly List<int> Cells = new List<int>(8);
            public bool Horizontal;

            public int Count => Cells.Count;
        }

        enum ComboKind { None, Destroy, Conversion }

        public int Width { get; private set; }
        public int Height { get; private set; }
        public int Variety { get; private set; }

        int cellCount;

        // --- Kristal katmanı (Board.crystals) ---
        // Type -1 = kare boş. İndeks: y * Width + x, y = 0 en alt sıra.
        public sbyte[] Type = Array.Empty<sbyte>();
        public LabPower[] Power = Array.Empty<LabPower>();
        sbyte[] bombTarget = Array.Empty<sbyte>();

        // --- Engel katmanı (ObstacleGrid) ---
        public LabObstacle[] Obstacle = Array.Empty<LabObstacle>();
        byte[] hitsLeft = Array.Empty<byte>();

        int transparentHits = 1;
        int solidHits = 1;
        int blockHits = 3;
        double refillLuck = 1.0;

        public int Remaining { get; private set; }
        public int Total { get; private set; }

        public LabRng Rng;

        // --- İstatistik (bot ve rapor okuyor) ---
        public int MovesUsed;
        public int HitsDealt;        // engellere işleyen vuruş (çatlatma dahil)
        public int Cleared;          // patlayan kristal
        public int Refreshes;        // hamle kalmadığı için tahta baştan kuruldu
        public int CrossMade;
        public int BombMade;
        public int ColorBombMade;

        // Tahta yenilendikten sonra da hamle yok: oyuncu kilitlendi.
        // Level tasarımı hatası - rapor bunu ayrıca gösteriyor.
        public bool Stuck;

        public bool Won => Total > 0 && Remaining == 0;

        // Board.movedToX/Y ve movedFromX/Y - kare indeksi, -1 = yok
        int movedTo = -1;
        int movedFrom = -1;

        // Multicolor + buff kombosunun parametreleri (ComboResult)
        int convBomb;
        int convType;
        LabPower convPower;

        // --- Tamponlar: sıcak yolda bellek ayırmamak için ---
        bool[] claimedH = Array.Empty<bool>();
        bool[] claimedV = Array.Empty<bool>();
        bool[] destroy = Array.Empty<bool>();
        bool[] initial = Array.Empty<bool>();
        bool[] fired = Array.Empty<bool>();
        bool[] damaged = Array.Empty<bool>();

        // Board.blastArea: güçlerin kapladığı kareler - katı/bloğa doğrudan vurur
        bool[] blastArea = Array.Empty<bool>();
        int[] verticalOwner = Array.Empty<int>();

        readonly List<Group> groups = new List<Group>();
        readonly Stack<Group> groupPool = new Stack<Group>();
        readonly List<int> bombCells = new List<int>();
        readonly List<int> spawnCells = new List<int>();
        readonly List<LabPower> spawnPowers = new List<LabPower>();
        readonly List<int> destroyList = new List<int>();
        readonly List<int> layoutCells = new List<int>();
        readonly Queue<int> expandQueue = new Queue<int>();
        readonly int[] safeTypes = new int[16];

        // ==================== KURULUM ====================

        // Board.Start: engeller -> kristaller (CreateBoard -> BuildValidLayout)
        public void Init(LabLevel level, ulong seed)
        {
            Resize(level.Width, level.Height);

            Variety = level.Variety;
            transparentHits = Math.Max(1, level.TransparentHits);
            solidHits = Math.Max(1, level.SolidHits);
            blockHits = Math.Max(1, level.BlockHits);
            refillLuck = level.RefillLuck;

            Remaining = 0;

            for (int i = 0; i < cellCount; i++)
            {
                LabObstacle o = level.Obstacles[i];

                Obstacle[i] = o;
                hitsLeft[i] = (byte)HitsFor(o);

                if (o != LabObstacle.None) Remaining++;

                Type[i] = -1;
                Power[i] = LabPower.None;
                bombTarget[i] = 0;
            }

            Total = Remaining;
            Rng = new LabRng(seed);

            MovesUsed = HitsDealt = Cleared = Refreshes = 0;
            CrossMade = BombMade = ColorBombMade = 0;
            Stuck = false;
            movedTo = movedFrom = -1;

            // CreateBoard: katı olmayan her kare, x dıştan y içten
            layoutCells.Clear();

            for (int x = 0; x < Width; x++)
                for (int y = 0; y < Height; y++)
                    if (!IsBlocked(Idx(x, y))) layoutCells.Add(Idx(x, y));

            BuildValidLayout(layoutCells);
        }

        // Bot aday hamleleri bir kopyada deniyor
        public void CopyFrom(LabBoard o)
        {
            Resize(o.Width, o.Height);

            Array.Copy(o.Type, Type, cellCount);
            Array.Copy(o.Power, Power, cellCount);
            Array.Copy(o.bombTarget, bombTarget, cellCount);
            Array.Copy(o.Obstacle, Obstacle, cellCount);
            Array.Copy(o.hitsLeft, hitsLeft, cellCount);

            Variety = o.Variety;
            transparentHits = o.transparentHits;
            solidHits = o.solidHits;
            blockHits = o.blockHits;
            refillLuck = o.refillLuck;
            Remaining = o.Remaining;
            Total = o.Total;
            Rng = o.Rng;

            MovesUsed = o.MovesUsed;
            HitsDealt = o.HitsDealt;
            Cleared = o.Cleared;
            Refreshes = o.Refreshes;
            CrossMade = o.CrossMade;
            BombMade = o.BombMade;
            ColorBombMade = o.ColorBombMade;
            Stuck = o.Stuck;

            movedTo = o.movedTo;
            movedFrom = o.movedFrom;
        }

        void Resize(int width, int height)
        {
            if (width == Width && height == Height && Type.Length == width * height) return;

            Width = width;
            Height = height;
            cellCount = width * height;

            Type = new sbyte[cellCount];
            Power = new LabPower[cellCount];
            bombTarget = new sbyte[cellCount];
            Obstacle = new LabObstacle[cellCount];
            hitsLeft = new byte[cellCount];

            claimedH = new bool[cellCount];
            claimedV = new bool[cellCount];
            destroy = new bool[cellCount];
            initial = new bool[cellCount];
            fired = new bool[cellCount];
            damaged = new bool[cellCount];
            blastArea = new bool[cellCount];
            verticalOwner = new int[cellCount];
        }

        // ==================== SORGULAR ====================

        public int Idx(int x, int y) => y * Width + x;
        public int XOf(int i) => i % Width;
        public int YOf(int i) => i / Width;

        bool InBounds(int x, int y) => x >= 0 && x < Width && y >= 0 && y < Height;

        // ObstacleGrid.IsBlocking: katı ya da blok - kristal yok, düşüşü keser
        static bool Blocks(LabObstacle o) => o == LabObstacle.Solid || o == LabObstacle.Block;

        // Board.IsBlocked
        public bool IsBlocked(int i) => Blocks(Obstacle[i]);

        bool IsBlocked(int x, int y) => Blocks(Obstacle[Idx(x, y)]);

        // Takasa girebilir mi: katı değil ve kristali var (Board.TrySwap'ın ön koşulları)
        public bool IsPlayable(int i) => !Blocks(Obstacle[i]) && Type[i] >= 0;

        // Katı olmayan ama kristali olmayan kareler - katı blokların altındaki ölü cepler
        public int CountEmptyCells()
        {
            int n = 0;

            for (int i = 0; i < cellCount; i++)
                if (!Blocks(Obstacle[i]) && Type[i] < 0) n++;

            return n;
        }

        int HitsFor(LabObstacle o)
        {
            switch (o)
            {
                case LabObstacle.Solid:       return solidHits;
                case LabObstacle.Block:       return blockHits;
                case LabObstacle.Transparent: return transparentHits;
            }

            return 0;
        }

        // Board.GetMatchTypeAt: renk bombası eşleşmeye girmez
        int MatchType(int x, int y)
        {
            if (!InBounds(x, y)) return -1;

            int i = Idx(x, y);
            if (Type[i] < 0) return -1;
            if (Power[i] == LabPower.ColorBomb) return -1;

            return Type[i];
        }

        // ==================== DİZİLİM ====================

        // Board.GetNonMatchingType
        int NonMatchingType(int x, int y)
        {
            int count = 0;

            for (int t = 0; t < Variety; t++)
            {
                if (SameRun(x, y, -1, 0, t) + SameRun(x, y, 1, 0, t) >= 2) continue;
                if (SameRun(x, y, 0, -1, t) + SameRun(x, y, 0, 1, t) >= 2) continue;

                safeTypes[count++] = t;
            }

            if (count == 0) return Rng.Range(Variety);
            return safeTypes[Rng.Range(count)];
        }

        // Board.SameRun
        int SameRun(int x, int y, int dx, int dy, int type)
        {
            int count = 0;

            for (int i = 1; i <= 2; i++)
            {
                if (MatchType(x + dx * i, y + dy * i) != type) break;
                count++;
            }

            return count;
        }

        // Board.BuildValidLayout
        bool BuildValidLayout(List<int> cells)
        {
            for (int attempt = 0; attempt < LayoutRetryLimit; attempt++)
            {
                ClearCrystals();

                foreach (int c in cells)
                {
                    Type[c] = (sbyte)NonMatchingType(XOf(c), YOf(c));
                    Power[c] = LabPower.None;
                }

                if (HasValidMove()) return true;
            }

            return false;
        }

        void ClearCrystals()
        {
            for (int i = 0; i < cellCount; i++)
            {
                Type[i] = -1;
                Power[i] = LabPower.None;
            }
        }

        // ==================== HAMLE GEÇERLİLİĞİ ====================

        // Board.HasValidMove
        public bool HasValidMove()
        {
            for (int x = 0; x < Width; x++)
            {
                for (int y = 0; y < Height; y++)
                {
                    int i = Idx(x, y);

                    if (Type[i] >= 0 && Power[i] != LabPower.None) return true;

                    if (x + 1 < Width && WouldCreateMatch(i, Idx(x + 1, y))) return true;
                    if (y + 1 < Height && WouldCreateMatch(i, Idx(x, y + 1))) return true;
                }
            }

            return false;
        }

        // Board.WouldCreateMatch
        bool WouldCreateMatch(int a, int b)
        {
            if (Type[a] < 0 || Type[b] < 0) return false;

            SwapCells(a, b);
            bool result = HasMatchAtCell(a) || HasMatchAtCell(b);
            SwapCells(a, b);

            return result;
        }

        // Board.HasMatchAtCell
        bool HasMatchAtCell(int i)
        {
            int x = XOf(i), y = YOf(i);

            int type = MatchType(x, y);
            if (type < 0) return false;

            int count = 1;
            for (int k = x + 1; k < Width && MatchType(k, y) == type; k++) count++;
            for (int k = x - 1; k >= 0 && MatchType(k, y) == type; k--) count++;
            if (count >= 3) return true;

            count = 1;
            for (int k = y + 1; k < Height && MatchType(x, k) == type; k++) count++;
            for (int k = y - 1; k >= 0 && MatchType(x, k) == type; k--) count++;
            return count >= 3;
        }

        // Board'daki SwapSequence'e girebilecek bir takas mı. Buff'lı takas her
        // zaman geçerli (TryPowerCombo ne olursa olsun ateşliyor), değilse
        // eşleşme doğmalı.
        public bool IsLegalMove(int a, int b)
        {
            if (!IsPlayable(a) || !IsPlayable(b)) return false;
            if (Power[a] != LabPower.None || Power[b] != LabPower.None) return true;

            return WouldCreateMatch(a, b);
        }

        void SwapCells(int a, int b)
        {
            sbyte t = Type[a]; Type[a] = Type[b]; Type[b] = t;
            LabPower p = Power[a]; Power[a] = Power[b]; Power[b] = p;
            sbyte bt = bombTarget[a]; bombTarget[a] = bombTarget[b]; bombTarget[b] = bt;
        }

        // ==================== HAMLE ====================

        // Board.TrySwap + SwapSequence + EndOfMoveChecks.
        // from: oyuncunun sürüklediği kristalin karesi, to: bıraktığı kare.
        // Geçersiz takasta tahta değişmez, false döner.
        public bool TryMove(int from, int to)
        {
            if (!IsLegalMove(from, to)) return false;

            SwapCells(from, to);

            // Board: atOrigin = crystals[x,y], atTarget = crystals[targetX,targetY]
            ComboKind combo = ResolveCombo(from, to);

            if (combo != ComboKind.None)
            {
                movedTo = movedFrom = -1;   // ClearSwapOrigin
                MovesUsed++;

                if (combo == ComboKind.Conversion) RunConversion();

                DestroyAndRefill(1, false);
                ProcessCascade();
                EndOfMove();

                return true;
            }

            // IsLegalMove eşleşmeyi zaten doğruladı
            movedTo = to;
            movedFrom = from;
            MovesUsed++;

            ProcessCascade();
            EndOfMove();

            return true;
        }

        // Board.EndOfMoveChecks: kazandıysa dur, hamle kalmadıysa tahtayı baştan kur
        void EndOfMove()
        {
            if (Won) return;
            if (HasValidMove()) return;

            Refreshes++;

            // VanishDiagonalWave: kristali OLAN kareler, x dıştan y içten.
            // Ölü cepler boş kalıyor.
            layoutCells.Clear();

            for (int x = 0; x < Width; x++)
                for (int y = 0; y < Height; y++)
                    if (Type[Idx(x, y)] >= 0) layoutCells.Add(Idx(x, y));

            BuildValidLayout(layoutCells);

            if (!HasValidMove()) Stuck = true;
        }

        // ==================== KOMBOLAR ====================

        // Board.TryPowerCombo. a = takastan sonra başlangıç karesindeki taş,
        // b = oyuncunun sürükleyip hedefe bıraktığı taş. Patlama kümesi destroy'a yazılır.
        ComboKind ResolveCombo(int a, int b)
        {
            bool aBomb = Power[a] == LabPower.ColorBomb;
            bool bBomb = Power[b] == LabPower.ColorBomb;

            bool aBuff = Power[a] != LabPower.None && !aBomb;
            bool bBuff = Power[b] != LabPower.None && !bBomb;

            ClearMask(destroy);
            ClearMask(blastArea);

            // İki renk bombası: tahtadaki her şey (zincirleme yok)
            if (aBomb && bBomb)
            {
                MarkAllCrystals();
                MarkAllArea();
                return ComboKind.Destroy;
            }

            // Multicolor + buff: rastgele bir renk o buff'a dönüşür
            if ((aBomb && bBuff) || (bBomb && aBuff))
            {
                convBomb = aBomb ? a : b;
                convType = Rng.Range(Variety);
                convPower = Power[aBomb ? b : a];
                return ComboKind.Conversion;
            }

            // Renk bombası + normal: o renkteki her şey
            if (aBomb || bBomb)
            {
                int bomb = aBomb ? a : b;
                int target = aBomb ? b : a;

                bombTarget[bomb] = Type[target];

                destroy[bomb] = true;
                destroy[target] = true;

                for (int i = 0; i < cellCount; i++)
                    if (Type[i] >= 0 && Type[i] == Type[target]) destroy[i] = true;

                Expand();
                return ComboKind.Destroy;
            }

            // Bomba + 4-way: tüm tahta (zincirleme yok)
            if ((Power[a] == LabPower.Bomb && Power[b] == LabPower.Cross) ||
                (Power[a] == LabPower.Cross && Power[b] == LabPower.Bomb))
            {
                MarkAllCrystals();
                MarkAllArea();
                return ComboKind.Destroy;
            }

            int bx = XOf(b), by = YOf(b);

            // Bomba + bomba: hedef karenin çevresinde 5x5
            if (Power[a] == LabPower.Bomb && Power[b] == LabPower.Bomb)
            {
                for (int dx = -2; dx <= 2; dx++)
                    for (int dy = -2; dy <= 2; dy++)
                    {
                        int nx = bx + dx, ny = by + dy;
                        if (!InBounds(nx, ny)) continue;

                        int n = Idx(nx, ny);
                        blastArea[n] = true;
                        if (Type[n] >= 0) destroy[n] = true;
                    }

                Expand();
                return ComboKind.Destroy;
            }

            // 4-way + 4-way: hedef karenin 3 satırı + 3 sütunu
            if (Power[a] == LabPower.Cross && Power[b] == LabPower.Cross)
            {
                for (int i = 0; i < cellCount; i++)
                {
                    bool inRows = Math.Abs(YOf(i) - by) <= 1;
                    bool inCols = Math.Abs(XOf(i) - bx) <= 1;

                    if (!inRows && !inCols) continue;

                    blastArea[i] = true;
                    if (Type[i] >= 0) destroy[i] = true;
                }

                Expand();
                return ComboKind.Destroy;
            }

            // Tek buff: neyle takas edilirse edilsin getirildiği karede patlar
            if (aBuff || bBuff)
            {
                if (aBuff) destroy[a] = true;
                if (bBuff) destroy[b] = true;

                Expand();
                return ComboKind.Destroy;
            }

            return ComboKind.None;
        }

        // Board.PlayPowerConversion
        void RunConversion()
        {
            ClearMask(destroy);

            for (int i = 0; i < cellCount; i++)
            {
                if (i == convBomb || Type[i] < 0) continue;
                if (Type[i] != convType) continue;
                if (Power[i] == LabPower.ColorBomb) continue;

                destroy[i] = true;
            }

            // Süper buff tahtadan düşüyor - ne engel kırar ne patlama listesine girer
            Type[convBomb] = -1;
            Power[convBomb] = LabPower.None;

            for (int i = 0; i < cellCount; i++)
                if (destroy[i]) Power[i] = convPower;

            Expand();
        }

        void MarkAllArea()
        {
            for (int i = 0; i < cellCount; i++) blastArea[i] = true;
        }

        void MarkAllCrystals()
        {
            for (int i = 0; i < cellCount; i++)
                if (Type[i] >= 0) destroy[i] = true;
        }

        // Board.ExpandWithPowers: destroy kümesini güç zincirleriyle yerinde büyütür
        void Expand()
        {
            expandQueue.Clear();

            for (int i = 0; i < cellCount; i++)
            {
                initial[i] = destroy[i];
                fired[i] = false;

                if (destroy[i]) expandQueue.Enqueue(i);
            }

            while (expandQueue.Count > 0)
            {
                int c = expandQueue.Dequeue();

                if (Type[c] < 0 || Power[c] == LabPower.None) continue;
                if (fired[c]) continue;
                fired[c] = true;

                // Zincirleme tetiklenen renk bombası rastgele bir çeşit seçer
                if (Power[c] == LabPower.ColorBomb && !initial[c])
                    bombTarget[c] = (sbyte)Rng.Range(Variety);

                AddPowerAffected(c);
            }
        }

        // Board.GetPowerAffected
        void AddPowerAffected(int c)
        {
            int px = XOf(c), py = YOf(c);

            switch (Power[c])
            {
                case LabPower.Cross:
                    for (int x = 0; x < Width; x++) AffectArea(Idx(x, py));
                    for (int y = 0; y < Height; y++) AffectArea(Idx(px, y));
                    break;

                case LabPower.Bomb:
                    for (int dx = -1; dx <= 1; dx++)
                        for (int dy = -1; dy <= 1; dy++)
                            if (InBounds(px + dx, py + dy)) AffectArea(Idx(px + dx, py + dy));
                    break;

                case LabPower.ColorBomb:
                    int target = bombTarget[c];

                    for (int i = 0; i < cellCount; i++)
                        if (i != c && Type[i] >= 0 && Type[i] == target) Affect(i);
                    break;
            }
        }

        // Alan etkisi: kare alana girer, kristali varsa patlar
        void AffectArea(int i)
        {
            blastArea[i] = true;
            Affect(i);
        }

        void Affect(int i)
        {
            if (Type[i] < 0 || destroy[i]) return;

            destroy[i] = true;
            expandQueue.Enqueue(i);
        }

        // ==================== CASCADE ====================

        // Board.ProcessCascade
        void ProcessCascade()
        {
            int cascadeLevel = 0;

            while (true)
            {
                FindAllGroups();
                if (groups.Count == 0) break;

                cascadeLevel++;

                spawnCells.Clear();
                spawnPowers.Clear();
                ClearMask(destroy);
                ClearMask(blastArea);

                // L / T kesişimleri: kesişim başına tek bomba
                FindBombIntersections();

                foreach (int cell in bombCells)
                {
                    spawnCells.Add(cell);
                    spawnPowers.Add(LabPower.Bomb);
                }

                foreach (Group g in groups)
                {
                    LabPower power = g.Count >= 5 ? LabPower.ColorBomb
                                   : g.Count == 4 ? LabPower.Cross
                                   : LabPower.None;

                    if (GroupHitsBombCell(g)) power = LabPower.None;

                    if (power != LabPower.None)
                    {
                        // Oyuncunun sürüklediği kare, sonra takasın diğer karesi,
                        // cascade'lerde grubun ortası
                        int at = -1;

                        if (cascadeLevel == 1)
                        {
                            if (movedTo >= 0 && g.Cells.Contains(movedTo)) at = movedTo;
                            else if (movedFrom >= 0 && g.Cells.Contains(movedFrom)) at = movedFrom;
                        }

                        if (at < 0) at = GroupCenter(g);

                        spawnCells.Add(at);
                        spawnPowers.Add(power);
                    }

                    foreach (int c in g.Cells) destroy[c] = true;
                }

                ReleaseGroups();

                Expand();

                // Güç doğacak karedeki taş patlamıyor, güce dönüşüyor
                foreach (int c in spawnCells)
                    if (Type[c] >= 0) destroy[c] = false;

                DestroyAndRefill(cascadeLevel, true);
            }

            movedTo = movedFrom = -1;
        }

        // Board.FindAllGroups + TryAddGroup
        void FindAllGroups()
        {
            ReleaseGroups();
            ClearMask(claimedH);
            ClearMask(claimedV);

            for (int x = 0; x < Width; x++)
            {
                for (int y = 0; y < Height; y++)
                {
                    int type = MatchType(x, y);
                    if (type < 0) continue;

                    TryAddGroup(x, y, type, true);
                    TryAddGroup(x, y, type, false);
                }
            }
        }

        void TryAddGroup(int x, int y, int type, bool horizontal)
        {
            Group g = groupPool.Count > 0 ? groupPool.Pop() : new Group();
            g.Cells.Clear();
            g.Horizontal = horizontal;

            g.Cells.Add(Idx(x, y));

            if (horizontal)
            {
                for (int i = x + 1; i < Width && MatchType(i, y) == type; i++) g.Cells.Add(Idx(i, y));
                for (int i = x - 1; i >= 0 && MatchType(i, y) == type; i--) g.Cells.Add(Idx(i, y));
            }
            else
            {
                for (int i = y + 1; i < Height && MatchType(x, i) == type; i++) g.Cells.Add(Idx(x, i));
                for (int i = y - 1; i >= 0 && MatchType(x, i) == type; i--) g.Cells.Add(Idx(x, i));
            }

            bool[] claimed = horizontal ? claimedH : claimedV;

            bool anyClaimed = false;
            foreach (int c in g.Cells)
                if (claimed[c]) { anyClaimed = true; break; }

            if (g.Count < 3 || anyClaimed)
            {
                groupPool.Push(g);
                return;
            }

            foreach (int c in g.Cells) claimed[c] = true;

            groups.Add(g);
        }

        void ReleaseGroups()
        {
            foreach (Group g in groups) groupPool.Push(g);
            groups.Clear();
        }

        // Board.FindBombIntersections: hem yatay hem dikey diziye ait kareler.
        // 5'li düz dizinin kestiği kesişim atlanır (orada multicolor doğuyor).
        void FindBombIntersections()
        {
            bombCells.Clear();

            for (int i = 0; i < cellCount; i++) verticalOwner[i] = -1;

            for (int gi = 0; gi < groups.Count; gi++)
            {
                if (groups[gi].Horizontal) continue;
                foreach (int c in groups[gi].Cells) verticalOwner[c] = gi;
            }

            foreach (Group h in groups)
            {
                if (!h.Horizontal) continue;

                foreach (int c in h.Cells)
                {
                    int vi = verticalOwner[c];
                    if (vi < 0) continue;

                    if (h.Count >= 5 || groups[vi].Count >= 5) continue;

                    bombCells.Add(c);
                }
            }
        }

        bool GroupHitsBombCell(Group g)
        {
            if (bombCells.Count == 0) return false;

            foreach (int c in g.Cells)
                if (bombCells.Contains(c)) return true;

            return false;
        }

        // Board.GroupCenterCrystal: koordinat ortalamasına en yakın, eşitlikte ilki
        int GroupCenter(Group g)
        {
            float sum = 0f;

            foreach (int c in g.Cells)
                sum += g.Horizontal ? XOf(c) : YOf(c);

            float center = sum / g.Count;

            int best = g.Cells[0];
            float bestDist = float.MaxValue;

            foreach (int c in g.Cells)
            {
                float d = Math.Abs((g.Horizontal ? XOf(c) : YOf(c)) - center);

                if (d < bestDist)
                {
                    bestDist = d;
                    best = c;
                }
            }

            return best;
        }

        // ==================== PATLAMA + DOLUM ====================

        // Board.DestroyAndRefill. Patlayacaklar destroy maskesinde.
        void DestroyAndRefill(int cascadeLevel, bool withPowers)
        {
            destroyList.Clear();

            for (int i = 0; i < cellCount; i++)
                if (destroy[i] && Type[i] >= 0) destroyList.Add(i);

            // DestroyCrystals
            foreach (int c in destroyList)
            {
                Type[c] = -1;
                Power[c] = LabPower.None;
            }

            Cleared += destroyList.Count;

            // Yerçekiminden ÖNCE: kırılan katı şeffafa dönüp kristal bekliyor
            DamageObstacles(withPowers);

            if (withPowers)
            {
                for (int k = 0; k < spawnCells.Count; k++)
                {
                    int c = spawnCells[k];
                    if (Type[c] < 0) continue;

                    SetPower(c, spawnPowers[k]);
                }
            }

            Settle();
        }

        void SetPower(int c, LabPower power)
        {
            Power[c] = power;

            switch (power)
            {
                case LabPower.Cross:
                    CrossMade++;
                    break;

                case LabPower.Bomb:
                    BombMade++;
                    break;

                case LabPower.ColorBomb:
                    ColorBombMade++;
                    // Havuzdan gelen taşın eski hedefi - bilinemiyor
                    bombTarget[c] = (sbyte)Rng.Range(Variety);
                    break;
            }
        }

        // ==================== ENGEL KIRMA ====================

        // Board.DamageObstaclesFrom: patlayan her kare + güç doğan kareler + güç alanı
        void DamageObstacles(bool withPowers)
        {
            if (Remaining <= 0)
            {
                ClearMask(blastArea);
                return;
            }

            ClearMask(damaged);

            foreach (int c in destroyList) DamageAround(c);

            if (withPowers)
                foreach (int c in spawnCells) DamageAround(c);

            // Güç alanının üstünden geçtiği engeller, kendi karesinden
            for (int i = 0; i < cellCount; i++)
                if (blastArea[i]) TryDamage(XOf(i), YOf(i), true);

            ClearMask(blastArea);
        }

        // Board.DamageAroundCell: kendi karesi (şeffafı kırar) + 4 dik komşu (katıyı kırar)
        void DamageAround(int c)
        {
            int x = XOf(c), y = YOf(c);

            TryDamage(x, y, true);
            TryDamage(x + 1, y, false);
            TryDamage(x - 1, y, false);
            TryDamage(x, y + 1, false);
            TryDamage(x, y - 1, false);
        }

        // Board.TryDamageObstacle
        void TryDamage(int x, int y, bool ownCell)
        {
            if (!InBounds(x, y)) return;

            int i = Idx(x, y);
            LabObstacle type = Obstacle[i];

            if (type == LabObstacle.None) return;

            // Şeffaf SADECE kendi karesindeki kristal patlayınca kırılır
            if (!ownCell && type == LabObstacle.Transparent) return;

            // Aynı patlamada aynı engele bir kez
            if (damaged[i]) return;
            damaged[i] = true;

            ApplyDamage(i);
        }

        // ObstacleGrid.Damage: Solid -> Transparent -> None, Block -> None
        void ApplyDamage(int i)
        {
            HitsDealt++;

            hitsLeft[i]--;
            if (hitsLeft[i] > 0) return;

            LabObstacle next = Obstacle[i] == LabObstacle.Solid
                ? LabObstacle.Transparent
                : LabObstacle.None;

            Obstacle[i] = next;
            hitsLeft[i] = (byte)HitsFor(next);

            if (next == LabObstacle.None) Remaining--;
        }

        // ==================== TAHTAYI OTURTMA ====================

        // Board.SettleBoard: düş - çapraz kay - doldur, hiçbir şey kıpırdamayana kadar
        void Settle()
        {
            for (int guard = 0; guard < SettleStepLimit; guard++)
            {
                bool fell = ApplyGravity();
                bool slid = ApplyDiagonalSlide();
                bool filled = FillEmptySpaces();

                if (!fell && !slid && !filled) break;
            }
        }

        // Board.ApplyGravity
        bool ApplyGravity()
        {
            bool moved = false;

            for (int x = 0; x < Width; x++)
            {
                for (int y = 0; y < Height; y++)
                {
                    int i = Idx(x, y);

                    if (IsBlocked(i)) continue;
                    if (Type[i] >= 0) continue;

                    for (int above = y + 1; above < Height; above++)
                    {
                        int a = Idx(x, above);

                        if (IsBlocked(a)) break;
                        if (Type[a] < 0) continue;

                        MoveCrystal(a, i);
                        moved = true;
                        break;
                    }
                }
            }

            return moved;
        }

        // Board.FillEmptySpaces: sadece sütunun tahtaya açık kısmı.
        // Board.RefillType: şans payı kadarı rastgele, kalanı hazır eşleşme yapmayan renk.
        bool FillEmptySpaces()
        {
            bool moved = false;

            for (int x = 0; x < Width; x++)
            {
                int floor = TopOpenFloor(x);

                for (int y = floor; y < Height; y++)
                {
                    int i = Idx(x, y);
                    if (Type[i] >= 0) continue;

                    Type[i] = (sbyte)(Rng.NextDouble() < refillLuck
                        ? Rng.Range(Variety)
                        : NonMatchingType(x, y));

                    Power[i] = LabPower.None;
                    moved = true;
                }
            }

            return moved;
        }

        // Board.TopOpenFloor: en yüksek katı engelin bir üstü
        int TopOpenFloor(int x)
        {
            for (int y = Height - 1; y >= 0; y--)
                if (IsBlocked(x, y)) return y + 1;

            return 0;
        }

        // Board.ApplyDiagonalSlide
        bool ApplyDiagonalSlide()
        {
            if (Remaining <= 0) return false;

            bool moved = false;

            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    int i = Idx(x, y);

                    if (IsBlocked(i) || Type[i] >= 0) continue;
                    if (!IsSealedFromAbove(x, y)) continue;

                    int source = TakeDiagonalSource(x, y);
                    if (source < 0) continue;

                    MoveCrystal(source, i);
                    moved = true;
                }
            }

            return moved;
        }

        bool IsSealedFromAbove(int x, int y)
        {
            for (int above = y + 1; above < Height; above++)
                if (IsBlocked(x, above)) return true;

            return false;
        }

        // Board.TakeDiagonalSource: sol-üst / sağ-üst dönüşümlü
        int TakeDiagonalSource(int x, int y)
        {
            bool leftFirst = ((x + y) & 1) == 0;

            int first = DiagonalCandidate(leftFirst ? x - 1 : x + 1, y);
            if (first >= 0) return first;

            return DiagonalCandidate(leftFirst ? x + 1 : x - 1, y);
        }

        // Board.DiagonalCandidate
        int DiagonalCandidate(int sx, int y)
        {
            int sy = y + 1;

            if (sx < 0 || sx >= Width || sy >= Height) return -1;
            if (IsBlocked(sx, sy)) return -1;

            int s = Idx(sx, sy);
            if (Type[s] < 0) return -1;

            // Kendi sütununda düz inebiliyorsa çapraza sapmasın
            if (!IsBlocked(sx, y) && Type[Idx(sx, y)] < 0) return -1;

            return s;
        }

        void MoveCrystal(int from, int to)
        {
            Type[to] = Type[from];
            Power[to] = Power[from];
            bombTarget[to] = bombTarget[from];

            Type[from] = -1;
            Power[from] = LabPower.None;
        }

        void ClearMask(bool[] mask)
        {
            Array.Clear(mask, 0, cellCount);
        }

        // ==================== TEŞHİS ====================

        // Tahta durgunken hazır eşleşme kalmamalı - testler bunu kontrol ediyor
        public bool HasAnyMatch()
        {
            FindAllGroups();
            bool any = groups.Count > 0;
            ReleaseGroups();
            return any;
        }

        // Konsola basmak için: üst sıra en üstte. Rakam = kristal rengi,
        // + 4-way, o bomba, * multicolor, # katı, _ boş kare. Şeffafın altındaki
        // kristal büyük harfle (A = 0, B = 1 ...).
        public string Dump()
        {
            var sb = new System.Text.StringBuilder();

            for (int y = Height - 1; y >= 0; y--)
            {
                for (int x = 0; x < Width; x++)
                {
                    int i = Idx(x, y);
                    char c;

                    if (IsBlocked(i)) c = '#';
                    else if (Type[i] < 0) c = '_';
                    else if (Power[i] == LabPower.Cross) c = '+';
                    else if (Power[i] == LabPower.Bomb) c = 'o';
                    else if (Power[i] == LabPower.ColorBomb) c = '*';
                    else if (Obstacle[i] == LabObstacle.Transparent) c = (char)('A' + Type[i]);
                    else c = (char)('0' + Type[i]);

                    sb.Append(c);
                }

                sb.Append('\n');
            }

            return sb.ToString();
        }
    }
}
