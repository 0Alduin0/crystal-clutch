using System;
using System.Collections.Generic;

namespace LevelLab
{
    // Aday bir engel yerleşimi ve nasıl kurulduğu
    public sealed class LayoutCandidate
    {
        public LabObstacle[] Cells;
        public string Recipe = "";
    }

    // Engel yerleşimi üretir: desen kütüphanesinden 1-3 desen seçip malzeme
    // reçetesiyle boyar, sonra simetriye çeker.
    //
    // "Güzel" level için üç kural:
    //   1. SİMETRİ. Oyuncu tahtaya bakınca bir şekil görmeli, gürültü değil.
    //      Çoğunlukla soldan sağa ayna; bazen dört yönlü ya da 180° dönme.
    //   2. ŞEKİL. Engeller tek tek serpilmiyor, desen olarak konuyor (çerçeve,
    //      elmas, raf...). Malzeme de desene göre: dışı katı içi şeffaf gibi.
    //   3. OYNANABİLİRLİK. Tahtaya kristal girişini kesen, çok ölü kare
    //      bırakan ya da mekanik takvimine uymayan yerleşim elenir. Zorluk ve
    //      "3 hamlede biter mi" sorusunu burası değil, bot cevaplıyor.
    public sealed class LayoutGenerator
    {
        const int MaxAttempts = 400;

        // Tahtanın en fazla bu kadarı kapalı olsun - kalan alan oynanmaya yetsin
        const int MaxBlockingCells = 22;

        // Başlangıçta yukarıdan beslenemeyen kare sınırı (ölü cep)
        const int MaxUnfeedable = 3;

        enum Symmetry { MirrorX, MirrorXY, Rotate180 }

        enum Mat { T, S, B }

        // Desenin bir karesi: kenarda mı içeride mi, şeklin en üst sırasında mı
        struct Cell
        {
            public int X, Y;
            public bool Edge;
            public bool Top;
        }

        readonly int width;
        readonly int height;
        LabRng rng;

        LabObstacle[] grid;
        readonly List<Cell> shape = new List<Cell>(64);
        readonly List<string> parts = new List<string>(4);

        public LayoutGenerator(int width, int height, ulong seed)
        {
            this.width = width;
            this.height = height;
            rng = new LabRng(seed);
            grid = new LabObstacle[width * height];
        }

        // Kurallara uyan bir aday; MaxAttempts denemede bulunamazsa null
        public LayoutCandidate Generate(LevelSpec spec)
        {
            for (int attempt = 0; attempt < MaxAttempts; attempt++)
            {
                Array.Clear(grid, 0, grid.Length);
                parts.Clear();

                Symmetry sym = PickSymmetry();
                int motifs = 1 + rng.Range(3);

                for (int m = 0; m < motifs; m++)
                    AddMotif(spec);

                Symmetrize(sym);
                ApplyMechanics(spec);

                if (!IsAcceptable(spec)) continue;

                return new LayoutCandidate
                {
                    Cells = (LabObstacle[])grid.Clone(),
                    Recipe = SymmetryName(sym) + ": " + string.Join(" + ", parts)
                };
            }

            return null;
        }

        // ==================== SİMETRİ ====================

        // 180° dönme küçük parçalarla gürültü gibi duruyor - seyrek
        Symmetry PickSymmetry()
        {
            int r = rng.Range(100);
            if (r < 70) return Symmetry.MirrorX;
            if (r < 93) return Symmetry.MirrorXY;
            return Symmetry.Rotate180;
        }

        static string SymmetryName(Symmetry s)
        {
            switch (s)
            {
                case Symmetry.MirrorXY: return "4 yönlü";
                case Symmetry.Rotate180: return "180°";
            }

            return "ayna";
        }

        // Birleşim: karenin ve eşlerinin en güçlü malzemesi hepsine yazılır.
        // (None < Şeffaf < Katı < Blok - enum sırası tam da bu.)
        void Symmetrize(Symmetry sym)
        {
            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++)
                {
                    int mx = width - 1 - x;
                    int my = height - 1 - y;

                    LabObstacle v = grid[Idx(x, y)];

                    switch (sym)
                    {
                        case Symmetry.MirrorX:
                            v = Max(v, grid[Idx(mx, y)]);
                            Set(x, y, v); Set(mx, y, v);
                            break;

                        case Symmetry.MirrorXY:
                            v = Max(Max(v, grid[Idx(mx, y)]), Max(grid[Idx(x, my)], grid[Idx(mx, my)]));
                            Set(x, y, v); Set(mx, y, v); Set(x, my, v); Set(mx, my, v);
                            break;

                        case Symmetry.Rotate180:
                            v = Max(v, grid[Idx(mx, my)]);
                            Set(x, y, v); Set(mx, my, v);
                            break;
                    }
                }
        }

