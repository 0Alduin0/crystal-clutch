using UnityEditor;
using UnityEngine;

// LevelData'nın Inspector'ı: normal alanların altına tıklanabilir bir ENGEL
// TAHTASI çiziyor. Yerleşimi 's' ve 'k' harfleriyle elle yazmak zorunda değilsin.
//
// Tahta ekranda gördüğün gibi duruyor: en üstteki satır tahtanın üst sırası.
[CustomEditor(typeof(LevelData))]
public class LevelDataEditor : Editor
{
    const float CellSize = 26f;

    // Fırça editör oturumu boyunca hatırlansın - her karede yeniden seçmek yorucu
    static ObstacleType brush = ObstacleType.Solid;

    // ObstacleType sırasıyla birebir: toolbar indeksi doğrudan enum değeri
    static readonly string[] BrushLabels = { "Boş", "Şeffaf", "Katı", "Blok" };

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        LevelData level = (LevelData)target;

        EditorGUILayout.Space(12);
        EditorGUILayout.LabelField("Engel Tahtası", EditorStyles.boldLabel);

        if (level.width <= 0 || level.height <= 0)
        {
            EditorGUILayout.HelpBox("Grid genişliği/yüksekliği 0 - önce onları ayarla.",
                                    MessageType.Warning);
            return;
        }

        // Asset'i sırf ona BAKINCA kirletmemek için önce bellekte normalize
        // edilmiş bir kopya çıkarıyoruz. Diske ancak gerçekten bir şey
        // değiştirildiğinde yazıyoruz.
        string[] layout = ReadLayout(level);
        bool changed = false;

        changed |= DrawToolbar(layout, level);
        changed |= DrawGrid(layout, level);

        DrawSummary(layout, level);

