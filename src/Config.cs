using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace TableManager
{
    /// <summary>Как узнать стол рума: процесс, класс и заголовок окна (регулярные
    /// выражения без учёта регистра). Exclude — лобби и служебные окна.</summary>
    public class RoomRule
    {
        public string Name { get; set; }
        public string Process { get; set; }

        /// <summary>Класс окна — когда у стола и лобби одинаковый заголовок
        /// (CoinPoker: стол — окно Unity, лобби — Chrome). Пусто — любой.</summary>
        public string Class { get; set; }

        public string Table { get; set; }
        public string Exclude { get; set; }

        /// <summary>"largest" — самое большое окно рума считается лобби: для румов,
        /// где у стола и лобби одинаковые и заголовок, и класс (QQPK).</summary>
        public string Lobby { get; set; }

        /// <summary>Заголовок стола, по которому правило создано кнопкой «Указать
        /// стол»: исключения не должны его задеть.</summary>
        public string Sample { get; set; }
    }

    /// <summary>Зона раскладки: часть рабочей области монитора, разбитая на сетку.
    /// Monitor: "primary", "secondary" или имя устройства (\\.\DISPLAY2).
    /// X, Y, W, H — доли рабочей области (0..1).</summary>
    public class Zone
    {
        public string Monitor { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double W { get; set; }
        public double H { get; set; }
        public int Columns { get; set; }
        public int Rows { get; set; }

        /// <summary>Порядок заполнения ячеек: "rows" — по строкам слева направо,
        /// "rows-rtl" — по строкам справа налево, "columns" — по столбцам слева,
        /// "columns-rtl" — по столбцам справа налево (сверху вниз в столбце).</summary>
        public string Order { get; set; }
    }

    /// <summary>Уровень раскладки: набор зон. Уровень действует, пока столов не
    /// больше MaxTables (0 — сколько ячеек), дальше — следующий уровень.</summary>
    public class Level
    {
        public int MaxTables { get; set; }
        public List<Zone> Zones { get; set; }
    }

    public class Config
    {
        public List<RoomRule> Rooms { get; set; }

        /// <summary>Уровни по числу столов. Зоны уровня заполняются по порядку.</summary>
        public List<Level> Levels { get; set; }

        /// <summary>На сколько столов меньше вместимости прошлого уровня нужно
        /// опуститься, чтобы вернуться к нему (без дёрганья на границе).</summary>
        public int Hysteresis { get; set; }

        /// <summary>Зазор между столами, px.</summary>
        public int Gap { get; set; }

        /// <summary>"center" — стол по центру ячейки, "topleft" — в углу.</summary>
        public string Align { get; set; }

        /// <summary>Как часто искать новые столы, мс.</summary>
        public int PollMs { get; set; }

        /// <summary>Своя клавиша менеджера: расставить все столы заново.</summary>
        public string RelayoutHotkey { get; set; }

        /// <summary>Своя клавиша: показать ячейки на 3 секунды.</summary>
        public string ShowSlotsHotkey { get; set; }

        /// <summary>Своя клавиша: окно под курсором — стол (и такие же окна тоже).</summary>
        public string PickTableHotkey { get; set; }

        /// <summary>Своя клавиша: окно под курсором — не стол.</summary>
        public string NotTableHotkey { get; set; }

        /// <summary>true — сдвинутый вручную стол остаётся, где его оставили, до
        /// общей расстановки. false — все столы только по ячейкам: сдвинутый стол
        /// возвращается в свою ячейку, брошенный на другую — встаёт в неё (если
        /// она занята — столы меняются местами).</summary>
        public bool RespectManualMoves { get; set; }

#if ACTION_KEYS
        /// <summary>Клавиши за столами (только в сборке с клавишами).</summary>
        public ActionKeysConfig ActionKeys { get; set; }
#endif

        public static Config Default()
        {
            return new Config
            {
                Gap = 0,
                Align = "center",
                PollMs = 400,
                RelayoutHotkey = "Ctrl+Alt+L",
                ShowSlotsHotkey = "Ctrl+Alt+K",
                PickTableHotkey = "Ctrl+Alt+T",
                NotTableHotkey = "Ctrl+Alt+N",
                RespectManualMoves = false,
#if ACTION_KEYS
                ActionKeys = ActionKeysConfig.Default(),
#endif
                Hysteresis = 2,
                Levels = new List<Level>
                {
                    // До 14 столов: основной 3×2 по столбцам слева направо (верх-низ), потом
                    // ультраширокий по столбцам справа налево (тоже верх-низ).
                    new Level
                    {
                        MaxTables = 14,
                        Zones = new List<Zone>
                        {
                            new Zone { Monitor = "primary", X = 0, Y = 0, W = 1, H = 1, Columns = 3, Rows = 2, Order = "columns" },
                            new Zone { Monitor = "secondary", X = 0, Y = 0, W = 1, H = 1, Columns = 4, Rows = 2, Order = "columns-rtl" },
                        },
                    },
                    // Больше 14: основной 4×3, по-прежнему первым.
                    new Level
                    {
                        MaxTables = 0,
                        Zones = new List<Zone>
                        {
                            new Zone { Monitor = "primary", X = 0, Y = 0, W = 1, H = 1, Columns = 4, Rows = 3, Order = "columns" },
                            new Zone { Monitor = "secondary", X = 0, Y = 0, W = 1, H = 1, Columns = 4, Rows = 2, Order = "columns-rtl" },
                        },
                    },
                },
                Rooms = new List<RoomRule>
                {
                    // Шаблоны строгие: лобби и окна турниров не должны попадать в ячейки.
                    // Заголовки — как у клиентов без Jurojin (он дописывает к ним своё)
                    // и для турниров, и для кэша.
                    new RoomRule { Name = "GG / PokerOK", Process = "^GGnet$", Table = @"- Стол \d|- Table \d", Exclude = "Лобби|Lobby" },
                    new RoomRule { Name = "Winamax", Process = "^Winamax$", Table = @"^Winamax .+\(\d+\)\(#\d+\)|Hold'?em|Omaha", Exclude = "" },
                    new RoomRule { Name = "AKs Poker", Process = "^AKs Poker$", Table = @" - Blinds \d|^\S+ - [\d.]+/[\d.]+|NLHE|PLO|Hold'?em|Omaha", Exclude = @"Standard Time|Lobby|^\d{1,2}:\d\d\b" },
                    new RoomRule { Name = "CoinPoker", Process = "^CoinPoker$", Class = "^UnityWndClass$", Table = "", Exclude = "" },
                    new RoomRule { Name = "Redstar", Process = "^PokerClient$", Table = @"\d{5,} \| |\| Level \d", Exclude = "Lobby|Лобби" },
                    new RoomRule { Name = "TigerGaming / Pokerdom (WPN)", Process = "^GameClient$", Table = @"Hold'?em|Холдем|Omaha|Омаха|- (Hand|Рука): \d", Exclude = "Lobby|Лобби" },
                    new RoomRule { Name = "PokerPlanets", Process = "^PokerPlanets$", Table = "Blinds:|Hold'em|Omaha", Exclude = "PokerPlanets - user|Lobby" },
                    // У QQPK все окна называются «QQPK» и одного типа: лобби — самое большое.
                    new RoomRule { Name = "QQPK", Process = "^QQPK$", Class = "^Chrome_WidgetWin_1$", Table = "", Exclude = "", Lobby = "largest" },
                },
            };
        }

        public static string PathNextToExe(string fileName)
        {
            return System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);
        }

        /// <summary>Читает config.json; если его нет — создаёт с настройками по умолчанию.</summary>
        public static Config Load(string path)
        {
            if (!File.Exists(path))
            {
                var fresh = Default();
                Save(path, fresh);
                return fresh;
            }
            var config = new JavaScriptSerializer().Deserialize<Config>(File.ReadAllText(path, Encoding.UTF8));
            var defaults = Default();
            if (config.Rooms == null) config.Rooms = defaults.Rooms;
            if (config.Levels == null || config.Levels.Count == 0) config.Levels = defaults.Levels;
            foreach (var level in config.Levels)
            {
                if (level.Zones == null) level.Zones = new List<Zone>();
            }
            if (config.Hysteresis < 0) config.Hysteresis = 0;
            if (config.PollMs < 100) config.PollMs = defaults.PollMs;
            if (string.IsNullOrEmpty(config.Align)) config.Align = defaults.Align;
            // Клавиши, которых не было в старом config.json; "" — без клавиши.
            if (config.PickTableHotkey == null) config.PickTableHotkey = defaults.PickTableHotkey;
            if (config.NotTableHotkey == null) config.NotTableHotkey = defaults.NotTableHotkey;
#if ACTION_KEYS
            // config.json обычного расстановщика — дописать раздел клавиш, чтобы его было видно.
            var noKeys = config.ActionKeys == null;
            config.ActionKeys = ActionKeysConfig.Fill(config.ActionKeys);
            if (noKeys) Save(path, config);
#endif
            return config;
        }

        public static void Save(string path, Config config)
        {
            var json = new JavaScriptSerializer().Serialize(config);
            File.WriteAllText(path, Json.Pretty(json), new UTF8Encoding(false));
        }
    }

    /// <summary>Отступы для JSON из JavaScriptSerializer (он пишет в одну строку)
    /// и кириллица без \uXXXX, чтобы config.json было удобно править руками.</summary>
    public static class Json
    {
        public static string Pretty(string json)
        {
            var sb = new StringBuilder();
            var indent = 0;
            var inString = false;
            for (var i = 0; i < json.Length; i++)
            {
                var ch = json[i];
                if (inString)
                {
                    if (ch == '\\' && i + 1 < json.Length)
                    {
                        // \uXXXX с не-ASCII символом — пишем сам символ.
                        int code;
                        if (json[i + 1] == 'u' && i + 5 < json.Length
                            && int.TryParse(json.Substring(i + 2, 4), System.Globalization.NumberStyles.HexNumber, null, out code)
                            && code >= 0x80 && (code < 0xD800 || code > 0xDFFF))
                        {
                            sb.Append((char)code);
                            i += 5;
                            continue;
                        }
                        sb.Append(ch).Append(json[i + 1]);
                        i++;
                        continue;
                    }
                    if (ch == '"') inString = false;
                    sb.Append(ch);
                    continue;
                }
                if (ch == '"')
                {
                    inString = true;
                    sb.Append(ch);
                    continue;
                }
                switch (ch)
                {
                    case '{':
                    case '[':
                        sb.Append(ch).Append('\n').Append(' ', ++indent * 2);
                        break;
                    case '}':
                    case ']':
                        sb.Append('\n').Append(' ', --indent * 2).Append(ch);
                        break;
                    case ',':
                        sb.Append(ch).Append('\n').Append(' ', indent * 2);
                        break;
                    case ':':
                        sb.Append(": ");
                        break;
                    default:
                        sb.Append(ch);
                        break;
                }
            }
            return sb.ToString();
        }
    }
}