        static LabObstacle Max(LabObstacle a, LabObstacle b) => a >= b ? a : b;

        // ==================== DESENLER ====================

        void AddMotif(LevelSpec spec)
        {
            shape.Clear();

            string name;

            // Ağırlıklı seçim: dağınık ikililer (11) diğerlerinin yarısı kadar
            // çıksın - fazlası tahtayı serpilmiş gösteriyor
            int pick = rng.Range(23);
            if (pick >= 12) pick -= 12;

            switch (pick)
            {
                case 0: name = Rect(filled: true); break;
                case 1: name = Rect(filled: false); break;
                case 2: name = HBar(); break;
                case 3: name = Pillar(); break;
                case 4: name = Diamond(); break;
                case 5: name = Plus(); break;
                case 6: name = Floor(); break;
                case 7: name = Corners(); break;
                case 8: name = Checker(); break;
                case 9: name = Shelves(); break;
                case 10: name = Pairs(); break;
                default: name = Ring(); break;
            }

            if (shape.Count == 0) return;

            MarkTops();

            Mat edge, core;
            string recipe = PickRecipe(spec, out edge, out core);

            foreach (Cell c in shape)
            {
                Mat m = c.Edge ? edge : core;

                // "Tepe" reçeteleri: şeklin en üst sırası kenar malzemesi, gerisi çekirdek
                if (recipe.StartsWith("tepe")) m = c.Top ? edge : core;

                // Dama reçetesi
                if (recipe == "dama") m = ((c.X + c.Y) & 1) == 0 ? edge : core;

                Put(c.X, c.Y, ToObstacle(m));
            }

            parts.Add(name + "(" + recipe + ")");
        }

        // Dolu ya da içi boş dikdörtgen
        string Rect(bool filled)
        {
            int w = 2 + rng.Range(filled ? 3 : 5);
            int h = 2 + rng.Range(filled ? 3 : 5);
            if (!filled) { w = Math.Max(3, w); h = Math.Max(3, h); }

            int x0 = rng.Range(Math.Max(1, width - w + 1));
            int y0 = rng.Range(Math.Max(1, height - 1 - h + 1));   // en üst sıraya taşmasın

            for (int x = x0; x < x0 + w; x++)
                for (int y = y0; y < y0 + h; y++)
                {
                    bool edge = x == x0 || x == x0 + w - 1 || y == y0 || y == y0 + h - 1;
                    if (!filled && !edge) continue;
                    AddCell(x, y, edge);
                }

            return filled ? "blok" : "çerçeve";
        }

        // Çerçevenin içinde ikinci bir çerçeve - ya da tam ortada halka
        string Ring()
        {
            int r = 2 + rng.Range(2);                  // yarıçap 2-3
            int cx = width / 2 - 1, cy = 2 + rng.Range(Math.Max(1, height - 6));

            for (int x = cx - r + 1; x <= cx + r; x++)
                for (int y = cy - r + 1; y <= cy + r; y++)
                {
                    bool edge = x == cx - r + 1 || x == cx + r || y == cy - r + 1 || y == cy + r;
                    if (!edge) continue;
                    AddCell(x, y, true);
                }

            return "halka";
        }

        string HBar()
        {
            int len = 3 + rng.Range(width - 2);
            int thick = 1 + rng.Range(2);
            int x0 = rng.Range(Math.Max(1, width - len + 1));
            int y0 = rng.Range(Math.Max(1, height - 1 - thick + 1));

            for (int x = x0; x < x0 + len; x++)
                for (int t = 0; t < thick; t++)
                    AddCell(x, y0 + t, t == thick - 1);

            return "çubuk";
        }

        string Pillar()
        {
            int len = 2 + rng.Range(height - 3);
            int x0 = rng.Range(width / 2);
            int y0 = rng.Range(Math.Max(1, height - 1 - len + 1));

            for (int y = y0; y < y0 + len; y++)
                AddCell(x0, y, y == y0 + len - 1);

            return "sütun";
        }

        string Diamond()
        {
            int r = 1 + rng.Range(3);
            bool hollow = r >= 2 && rng.Range(2) == 0;
            int cx = width / 2 - 1 + rng.Range(2);
            int cy = r + rng.Range(Math.Max(1, height - 2 * r - 1));

            for (int x = cx - r; x <= cx + r; x++)
                for (int y = cy - r; y <= cy + r; y++)
                {
                    int d = Math.Abs(x - cx) + Math.Abs(y - cy);
                    if (d > r) continue;
                    if (hollow && d < r) continue;
                    AddCell(x, y, d == r);
                }

            return hollow ? "içi boş elmas" : "elmas";
        }

