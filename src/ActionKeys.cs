#if ACTION_KEYS
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace TableManager
{
    /// <summary>Действие за столом: клавиша, которая срабатывает над столом, и своя
    /// клавиша, которой запоминается кнопка этого действия.</summary>
    public class TableAction
    {
        public string Name { get; set; }

        /// <summary>«Space», «S», «Shift+D»… Срабатывает только над столом.</summary>
        public string Key { get; set; }

        /// <summary>Курсор на кнопке действия → эта клавиша запоминает кнопку для рума стола.</summary>
        public string RecordHotkey { get; set; }
    }

    /// <summary>Кнопка действия у рума: место на столе в долях клиентской области
    /// (0…1). Стол меняет размер, пропорции сохраняются — кнопка остаётся там же.
    /// Process — программа рума: кнопка подходит и к другим румам той же программы
    /// (турниры и кэш одного клиента — разные правила, а клиент один).</summary>
    public class ActionButton
    {
        public string Room { get; set; }
        public string Process { get; set; }
        public string Action { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
    }

    /// <summary>Собственная клавиша клиента для действия (способ "keys"): то
    /// сочетание, что назначено действию в настройках самого клиента. Room — имя
    /// рума или программы (PokerClient).</summary>
    public class ClientKey
    {
        public string Room { get; set; }
        public string Action { get; set; }
        public string Keys { get; set; }
    }

    /// <summary>Клавиши за столами (сборка с клавишами).</summary>
    public class ActionKeysConfig
    {
        /// <summary>Способы нажатия: клик прямо в окно стола (курсор не двигается),
        /// клик курсором (курсор на кнопку и обратно), собственные клавиши клиента.</summary>
        public const string DirectMode = "direct", CursorMode = "cursor", KeysMode = "keys";

        /// <summary>Программа GG / ПокерОК: на её столах программа ничего не нажимает —
        /// ни клики, ни клавиши клиента, что бы ни было в config.json.</summary>
        public const string NoKeysProcess = "GGnet";

        /// <summary>false — клавиши за столами выключены совсем.</summary>
        public bool Enabled { get; set; }

        /// <summary>true — клавиши работают, только когда активно окно покерного
        /// клиента; в мессенджере или браузере они печатают как обычно.</summary>
        public bool OnlyWhenPokerActive { get; set; }

        /// <summary>Стол под курсором сразу становится активным, без клика.</summary>
        public bool? FocusUnderMouse { get; set; }

        /// <summary>Рамка вокруг активного стола.</summary>
        public bool? FocusBorder { get; set; }

        /// <summary>Стол стал активным — английская раскладка: горячие клавиши
        /// клиентов (S, D…) в русской раскладке не срабатывают.</summary>
        public bool? EnglishLayout { get; set; }

        /// <summary>Пауза между шагами нажатия, мс: некоторые клиенты не успевают.</summary>
        public int ClickDelayMs { get; set; }

        /// <summary>Своя клавиша: выключить/включить клавиши за столами (например,
        /// чтобы написать в чат стола).</summary>
        public string ToggleHotkey { get; set; }

        public List<TableAction> Actions { get; set; }

        /// <summary>Способ нажатия по умолчанию: "direct", "cursor" или "keys".</summary>
        public string ClickMode { get; set; }

        /// <summary>Способ нажатия для рума или программы, если не как ClickMode.</summary>
        public Dictionary<string, string> RoomClickModes { get; set; }

        /// <summary>Собственные клавиши клиентов для способа "keys".</summary>
        public List<ClientKey> ClientKeys { get; set; }

        /// <summary>Румы (Name из Rooms), где клавиши не работают.</summary>
        public List<string> ExcludeRooms { get; set; }

        /// <summary>Записанные кнопки: рум + действие → место на столе.</summary>
        public List<ActionButton> Buttons { get; set; }

        public static ActionKeysConfig Default()
        {
            return new ActionKeysConfig
            {
                Enabled = true,
                OnlyWhenPokerActive = true,
                FocusUnderMouse = true,
                FocusBorder = true,
                EnglishLayout = true,
                ClickDelayMs = 20,
                ToggleHotkey = "Ctrl+Alt+H",
                Actions = new List<TableAction>
                {
                    // Ctrl+Alt+Space часто занят (быстрый ввод Claude и др.) — F, как Fold.
                    new TableAction { Name = "Фолд", Key = "Space", RecordHotkey = "Ctrl+Alt+F" },
                    new TableAction { Name = "Чек/колл", Key = "S", RecordHotkey = "Ctrl+Alt+S" },
                    new TableAction { Name = "Ставка", Key = "D", RecordHotkey = "Ctrl+Alt+D" },
                },
                ClickMode = DirectMode,
                RoomClickModes = new Dictionary<string, string>(),
                ClientKeys = new List<ClientKey>(),
                ExcludeRooms = new List<string>(),
                Buttons = new List<ActionButton>(),
            };
        }

        /// <summary>Чего нет в config.json — по умолчанию.</summary>
        public static ActionKeysConfig Fill(ActionKeysConfig config)
        {
            var defaults = Default();
            if (config == null) return defaults;
            if (config.Actions == null) config.Actions = defaults.Actions;
            if (config.FocusUnderMouse == null) config.FocusUnderMouse = defaults.FocusUnderMouse;
            if (config.FocusBorder == null) config.FocusBorder = defaults.FocusBorder;
            if (config.EnglishLayout == null) config.EnglishLayout = defaults.EnglishLayout;
            if (string.IsNullOrEmpty(config.ClickMode)) config.ClickMode = defaults.ClickMode;
            if (config.RoomClickModes == null) config.RoomClickModes = defaults.RoomClickModes;
            if (config.ClientKeys == null) config.ClientKeys = defaults.ClientKeys;
            if (config.ExcludeRooms == null) config.ExcludeRooms = new List<string>();
            if (config.Buttons == null) config.Buttons = new List<ActionButton>();
            if (config.ToggleHotkey == null) config.ToggleHotkey = defaults.ToggleHotkey;
            if (config.ClickDelayMs <= 0) config.ClickDelayMs = defaults.ClickDelayMs;
            // GG из прежних версий (свои клавиши клиента по умолчанию) — убрать: не работают.
            foreach (var key in config.RoomClickModes.Keys.Where(k => Same(k, NoKeysProcess)).ToList()) config.RoomClickModes.Remove(key);
            config.ClientKeys.RemoveAll(k => Same(k.Room, NoKeysProcess));
            return config;
        }

        public bool IsExcluded(string room)
        {
            return ExcludeRooms.Any(r => Same(r, room));
        }

        /// <summary>Программа, на столах которой клавиши не работают никогда (GG).</summary>
        public static bool IsNoKeys(string process)
        {
            return Same(process, NoKeysProcess);
        }

        /// <summary>Способ нажатия для стола: по руму, потом по программе, потом общий.</summary>
        public string ModeOf(string room, string process)
        {
            foreach (var pair in RoomClickModes)
            {
                if (Same(pair.Key, room) && IsMode(pair.Value)) return pair.Value.ToLowerInvariant();
            }
            foreach (var pair in RoomClickModes)
            {
                if (Same(pair.Key, process) && IsMode(pair.Value)) return pair.Value.ToLowerInvariant();
            }
            return IsMode(ClickMode) ? ClickMode.ToLowerInvariant() : DirectMode;
        }

        /// <summary>Способ нажатия задан именно для рума (из меню).</summary>
        public void SetMode(string room, string mode)
        {
            foreach (var key in RoomClickModes.Keys.Where(k => Same(k, room)).ToList()) RoomClickModes.Remove(key);
            RoomClickModes[room] = mode;
        }

        private static bool IsMode(string mode)
        {
            return Same(mode, DirectMode) || Same(mode, CursorMode) || Same(mode, KeysMode);
        }

        /// <summary>Кнопка стола: записанная для этого рума, иначе — для другого рума той же программы.</summary>
        public ActionButton ButtonOf(string room, string process, string action)
        {
            return Buttons.FirstOrDefault(b => b.Room == room && b.Action == action)
                ?? Buttons.FirstOrDefault(b => Same(b.Process, process) && b.Action == action);
        }

        public ClientKey ClientKeyOf(string room, string process, string action)
        {
            return ClientKeys.FirstOrDefault(k => Same(k.Room, room) && k.Action == action)
                ?? ClientKeys.FirstOrDefault(k => Same(k.Room, process) && k.Action == action);
        }

        private static bool Same(string a, string b)
        {
            return !string.IsNullOrEmpty(a) && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>Клавиши за столами: клавиша над столом → одно нажатие кнопки этого
    /// действия у рума. Столы не читаются и решений программа не принимает: одно
    /// нажатие игрока — одно нажатие кнопки, которую он сам выбрал. Над другими
    /// окнами и в выключенных румах клавиши работают как обычно. Здесь же «фокус
    /// под мышкой» — стол под курсором становится активным — и рамка вокруг
    /// активного стола.</summary>
    public class ActionKeys : IDisposable
    {
        /// <summary>Номера своих клавиш в HotkeyWindow: вкл/выкл и запись кнопок.</summary>
        public const int ToggleId = 5;
        public const int FirstRecordId = 10;

        /// <summary>Сколько после последней набранной в другом окне клавиши фокус
        /// не уводится на стол: печатают в мессенджере, мышь задела стол.</summary>
        private static readonly TimeSpan TypingPause = TimeSpan.FromSeconds(1.5);

        private class Binding
        {
            public int Action;
            public uint Modifiers;
            public int Key;
        }

        private readonly Func<List<TableWindow>> _tables;
        private readonly Action<string> _notify;
        private readonly Action<string> _log;
        private readonly Action _save;
        private readonly KeyNative.HookProc _proc;
        private readonly object _pressLock = new object();
        private readonly System.Windows.Forms.Timer _focusTimer;
        private readonly FocusFrame _frame = new FocusFrame();

        /// <summary>Наши клавиши, которые сейчас зажаты: их автоповтор и отпускание
        /// тоже не уходят в клиент.</summary>
        private readonly HashSet<int> _held = new HashSet<int>();

        /// <summary>Подсказки и ошибки — по разу на рум и действие.</summary>
        private readonly HashSet<string> _hinted = new HashSet<string>();

        private readonly ToolStripMenuItem _toggleItem;
        private readonly ToolStripMenuItem _focusItem;
        private readonly ToolStripMenuItem _borderItem;
        private readonly ToolStripMenuItem _englishItem;
        private readonly List<ToolStripMenuItem> _recordItems = new List<ToolStripMenuItem>();
        private readonly List<ToolStripItem> _roomItems = new List<ToolStripItem>();
        private List<Binding> _bindings = new List<Binding>();
        private Config _config;
        private IntPtr _hook;
        private bool _suspended;
        private DateTime _lastTyped = DateTime.MinValue;
        private POINT _lastCursor;

        /// <summary>Активный стол прошлого тика: раскладка меняется, когда активным
        /// становится другой стол.</summary>
        private IntPtr _lastActive;
        private bool _noEnglishLogged;

        /// <summary>Программы активного окна, из-за которых стол не стал активным, — уже в журнале.</summary>
        private readonly HashSet<string> _focusDenied = new HashSet<string>();

        /// <summary>Подменю «Клавиши за столами» для трея.</summary>
        public ToolStripMenuItem Menu { get; private set; }

        public ActionKeys(Config config, Func<List<TableWindow>> tables, Action<string> notify, Action<string> log, Action save)
        {
            _tables = tables;
            _notify = notify;
            _log = log;
            _save = save;
            _proc = OnKey;
            _toggleItem = new ToolStripMenuItem("Клавиши включены", null, delegate { Toggle(); });
            _focusItem = new ToolStripMenuItem("Стол под курсором — активный", null, delegate
            {
                Settings.FocusUnderMouse = Settings.FocusUnderMouse != true;
                _save();
            });
            _borderItem = new ToolStripMenuItem("Рамка вокруг активного стола", null, delegate
            {
                Settings.FocusBorder = Settings.FocusBorder != true;
                _save();
            });
            _englishItem = new ToolStripMenuItem("Английская раскладка на активном столе", null, delegate
            {
                Settings.EnglishLayout = Settings.EnglishLayout != true;
                _save();
            });
            Menu = new ToolStripMenuItem("Клавиши за столами");
            Menu.DropDownOpening += delegate { RefreshMenu(); };
            _focusTimer = new System.Windows.Forms.Timer { Interval = 50 };
            _focusTimer.Tick += delegate { OnFocusTick(); };
            Apply(config);
            _focusTimer.Start();
        }

        private ActionKeysConfig Settings
        {
            get { return _config.ActionKeys; }
        }

        public void Apply(Config config)
        {
            _config = config;
            _bindings = new List<Binding>();
            for (var i = 0; i < Settings.Actions.Count; i++)
            {
                var text = Settings.Actions[i].Key;
                if (string.IsNullOrEmpty(text)) continue;
                uint modifiers;
                Keys key;
                if (!HotkeyWindow.Parse(text, out modifiers, out key))
                {
                    _log("Клавиша за столом «" + text + "» (" + Settings.Actions[i].Name + ") не понята");
                    continue;
                }
                _bindings.Add(new Binding { Action = i, Modifiers = modifiers, Key = (int)key });
            }
            _hinted.Clear();
            _held.Clear();

            Menu.DropDownItems.Clear();
            _recordItems.Clear();
            _roomItems.Clear();
            Menu.DropDownItems.Add(_toggleItem);
            Menu.DropDownItems.Add(_focusItem);
            Menu.DropDownItems.Add(_borderItem);
            Menu.DropDownItems.Add(_englishItem);
            Menu.DropDownItems.Add(new ToolStripSeparator());
            for (var i = 0; i < Settings.Actions.Count; i++)
            {
                var action = i;
                var item = new ToolStripMenuItem(RecordLabel(i), null, delegate
                {
                    PickOverlay.Run("Кликните по кнопке «" + Settings.Actions[action].Name + "» на столе", p => Record(action, p));
                });
                _recordItems.Add(item);
                Menu.DropDownItems.Add(item);
            }
            Menu.DropDownItems.Add(new ToolStripSeparator());
            Menu.DropDownItems.Add(new ToolStripMenuItem("Румы (без галочки — без клавиш):") { Enabled = false });

            if (Settings.Enabled && _hook == IntPtr.Zero) Hook();
            if (!Settings.Enabled) Unhook();
        }

        /// <summary>Свои клавиши: вкл/выкл и запись кнопок (регистрирует трей).</summary>
        public void RegisterHotkeys(Action<int, string, ToolStripMenuItem, string> register)
        {
            register(ToggleId, Settings.ToggleHotkey, _toggleItem, "Клавиши включены");
            for (var i = 0; i < _recordItems.Count; i++)
            {
                register(FirstRecordId + i, Settings.Actions[i].RecordHotkey, _recordItems[i], RecordLabel(i));
            }
        }

        public void OnHotkey(int id)
        {
            if (id == ToggleId)
            {
                Toggle();
                return;
            }
            var action = id - FirstRecordId;
            if (action < 0 || action >= Settings.Actions.Count) return;
            POINT p;
            if (Native.GetCursorPos(out p)) Record(action, p);
        }

        /// <summary>Клавиши словами — для журнала.</summary>
        public string Describe()
        {
            if (!Settings.Enabled) return "выключены";
            var text = string.Join(", ", Settings.Actions.Select(a => a.Name + " — " + a.Key).ToArray())
                + "; нажатие: " + ModeName(Settings.ClickMode)
                + string.Concat(Settings.RoomClickModes.Select(p => ", " + p.Key + " — " + ModeName(p.Value)).ToArray())
                + (Settings.FocusUnderMouse == true ? "; стол под курсором — активный" : "")
                + (Settings.EnglishLayout == true ? "; на активном столе — английская раскладка" : "")
                + "; GG — без клавиш";
            if (Settings.ExcludeRooms.Count > 0) text += "; без клавиш: " + string.Join(", ", Settings.ExcludeRooms.ToArray());
            return text;
        }

        private static string ModeName(string mode)
        {
            switch ((mode ?? "").ToLowerInvariant())
            {
                case ActionKeysConfig.CursorMode:
                    return "клик с курсором";
                case ActionKeysConfig.KeysMode:
                    return "свои клавиши клиента";
                default:
                    return "клик без курсора";
            }
        }

        private string RecordLabel(int action)
        {
            return "Запомнить кнопку «" + Settings.Actions[action].Name + "»…";
        }

        private void Toggle()
        {
            if (!Settings.Enabled)
            {
                _notify("Клавиши за столами выключены в config.json (ActionKeys → Enabled)");
                return;
            }
            _suspended = !_suspended;
            _held.Clear();
            var text = _suspended ? "Клавиши за столами выключены — клавиши печатают как обычно" : "Клавиши за столами включены";
            _log(text);
            _notify(text);
        }

        private void RefreshMenu()
        {
            _toggleItem.Checked = Settings.Enabled && !_suspended;
            _focusItem.Checked = Settings.FocusUnderMouse == true;
            _borderItem.Checked = Settings.FocusBorder == true;
            _englishItem.Checked = Settings.EnglishLayout == true;
            foreach (var item in _roomItems) Menu.DropDownItems.Remove(item);
            _roomItems.Clear();
            var processes = new Dictionary<string, string>();
            foreach (var t in _tables()) processes[t.Room] = t.Process;
            var rooms = _config.Rooms.Select(r => string.IsNullOrEmpty(r.Name) ? r.Process : r.Name).Distinct().ToList();
            foreach (var room in rooms)
            {
                var item = RoomMenu(room, processes.ContainsKey(room) ? processes[room] : ProcessOfRule(room));
                _roomItems.Add(item);
                Menu.DropDownItems.Add(item);
            }
        }

        /// <summary>Программа рума из правила (^GGnet$ → GGnet), если стола рума сейчас нет.</summary>
        private string ProcessOfRule(string room)
        {
            var rule = _config.Rooms.FirstOrDefault(r => r.Name == room);
            if (rule == null || string.IsNullOrEmpty(rule.Process)) return null;
            var text = rule.Process.TrimStart('^').TrimEnd('$').Replace("\\", "");
            return text.IndexOfAny(new[] { '|', '(', '[', '*', '+', '?', '.' }) >= 0 ? null : text;
        }

        /// <summary>Подменю рума: клавиши вкл/выкл, способ нажатия, что записано.</summary>
        private ToolStripMenuItem RoomMenu(string room, string process)
        {
            if (ActionKeysConfig.IsNoKeys(process)) return new ToolStripMenuItem(room + " — без клавиш (GG)") { Enabled = false };
            var mode = Settings.ModeOf(room, process);
            var excluded = Settings.IsExcluded(room);
            var item = new ToolStripMenuItem(room + " — " + (excluded ? "без клавиш" : ModeName(mode))) { Checked = !excluded };
            item.DropDownItems.Add(new ToolStripMenuItem("Клавиши в этом руме", null, delegate { ToggleRoom(room); }) { Checked = !excluded });
            item.DropDownItems.Add(new ToolStripSeparator());
            foreach (var m in new[] { ActionKeysConfig.DirectMode, ActionKeysConfig.CursorMode, ActionKeysConfig.KeysMode })
            {
                var chosen = m;
                item.DropDownItems.Add(new ToolStripMenuItem(ModeName(m), null, delegate { SetMode(room, chosen); }) { Checked = mode == m });
            }
            item.DropDownItems.Add(new ToolStripSeparator());
            item.DropDownItems.Add(new ToolStripMenuItem(Recorded(room, process)) { Enabled = false });
            item.DropDownItems.Add(new ToolStripMenuItem(ClientKeysText(room, process)) { Enabled = false });
            return item;
        }

        /// <summary>Какие кнопки рума записаны — словами.</summary>
        private string Recorded(string room, string process)
        {
            var names = Settings.Actions.Where(a => Settings.ButtonOf(room, process, a.Name) != null).Select(a => a.Name).ToArray();
            return names.Length == 0 ? "Кнопки на столе не записаны" : "Кнопки на столе: " + string.Join(", ", names);
        }

        private string ClientKeysText(string room, string process)
        {
            var keys = Settings.Actions.Select(a => new { a.Name, Key = Settings.ClientKeyOf(room, process, a.Name) })
                .Where(k => k.Key != null).Select(k => k.Name + " — " + k.Key.Keys).ToArray();
            return keys.Length == 0 ? "Свои клавиши клиента не заданы (config.json → ClientKeys)" : "Клавиши клиента: " + string.Join(", ", keys);
        }

        private void ToggleRoom(string room)
        {
            if (Settings.IsExcluded(room))
            {
                Settings.ExcludeRooms.RemoveAll(r => string.Equals(r, room, StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                Settings.ExcludeRooms.Add(room);
            }
            _save();
            _log(room + (Settings.IsExcluded(room) ? ": клавиши за столами выключены" : ": клавиши за столами включены"));
        }

        private void SetMode(string room, string mode)
        {
            Settings.SetMode(room, mode);
            _save();
            _hinted.Clear();
            _log(room + ": нажатие — " + ModeName(mode));
        }

        /// <summary>Запомнить кнопку действия в точке экрана — для рума стола под ней.</summary>
        private void Record(int action, POINT point)
        {
            try
            {
                var message = RecordAt(action, point);
                _log(message);
                _notify(message);
            }
            catch (Exception e)
            {
                _log("Ошибка: " + e);
            }
        }

        private string RecordAt(int action, POINT point)
        {
            var name = Settings.Actions[action].Name;
            var window = Desktop.WindowAt(point.X, point.Y);
            if (window == null) return "Под курсором нет окна программы";
            var table = _tables().FirstOrDefault(t => t.Handle == window.Handle);
            if (table == null)
            {
                var pick = string.IsNullOrEmpty(_config.PickTableHotkey) ? "меню → Указать стол" : _config.PickTableHotkey;
                return "Это окно не стол: " + window.Process + " «" + RuleBuilder.Short(window.Title)
                    + "». Сначала укажите стол (" + pick + "), потом запишите кнопку";
            }
            Box client;
            double x, y;
            if (!ClientBox(table.Handle, out client) || !ToFraction(client, point.X, point.Y, out x, out y))
            {
                return "Курсор не на столе " + table.Room;
            }
            if (ActionKeysConfig.IsNoKeys(table.Process)) return table.Room + ": на столах GG клавиши не работают — кнопка не записана";
            Settings.Buttons.RemoveAll(b => b.Room == table.Room && b.Action == name);
            Settings.Buttons.Add(new ActionButton { Room = table.Room, Process = table.Process, Action = name, X = x, Y = y });
            _save();
            _hinted.Clear();
            var key = Settings.Actions[action].Key;
            var mode = Settings.ModeOf(table.Room, table.Process);
            return table.Room + ": кнопка «" + name + "» запомнена"
                + (string.IsNullOrEmpty(key) ? "" : " — " + key + " над столом этого рума нажимает её")
                + (mode == ActionKeysConfig.KeysMode ? " (сейчас у рума — свои клавиши клиента; кнопка нужна для клика)" : "")
                + (Settings.IsExcluded(table.Room) ? " (но в этом руме клавиши выключены в меню)" : "");
        }

        /// <summary>Точка экрана → доли клиентской области стола; false — точка не на столе.</summary>
        public static bool ToFraction(Box client, int x, int y, out double fx, out double fy)
        {
            fx = client.W > 0 ? Math.Round((double)(x - client.X) / client.W, 4) : -1;
            fy = client.H > 0 ? Math.Round((double)(y - client.Y) / client.H, 4) : -1;
            return x >= client.X && x < client.Right && y >= client.Y && y < client.Bottom;
        }

        /// <summary>Доли клиентской области стола → точка экрана.</summary>
        public static POINT ToScreen(Box client, double fx, double fy)
        {
            return new POINT
            {
                X = client.X + Math.Max(0, Math.Min(client.W - 1, (int)Math.Round(fx * client.W))),
                Y = client.Y + Math.Max(0, Math.Min(client.H - 1, (int)Math.Round(fy * client.H))),
            };
        }

        /// <summary>Координаты точки окна для сообщений мыши: x — младшие 16 бит, y — старшие.</summary>
        public static IntPtr MouseLParam(int x, int y)
        {
            return new IntPtr((y << 16) | (x & 0xFFFF));
        }

        /// <summary>Клиентская область окна (без рамки и заголовка) в пикселях экрана.</summary>
        private static bool ClientBox(IntPtr window, out Box box)
        {
            RECT rect;
            var origin = new POINT();
            box = new Box();
            if (!KeyNative.GetClientRect(window, out rect) || !KeyNative.ClientToScreen(window, ref origin)) return false;
            box = new Box(origin.X, origin.Y, rect.Width, rect.Height);
            return box.W > 0 && box.H > 0;
        }

        private void Hook()
        {
            _hook = KeyNative.SetWindowsHookEx(KeyNative.WH_KEYBOARD_LL, _proc, KeyNative.GetModuleHandle(null), 0);
            if (_hook == IntPtr.Zero)
            {
                _log("Клавиши за столами не включились: Windows не дала перехватывать клавиатуру (код "
                    + Marshal.GetLastWin32Error() + ")");
            }
        }

        private void Unhook()
        {
            if (_hook == IntPtr.Zero) return;
            KeyNative.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }

        public void Dispose()
        {
            _focusTimer.Stop();
            _frame.Close();
            Unhook();
        }

        /// <summary>Перехват клавиатуры. Решение — сразу (Windows снимает перехват,
        /// если он думает долго), нажатие — потом, в фоне.</summary>
        private IntPtr OnKey(int code, IntPtr message, IntPtr data)
        {
            if (code >= 0)
            {
                var key = Marshal.ReadInt32(data);
                var msg = message.ToInt32();
                var swallow = false;
                try
                {
                    if (msg == KeyNative.WM_KEYDOWN || msg == KeyNative.WM_SYSKEYDOWN)
                    {
                        swallow = KeyDown(key);
                        if (!swallow) _lastTyped = DateTime.Now;
                    }
                    else if (msg == KeyNative.WM_KEYUP || msg == KeyNative.WM_SYSKEYUP)
                    {
                        swallow = _held.Remove(key);
                    }
                }
                catch (Exception e)
                {
                    _log("Ошибка клавиш за столами: " + e);
                }
                if (swallow) return new IntPtr(1);
            }
            return KeyNative.CallNextHookEx(_hook, code, message, data);
        }

        /// <summary>Нажатие клавиши: true — клавиша наша (курсор над столом, нажатие
        /// для рума задано), дальше она не идёт.</summary>
        private bool KeyDown(int key)
        {
            // Автоповтор зажатой клавиши: нажимает только первое нажатие.
            if (_held.Contains(key)) return true;
            if (_suspended || !Settings.Enabled) return false;
            Binding binding = null;
            foreach (var b in _bindings)
            {
                if (b.Key == key && b.Modifiers == Modifiers())
                {
                    binding = b;
                    break;
                }
            }
            if (binding == null) return false;

            POINT cursor;
            if (!Native.GetCursorPos(out cursor) || Native.LeftButtonDown()) return false;
            var tables = _tables();
            var table = TableAt(tables, cursor);
            // GG — клавиша уходит в клиент как обычно, программа ничего не нажимает.
            if (table == null || Settings.IsExcluded(table.Room) || ActionKeysConfig.IsNoKeys(table.Process)) return false;
            if (Settings.OnlyWhenPokerActive && !PokerActive(tables)
                && !(Settings.FocusUnderMouse == true && !RecentlyTyped() && Activate(table.Handle)))
            {
                return false;
            }

            var action = Settings.Actions[binding.Action];
            var mode = Settings.ModeOf(table.Room, table.Process);
            if (mode == ActionKeysConfig.KeysMode)
            {
                var clientKey = Settings.ClientKeyOf(table.Room, table.Process, action.Name);
                uint modifiers;
                Keys pressed;
                if (clientKey == null || !HotkeyWindow.Parse(clientKey.Keys, out modifiers, out pressed))
                {
                    Hint(table.Room, action.Name, "своя клавиша клиента для «" + action.Name
                        + "» не задана — допишите её в config.json → ClientKeys или выберите в меню другой способ нажатия");
                    return false;
                }
                _held.Add(key);
                // Клавиши клиента идут в активное окно — стол должен быть им.
                if (KeyNative.GetForegroundWindow() != table.Handle && !Activate(table.Handle))
                {
                    Hint(table.Room, "focus", "Windows не дала сделать стол активным — клавиша клиента не отправлена");
                    return true;
                }
                PressKeys(modifiers, (int)pressed);
                return true;
            }

            var button = Settings.ButtonOf(table.Room, table.Process, action.Name);
            if (button == null)
            {
                var record = string.IsNullOrEmpty(action.RecordHotkey) ? "меню → Клавиши за столами" : action.RecordHotkey;
                Hint(table.Room, action.Name, "кнопка «" + action.Name + "» не записана — наведите на неё курсор и нажмите " + record);
                return false;
            }
            Box client;
            if (!ClientBox(table.Handle, out client)) return false;
            _held.Add(key);
            var target = ToScreen(client, button.X, button.Y);
            if (mode == ActionKeysConfig.CursorMode)
            {
                // Кнопку закрыло другое окно — клик ушёл бы в него.
                var top = Desktop.WindowAt(target.X, target.Y);
                if (top == null || top.Handle != table.Handle)
                {
                    _log(table.Room + ": кнопку «" + action.Name + "» закрывает другое окно — клик не сделан");
                    return true;
                }
                CursorClick(target);
            }
            else
            {
                DirectClick(table.Handle, table.Room, target);
            }
            return true;
        }

        private static uint Modifiers()
        {
            uint modifiers = 0;
            if (Down(KeyNative.VK_CONTROL)) modifiers |= Native.MOD_CONTROL;
            if (Down(KeyNative.VK_MENU)) modifiers |= Native.MOD_ALT;
            if (Down(KeyNative.VK_SHIFT)) modifiers |= Native.MOD_SHIFT;
            if (Down(KeyNative.VK_LWIN) || Down(KeyNative.VK_RWIN)) modifiers |= Native.MOD_WIN;
            return modifiers;
        }

        private static bool Down(int key)
        {
            return (Native.GetAsyncKeyState(key) & 0x8000) != 0;
        }

        private static TableWindow TableAt(List<TableWindow> tables, POINT point)
        {
            var window = Desktop.WindowAt(point.X, point.Y);
            return window == null ? null : tables.FirstOrDefault(t => t.Handle == window.Handle);
        }

        /// <summary>Активно окно покерного клиента (стол или лобби), а не мессенджер или браузер.</summary>
        private static bool PokerActive(List<TableWindow> tables)
        {
            uint pid;
            Native.GetWindowThreadProcessId(KeyNative.GetForegroundWindow(), out pid);
            return pid != 0 && tables.Any(t => t.ProcessId == pid);
        }

        private bool RecentlyTyped()
        {
            return DateTime.Now - _lastTyped < TypingPause;
        }

        private void Hint(string room, string topic, string text)
        {
            if (!_hinted.Add(room + "|" + topic)) return;
            _log(room + ": " + text);
            _notify(room + ": " + text);
        }

        /// <summary>Сделать окно активным. Фоновой программе Windows это просто так не
        /// даёт, поэтому — через ввод активного окна (AttachThreadInput).</summary>
        private bool Activate(IntPtr window)
        {
            var foreground = KeyNative.GetForegroundWindow();
            if (foreground == window) return true;
            uint pid;
            var thread = Native.GetWindowThreadProcessId(foreground, out pid);
            var self = KeyNative.GetCurrentThreadId();
            var attached = thread != 0 && thread != self && KeyNative.AttachThreadInput(self, thread, true);
            try
            {
                KeyNative.BringWindowToTop(window);
                KeyNative.SetForegroundWindow(window);
            }
            finally
            {
                if (attached) KeyNative.AttachThreadInput(self, thread, false);
            }
            var ok = KeyNative.GetForegroundWindow() == window;
            if (!ok) FocusDenied(pid);
            return ok;
        }

        /// <summary>Почему стол не стал активным — по разу на программу активного окна.
        /// Частая причина — программа от администратора (Hand2Note, клиент): пока её
        /// окно активно, Windows не показывает клавиши обычной программе и не отдаёт
        /// ей фокус.</summary>
        private void FocusDenied(uint pid)
        {
            var name = pid == 0 ? "" : Desktop.ProcessName(pid);
            if (!_focusDenied.Add(name)) return;
            if (KeyNative.Elevated(pid) && !KeyNative.Elevated((uint)System.Diagnostics.Process.GetCurrentProcess().Id))
            {
                var text = name + " запущен от администратора: пока активно его окно, клавиши за столами не работают"
                    + " и стол не становится активным — запустите расстановщик тоже от администратора";
                _log(text);
                _notify(text);
                return;
            }
            _log("Windows не дала сделать стол активным" + (name.Length > 0 ? " (активно окно " + name + ")" : ""));
        }

        /// <summary>Фокус под мышкой и рамка активного стола (каждые 50 мс).</summary>
        private void OnFocusTick()
        {
            try
            {
                if (!Settings.Enabled)
                {
                    _frame.Off();
                    return;
                }
                var tables = _tables();
                if (Settings.FocusUnderMouse == true) FocusUnderMouse(tables);
                var foreground = KeyNative.GetForegroundWindow();
                var active = tables.FirstOrDefault(t => t.Handle == foreground);
                if (active != null && active.Handle != _lastActive && Settings.EnglishLayout == true) EnglishOn(active);
                _lastActive = active == null ? IntPtr.Zero : active.Handle;
                if (Settings.FocusBorder == true && active != null && !Native.IsIconic(active.Handle))
                {
                    _frame.Around(Box.From(Native.FrameOf(active.Handle)));
                }
                else
                {
                    _frame.Off();
                }
            }
            catch (Exception e)
            {
                _frame.Off();
                if (_hinted.Add("focus-error")) _log("Ошибка фокуса под мышкой: " + e);
            }
        }

        /// <summary>Курсор пришёл на стол — стол становится активным. Только когда
        /// мышь двигается (лежащий на столе курсор фокус не забирает), без зажатых
        /// кнопок, без открытых меню и не сразу после набора текста в другом окне.</summary>
        private void FocusUnderMouse(List<TableWindow> tables)
        {
            POINT cursor;
            if (!Native.GetCursorPos(out cursor)) return;
            var moved = cursor.X != _lastCursor.X || cursor.Y != _lastCursor.Y;
            _lastCursor = cursor;
            if (!moved || Native.LeftButtonDown() || Down(KeyNative.VK_RBUTTON) || Down(KeyNative.VK_MBUTTON)) return;
            var table = TableAt(tables, cursor);
            if (table == null) return;
            var foreground = KeyNative.GetForegroundWindow();
            if (foreground == table.Handle) return;
            if (!tables.Any(t => t.Handle == foreground) && RecentlyTyped()) return;
            if (Busy(foreground)) return;
            Activate(table.Handle);
        }

        /// <summary>Стол стал активным — английская раскладка (просьба окну, как от
        /// переключателя раскладки; нажатий нет). Один раз на активацию: переключили
        /// раскладку сами (написать в чат) — программа с этим не спорит.</summary>
        private void EnglishOn(TableWindow table)
        {
            uint pid;
            var thread = Native.GetWindowThreadProcessId(table.Handle, out pid);
            if (thread == 0 || KeyNative.IsEnglish(KeyNative.GetKeyboardLayout(thread))) return;
            var english = KeyNative.EnglishLayout();
            if (english == IntPtr.Zero)
            {
                if (!_noEnglishLogged) _log("Английской раскладки нет в Windows — раскладку на столах не меняю");
                _noEnglishLogged = true;
                return;
            }
            KeyNative.PostMessage(table.Handle, KeyNative.WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, english);
        }

        /// <summary>У активного окна открыто меню или его двигают — фокус не трогаем.
        /// Своё меню в трее и затемнение выбора — тоже.</summary>
        private static bool Busy(IntPtr foreground)
        {
            uint pid;
            var thread = Native.GetWindowThreadProcessId(foreground, out pid);
            if (pid == (uint)System.Diagnostics.Process.GetCurrentProcess().Id) return true;
            var info = new KeyNative.GUITHREADINFO();
            info.cbSize = Marshal.SizeOf(typeof(KeyNative.GUITHREADINFO));
            return thread != 0 && KeyNative.GetGUIThreadInfo(thread, ref info) && (info.flags & KeyNative.GUI_BUSY) != 0;
        }

        /// <summary>Нажатия — в фоне и по очереди, чтобы клавиатура не ждала пауз.</summary>
        private void Queue(Action press)
        {
            var log = _log;
            ThreadPool.QueueUserWorkItem(delegate
            {
                lock (_pressLock)
                {
                    try
                    {
                        press();
                    }
                    catch (Exception e)
                    {
                        log("Ошибка нажатия: " + e.Message);
                    }
                }
            });
        }

        /// <summary>Клик прямо в окно стола: сообщения мыши самому глубокому окну под
        /// кнопкой. Курсор не двигается, стол не обязан быть активным.</summary>
        private void DirectClick(IntPtr table, string room, POINT target)
        {
            var delay = Settings.ClickDelayMs;
            var log = _log;
            var hinted = _hinted;
            Queue(delegate
            {
                var window = KeyNative.DeepestChild(table, target);
                var at = KeyNative.ToClient(window, target);
                var point = MouseLParam(at.X, at.Y);
                var ok = KeyNative.PostMessage(window, KeyNative.WM_MOUSEMOVE, IntPtr.Zero, point);
                Thread.Sleep(delay);
                ok &= KeyNative.PostMessage(window, KeyNative.WM_LBUTTONDOWN, new IntPtr(KeyNative.MK_LBUTTON), point);
                Thread.Sleep(delay);
                ok &= KeyNative.PostMessage(window, KeyNative.WM_LBUTTONUP, IntPtr.Zero, point);
                Thread.Sleep(delay);
                // Клиент думает, что мышь на кнопке, — вернуть ему настоящее место курсора.
                POINT cursor;
                if (Native.GetCursorPos(out cursor) && KeyNative.WindowFromPoint(cursor) == window)
                {
                    var back = KeyNative.ToClient(window, cursor);
                    KeyNative.PostMessage(window, KeyNative.WM_MOUSEMOVE, IntPtr.Zero, MouseLParam(back.X, back.Y));
                }
                if (!ok && hinted.Add(room + "|post"))
                {
                    log(room + ": Windows не дала нажать кнопку (код " + Marshal.GetLastWin32Error()
                        + "; клиент от администратора — запустите расстановщик тоже от администратора)");
                }
            });
        }

        /// <summary>Клик курсором: курсор на кнопку, нажатие, курсор обратно (если
        /// игрок его за это время не увёл).</summary>
        private void CursorClick(POINT target)
        {
            var delay = Settings.ClickDelayMs;
            var log = _log;
            Queue(delegate
            {
                POINT back;
                Native.GetCursorPos(out back);
                KeyNative.SetCursorPos(target.X, target.Y);
                Thread.Sleep(delay);
                var sent = KeyNative.Mouse(KeyNative.MOUSEEVENTF_LEFTDOWN);
                Thread.Sleep(delay);
                sent &= KeyNative.Mouse(KeyNative.MOUSEEVENTF_LEFTUP);
                Thread.Sleep(delay);
                POINT now;
                if (Native.GetCursorPos(out now) && Math.Abs(now.X - target.X) <= 2 && Math.Abs(now.Y - target.Y) <= 2)
                {
                    KeyNative.SetCursorPos(back.X, back.Y);
                }
                if (!sent) log("Windows не дала нажать кнопку (код " + Marshal.GetLastWin32Error() + ")");
            });
        }

        /// <summary>Своя клавиша клиента: сочетание уходит в активное окно (стол).</summary>
        private void PressKeys(uint modifiers, int key)
        {
            var delay = Settings.ClickDelayMs;
            var held = new List<int>();
            if ((modifiers & Native.MOD_CONTROL) != 0) held.Add(KeyNative.VK_CONTROL);
            if ((modifiers & Native.MOD_ALT) != 0) held.Add(KeyNative.VK_MENU);
            if ((modifiers & Native.MOD_SHIFT) != 0) held.Add(KeyNative.VK_SHIFT);
            if ((modifiers & Native.MOD_WIN) != 0) held.Add(KeyNative.VK_LWIN);
            Queue(delegate
            {
                // Стол мог только что стать активным — дать клиенту это заметить.
                Thread.Sleep(delay);
                foreach (var m in held) KeyNative.Key(m, false);
                KeyNative.Key(key, false);
                Thread.Sleep(delay);
                KeyNative.Key(key, true);
                for (var i = held.Count - 1; i >= 0; i--) KeyNative.Key(held[i], true);
            });
        }
    }

    /// <summary>Рамка вокруг активного стола: сквозь неё проходят клики, фокус она
    /// не забирает.</summary>
    public class FocusFrame : Form
    {
        private const int Thickness = 3;
        private Box _box;

        public FocusFrame()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.Magenta;
            TransparencyKey = Color.Magenta;
            Paint += delegate(object sender, PaintEventArgs e)
            {
                using (var pen = new Pen(Color.FromArgb(150, 80, 255), Thickness * 2))
                {
                    e.Graphics.DrawRectangle(pen, 0, 0, ClientSize.Width, ClientSize.Height);
                }
            };
        }

        /// <summary>Показать рамку по краю стола (видимая рамка окна, физические пиксели).</summary>
        public void Around(Box box)
        {
            if (Visible && box.X == _box.X && box.Y == _box.Y && box.W == _box.W && box.H == _box.H) return;
            _box = box;
            if (!Visible) Show();
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST, box.X, box.Y, box.W, box.H,
                Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
            Invalidate();
        }

        public void Off()
        {
            if (Visible) Hide();
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_TRANSPARENT | Native.WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
                return cp;
            }
        }
    }

    /// <summary>Вызовы Win32 сборки с клавишами: перехват клавиатуры, активный стол,
    /// клик и клавиши клиента.</summary>
    internal static class KeyNative
    {
        public delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);

        [StructLayout(LayoutKind.Sequential)]
        public struct MOUSEINPUT
        {
            public int dx, dy;
            public uint mouseData, dwFlags, time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct KEYBDINPUT
        {
            public ushort wVk, wScan;
            public uint dwFlags, time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct INPUT
        {
            public uint type;
            public InputUnion u;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct GUITHREADINFO
        {
            public int cbSize;
            public uint flags;
            public IntPtr hwndActive, hwndFocus, hwndCapture, hwndMenuOwner, hwndMoveSize, hwndCaret;
            public RECT rcCaret;
        }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr module, uint thread);

        [DllImport("user32.dll")]
        public static extern bool UnhookWindowsHookEx(IntPtr hook);

        [DllImport("user32.dll")]
        public static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr GetModuleHandle(string name);

        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool AttachThreadInput(uint attach, uint attachTo, bool on);

        [DllImport("user32.dll")]
        public static extern bool GetGUIThreadInfo(uint thread, ref GUITHREADINFO info);

        [DllImport("user32.dll")]
        public static extern bool GetClientRect(IntPtr hWnd, out RECT rect);

        [DllImport("user32.dll")]
        public static extern bool ClientToScreen(IntPtr hWnd, ref POINT point);

        [DllImport("user32.dll")]
        public static extern bool ScreenToClient(IntPtr hWnd, ref POINT point);

        [DllImport("user32.dll")]
        public static extern IntPtr ChildWindowFromPointEx(IntPtr hWnd, POINT point, uint flags);

        [DllImport("user32.dll")]
        public static extern IntPtr WindowFromPoint(POINT point);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool PostMessage(IntPtr hWnd, int message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll")]
        private static extern uint MapVirtualKey(uint code, uint mapType);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint count, INPUT[] inputs, int size);

        [DllImport("kernel32.dll")]
        private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("advapi32.dll")]
        private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

        [DllImport("advapi32.dll")]
        private static extern bool GetTokenInformation(IntPtr token, int infoClass, out int info, int length, out int returned);

        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000, TOKEN_QUERY = 0x8;
        private const int TokenElevation = 20;

        [DllImport("user32.dll")]
        public static extern IntPtr GetKeyboardLayout(uint thread);

        [DllImport("user32.dll")]
        private static extern int GetKeyboardLayoutList(int count, [Out] IntPtr[] layouts);

        public const int WM_INPUTLANGCHANGEREQUEST = 0x0050;
        private const int LANG_ENGLISH = 0x09, LANG_EN_US = 0x0409;

        public const int WH_KEYBOARD_LL = 13;
        public const int WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105;
        public const int WM_MOUSEMOVE = 0x0200, WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202, MK_LBUTTON = 0x0001;
        public const int VK_RBUTTON = 0x02, VK_MBUTTON = 0x04;
        public const int VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12, VK_LWIN = 0x5B, VK_RWIN = 0x5C;
        public const uint MOUSEEVENTF_LEFTDOWN = 0x0002, MOUSEEVENTF_LEFTUP = 0x0004;
        private const uint INPUT_MOUSE = 0, INPUT_KEYBOARD = 1, KEYEVENTF_EXTENDEDKEY = 0x0001, KEYEVENTF_KEYUP = 0x0002;
        private const uint CWP_SKIPINVISIBLE = 0x1, CWP_SKIPDISABLED = 0x2, CWP_SKIPTRANSPARENT = 0x4;

        /// <summary>Меню, перемещение или изменение размера окна.</summary>
        public const uint GUI_BUSY = 0x2 | 0x4 | 0x8 | 0x10;

        /// <summary>Самое глубокое видимое дочернее окно под точкой экрана — ему и
        /// идут сообщения мыши (у Qt и Chromium это само окно стола).</summary>
        public static IntPtr DeepestChild(IntPtr window, POINT screen)
        {
            for (var depth = 0; depth < 16; depth++)
            {
                var p = ToClient(window, screen);
                var child = ChildWindowFromPointEx(window, p, CWP_SKIPINVISIBLE | CWP_SKIPDISABLED | CWP_SKIPTRANSPARENT);
                if (child == IntPtr.Zero || child == window) break;
                window = child;
            }
            return window;
        }

        public static POINT ToClient(IntPtr window, POINT screen)
        {
            ScreenToClient(window, ref screen);
            return screen;
        }

        /// <summary>Процесс запущен от администратора; false — нет или не узнать.</summary>
        public static bool Elevated(uint pid)
        {
            var process = pid == 0 ? IntPtr.Zero : OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (process == IntPtr.Zero) return false;
            IntPtr token;
            var ok = OpenProcessToken(process, TOKEN_QUERY, out token);
            CloseHandle(process);
            if (!ok) return false;
            int elevated, size;
            ok = GetTokenInformation(token, TokenElevation, out elevated, sizeof(int), out size) && elevated != 0;
            CloseHandle(token);
            return ok;
        }

        /// <summary>Раскладка английская (США, Великобритания…): язык — младшие 16 бит.</summary>
        public static bool IsEnglish(IntPtr layout)
        {
            return ((int)(layout.ToInt64() & 0xFFFF) & 0x3FF) == LANG_ENGLISH;
        }

        /// <summary>Установленная английская раскладка (сначала США); Zero — нет ни одной.</summary>
        public static IntPtr EnglishLayout()
        {
            var count = GetKeyboardLayoutList(0, null);
            if (count <= 0) return IntPtr.Zero;
            var layouts = new IntPtr[count];
            count = GetKeyboardLayoutList(count, layouts);
            var found = IntPtr.Zero;
            for (var i = 0; i < count; i++)
            {
                if ((layouts[i].ToInt64() & 0xFFFF) == LANG_EN_US) return layouts[i];
                if (found == IntPtr.Zero && IsEnglish(layouts[i])) found = layouts[i];
            }
            return found;
        }

        /// <summary>Кнопка мыши там, где сейчас курсор.</summary>
        public static bool Mouse(uint flags)
        {
            var input = new INPUT { type = INPUT_MOUSE };
            input.u.mi.dwFlags = flags;
            return SendInput(1, new[] { input }, Marshal.SizeOf(typeof(INPUT))) == 1;
        }

        /// <summary>Клавиша в активное окно.</summary>
        public static bool Key(int vk, bool up)
        {
            var input = new INPUT { type = INPUT_KEYBOARD };
            input.u.ki.wVk = (ushort)vk;
            input.u.ki.wScan = (ushort)MapVirtualKey((uint)vk, 0);
            input.u.ki.dwFlags = (up ? KEYEVENTF_KEYUP : 0) | (Extended(vk) ? KEYEVENTF_EXTENDEDKEY : 0);
            return SendInput(1, new[] { input }, Marshal.SizeOf(typeof(INPUT))) == 1;
        }

        /// <summary>Клавиши с «расширенным» скан-кодом: стрелки, Home/End, Insert/Delete…</summary>
        private static bool Extended(int vk)
        {
            return (vk >= 0x21 && vk <= 0x28) || vk == 0x2D || vk == 0x2E || vk == VK_LWIN || vk == VK_RWIN
                || vk == 0x6F || vk == 0x90;
        }
    }
}
#endif