        if (changed) WriteLayout(layout);
    }

    // ==================== ÇİZİM ====================

    bool DrawToolbar(string[] layout, LevelData level)
    {
        EditorGUILayout.BeginHorizontal();

        EditorGUILayout.LabelField("Fırça", GUILayout.Width(40));
        brush = (ObstacleType)GUILayout.Toolbar((int)brush, BrushLabels);

        EditorGUILayout.EndHorizontal();

        bool changed = false;

        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button("Tümünü Temizle"))
        {
            FillAll(layout, level, ObstacleType.None);
            changed = true;
        }

        if (GUILayout.Button("Çerçeveyi Katı Yap"))
        {
            FillBorder(layout, level);
            changed = true;
        }

        EditorGUILayout.EndHorizontal();

        return changed;
    }

    bool DrawGrid(string[] layout, LevelData level)
    {
        bool changed = false;

        EditorGUILayout.Space(4);

        for (int row = 0; row < level.height; row++)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            for (int x = 0; x < level.width; x++)
            {
                ObstacleType type = LevelData.ParseChar(layout[row][x]);

                Color previous = GUI.backgroundColor;
                GUI.backgroundColor = ColorFor(type, level.blockTheme);

                if (GUILayout.Button(LabelFor(type),
                                     GUILayout.Width(CellSize), GUILayout.Height(CellSize)))
                {
                    // Aynı fırçayla ikinci kez tıklamak siler - silgi aramaya gerek yok
                    ObstacleType paint = (type == brush) ? ObstacleType.None : brush;

                    layout[row] = ReplaceAt(layout[row], x, LevelData.ToChar(paint));
                    changed = true;
                }

                GUI.backgroundColor = previous;
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.Space(4);

        EditorGUILayout.HelpBox(
            "Üstteki satır tahtanın EN ÜST sırası.\n\n" +
            "ŞEFFAF: arkasındaki kristal görünür, kare normal oynanır. " +
            "Üstündeki kristal patlayınca kırılır ve kare normale döner.\n\n" +
            "KATI: kristal yok, kare oynanamaz, kristal üstünden düşemez. " +
            "YANINDAKİ patlama kırar ve ŞEFFAFA döner.\n\n" +
            "BLOK: katı gibi ama çok vuruşlu. Her YANINDAKİ patlamada çatlar, " +
            "son vuruşta doğrudan boşalır. Vuruş sayısı ve tema (lav/buz) yukarıda.",
            MessageType.Info);

        return changed;
    }

    void DrawSummary(string[] layout, LevelData level)
    {
        int transparent = 0;
        int solid = 0;
        int block = 0;

        for (int row = 0; row < level.height; row++)
            for (int x = 0; x < level.width; x++)
            {
                ObstacleType t = LevelData.ParseChar(layout[row][x]);

                if (t == ObstacleType.Transparent) transparent++;
                else if (t == ObstacleType.Solid) solid++;
                else if (t == ObstacleType.Block) block++;
            }

        EditorGUILayout.LabelField($"Toplam: {transparent + solid + block} engel " +
                                   $"({transparent} şeffaf, {solid} katı, {block} blok)");

        // Katı ve blok aynı şekilde kapatıyor - uyarılar ikisini birlikte sayıyor
        solid += block;

        // Levelin TEK hedefi engelleri kırmak. Engelsiz level kazanılamaz.
        if (transparent + solid == 0)
            EditorGUILayout.HelpBox(
                "Tahtada hiç engel yok. Levelin tek hedefi engelleri kırmak, yani " +
                "bu level KAZANILAMAZ. Aşağıdaki tahtadan engel yerleştir.",
                MessageType.Error);

        // Katı engel yalnızca komşusundaki patlamayla kırılıyor; dört yanı da
        // katı olan bir engele hiçbir kristal komşu olamaz.
        if (solid > 0)
            EditorGUILayout.HelpBox(
                "Katı engel ve blok yalnızca KOMŞU karedeki patlamayla kırılır. Dört yanı " +
                "da katıyla çevrili bir engel asla kırılamaz - blok kümelerinin " +
                "kenarlarının kristale açık olduğundan emin ol.",
                MessageType.Warning);

        // Katı engel sütunu böler; altındaki kareler yukarıdan beslenemez,
        // kristal oraya yandaki sütundan ÇAPRAZ akarak giriyor.
        if (solid > 0)
            EditorGUILayout.HelpBox(
                "Katı engel ve blok sütunu böler: altında kalan karelere kristal yukarıdan " +
                "inemez, yandaki sütundan çapraz akar. Bloğu ne kadar genişletirsen " +
                "akışın altına ulaşması o kadar zorlaşır: 3+ genişlikteki bir bloğun " +
                "TAM ALTINDAKİ orta kareler hiçbir çaprazdan beslenemez ve kalıcı " +
                "olarak boş kalır. Sorun değil, ama tasarlarken hesaba kat.",
                MessageType.None);
    }

    // ==================== OKUMA / YAZMA ====================

    // Yerleşimi width/height ile birebir aynı boyutta, bellekte bir kopya olarak
    // döndürür. Asset'e dokunmaz.
    static string[] ReadLayout(LevelData level)
    {
        string[] layout = new string[level.height];

        for (int row = 0; row < level.height; row++)
        {
            string value = (level.obstacleRows != null && row < level.obstacleRows.Length)
                ? level.obstacleRows[row]
                : null;

            if (string.IsNullOrEmpty(value)) value = string.Empty;

            if (value.Length < level.width) value = value.PadRight(level.width, LevelData.CharNone);
            else if (value.Length > level.width) value = value.Substring(0, level.width);

            layout[row] = value;
        }

        return layout;
    }

    void WriteLayout(string[] layout)
    {
        serializedObject.Update();

        SerializedProperty rows = serializedObject.FindProperty("obstacleRows");
        rows.arraySize = layout.Length;

        for (int row = 0; row < layout.Length; row++)
            rows.GetArrayElementAtIndex(row).stringValue = layout[row];

        serializedObject.ApplyModifiedProperties();
    }

    // ==================== DÜZENLEME ====================

    static void FillAll(string[] layout, LevelData level, ObstacleType type)
    {
        string filled = new string(LevelData.ToChar(type), level.width);

        for (int row = 0; row < level.height; row++)
            layout[row] = filled;
    }

    static void FillBorder(string[] layout, LevelData level)
    {
        char solid = LevelData.ToChar(ObstacleType.Solid);

        for (int row = 0; row < level.height; row++)
        {
            bool edgeRow = (row == 0 || row == level.height - 1);

            for (int x = 0; x < level.width; x++)
            {
                if (!edgeRow && x != 0 && x != level.width - 1) continue;

                layout[row] = ReplaceAt(layout[row], x, solid);
            }
        }
    }

    static string ReplaceAt(string line, int index, char c)
    {
        char[] chars = line.ToCharArray();
        chars[index] = c;

        return new string(chars);
    }

    // ==================== GÖRÜNÜM ====================

    static Color ColorFor(ObstacleType type, BlockTheme theme)
    {
        switch (type)
        {
            case ObstacleType.Transparent: return new Color(0.55f, 0.85f, 1f);
            case ObstacleType.Solid:       return new Color(0.85f, 0.6f, 0.35f);

            // Temaya göre: tahtaya bakınca levelin hangi blokla oynandığı belli olsun
            case ObstacleType.Block:
                return theme == BlockTheme.Buz ? new Color(0.75f, 0.95f, 1f)
                                               : new Color(1f, 0.4f, 0.2f);
        }

        return new Color(0.8f, 0.8f, 0.8f);
    }

    static string LabelFor(ObstacleType type)
    {
        switch (type)
        {
            case ObstacleType.Transparent: return "Ş";
            case ObstacleType.Solid:       return "K";
            case ObstacleType.Block:       return "B";
        }

        return "";
    }
}