        string Plus()
        {
            int arm = 1 + rng.Range(3);
            int cx = width / 2 - 1 + rng.Range(2);
            int cy = arm + rng.Range(Math.Max(1, height - 2 * arm - 1));

            for (int i = -arm; i <= arm; i++)
            {
                AddCell(cx + i, cy, Math.Abs(i) == arm);
                if (i != 0) AddCell(cx, cy + i, Math.Abs(i) == arm);
            }

            return "artı";
        }

        // Alttan 1-2 sıra. Bazen aralıklı: taban duvarı bir sütunu kapatmasın.
        string Floor()
        {
            int rows = 1 + rng.Range(2);
            bool gapped = rng.Range(2) == 0;

            for (int y = 0; y < rows; y++)
                for (int x = 0; x < width; x++)
                {
                    if (gapped && x % 3 == 1) continue;
                    AddCell(x, y, y == rows - 1);
                }

            return gapped ? "aralıklı taban" : "taban";
        }

        string Corners()
        {
            int size = 2 + rng.Range(2);
            bool top = rng.Range(2) == 0;

            // Sol alt (ya da sol üst) köşede L; simetri öbür köşelere taşır
            int yBase = top ? height - 2 : 0;
            int dir = top ? -1 : 1;

            for (int i = 0; i < size; i++)
            {
                AddCell(i, yBase, i == size - 1);
                if (i > 0) AddCell(0, yBase + dir * i, i == size - 1);
            }

            return "köşe";
        }

        string Checker()
        {
            int w = 3 + rng.Range(3);
            int h = 2 + rng.Range(3);
            int x0 = rng.Range(Math.Max(1, width - w + 1));
            int y0 = rng.Range(Math.Max(1, height - 1 - h + 1));

            for (int x = x0; x < x0 + w; x++)
                for (int y = y0; y < y0 + h; y++)
                    AddCell(x, y, ((x + y) & 1) == 0);

            return "dama";
        }

        // Raf: kısa bir yatay çubuk ve altında şeffaflar. Rafın altı yukarıdan
        // beslenemiyor, çapraz akışla doluyor - klasik "altına ulaş" bulmacası.
        string Shelves()
        {
            int len = 2 + rng.Range(2);
            int x0 = rng.Range(width / 2 - 1);
            int y = 2 + rng.Range(Math.Max(1, height - 4));

            for (int x = x0; x < x0 + len; x++)
            {
                AddCell(x, y, true);        // raf: kenar malzemesi
                AddCell(x, y - 1, false);   // altı: çekirdek
            }

            return "raf";
        }

        // Tahtaya yayılmış ikili parçalar - engelleri tek bölgeye toplamamak için
        string Pairs()
        {
            int count = 2 + rng.Range(2);

            for (int i = 0; i < count; i++)
            {
                int x = rng.Range(width / 2);
                int y = rng.Range(height - 1);
                bool vertical = rng.Range(2) == 0;

                AddCell(x, y, true);
                AddCell(vertical ? x : x + 1, vertical ? y + 1 : y, false);
            }

            return "ikililer";
        }

        void AddCell(int x, int y, bool edge)
        {
            if (x < 0 || x >= width || y < 0 || y >= height) return;
            shape.Add(new Cell { X = x, Y = y, Edge = edge });
        }

        // Her sütunda şeklin en üst karesi "tepe"
        void MarkTops()
        {
            for (int i = 0; i < shape.Count; i++)
            {
                Cell c = shape[i];
                bool top = true;

                foreach (Cell o in shape)
                    if (o.X == c.X && o.Y > c.Y) { top = false; break; }

                c.Top = top;
                shape[i] = c;
            }
        }

        // ==================== MALZEME ====================

        string PickRecipe(LevelSpec spec, out Mat edge, out Mat core)
        {
            // Katı yoksa her şey şeffaf
            if (!spec.UseSolid)
            {
                edge = core = Mat.T;
                return "şeffaf";
            }

            // Blok açıksa "sert" malzeme bazen blok
            Mat hard = spec.UseBlock && rng.Range(100) < (spec.RequireBlock ? 75 : 45) ? Mat.B : Mat.S;

            switch (rng.Range(7))
            {
                case 0:
                    edge = core = Mat.T;
                    return "şeffaf";

                case 1:
                    edge = core = hard;
                    return hard == Mat.B ? "blok" : "katı";

                case 2:
                case 3:
                    edge = hard; core = Mat.T;
                    return hard == Mat.B ? "dışı blok" : "dışı katı";

                case 4:
                    edge = hard; core = Mat.T;
                    return hard == Mat.B ? "tepe blok" : "tepe katı";

                case 5:
                    edge = hard; core = Mat.T;
                    return "dama";

                default:
                    // Blok kabuk, katı çekirdek - kalın zırh
                    edge = hard; core = hard == Mat.B ? Mat.S : Mat.T;
                    return hard == Mat.B ? "blok+katı" : "dışı katı";
            }
        }

        static LabObstacle ToObstacle(Mat m)
        {
            switch (m)
            {
                case Mat.S: return LabObstacle.Solid;
                case Mat.B: return LabObstacle.Block;
            }

            return LabObstacle.Transparent;
        }

        // Birleştirme: aynı kareye iki desen düşerse güçlü olan kalır
        void Put(int x, int y, LabObstacle o)
        {
            int i = Idx(x, y);
            grid[i] = Max(grid[i], o);
        }

        void ApplyMechanics(LevelSpec spec)
        {
            for (int i = 0; i < grid.Length; i++)
            {
                if (!spec.UseSolid && grid[i] != LabObstacle.None) grid[i] = LabObstacle.Transparent;
                else if (!spec.UseBlock && grid[i] == LabObstacle.Block) grid[i] = LabObstacle.Solid;
            }
        }

        // ==================== KABUL ====================

        bool IsAcceptable(LevelSpec spec)
        {
            int blocking = 0, blocks = 0, total = 0, topBlocking = 0;

            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++)
                {
                    LabObstacle o = grid[Idx(x, y)];
                    if (o == LabObstacle.None) continue;

                    total++;

                    if (Blocks(o))
                    {
                        blocking++;
                        if (y == height - 1) topBlocking++;
                    }

                    if (o == LabObstacle.Block) blocks++;
                }

            if (total == 0) return false;
            if (blocking > MaxBlockingCells) return false;

            // En üst sıra tahtanın ağzı: kapanırsa kristal giremez
            if (topBlocking > 2) return false;

            if (spec.RequireBlock && blocks < 2) return false;

            if (!spec.BlockClusters && HasBlockSquare()) return false;

            int hp = HitPoints(spec);
            if (hp < spec.MinHitPoints || hp > spec.MaxHitPoints) return false;

            if (CountUnfeedable() > MaxUnfeedable) return false;

            return true;
        }

        int HitPoints(LevelSpec spec)
        {
            int hp = 0;

            foreach (LabObstacle o in grid)
            {
                switch (o)
                {
                    case LabObstacle.Transparent: hp += 1; break;
                    case LabObstacle.Solid: hp += 2; break;
                    case LabObstacle.Block: hp += spec.BlockHits; break;
                }
            }

            return hp;
        }

        bool HasBlockSquare()
        {
            for (int x = 0; x + 1 < width; x++)
                for (int y = 0; y + 1 < height; y++)
                    if (grid[Idx(x, y)] == LabObstacle.Block && grid[Idx(x + 1, y)] == LabObstacle.Block &&
                        grid[Idx(x, y + 1)] == LabObstacle.Block && grid[Idx(x + 1, y + 1)] == LabObstacle.Block)
                        return true;

            return false;
        }

        // Yukarıdan (dikey ya da Board'un çapraz akışıyla) kristal alamayan açık kareler.
        // Başta dolular ama bir kez boşalınca engel kırılana kadar boş kalırlar.
        int CountUnfeedable()
        {
            bool[] feed = new bool[grid.Length];
            int dead = 0;

            for (int y = height - 1; y >= 0; y--)
            {
                for (int x = 0; x < width; x++)
                {
                    int i = Idx(x, y);
                    if (Blocks(grid[i])) continue;

                    bool f = y == height - 1
                          || feed[Idx(x, y + 1)]
                          || (x > 0 && feed[Idx(x - 1, y + 1)])
                          || (x + 1 < width && feed[Idx(x + 1, y + 1)]);

                    feed[i] = f;
                    if (!f) dead++;
                }
            }

            return dead;
        }

        static bool Blocks(LabObstacle o) => o == LabObstacle.Solid || o == LabObstacle.Block;

        int Idx(int x, int y) => y * width + x;

        void Set(int x, int y, LabObstacle v) => grid[Idx(x, y)] = v;
    }
}
