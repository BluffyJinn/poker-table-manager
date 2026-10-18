using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace TableManager
{
    public static class Program
    {
        [STAThread]
        public static void Main()
        {
            // Координаты всех мониторов — в физических пикселях, с учётом масштаба
            // каждого (иначе на мониторе 150% столы выходят в полтора раза больше).
            Native.SetProcessDpiAwarenessContext(Native.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);

            // Один экземпляр на папку: у копий в разных папках свои настройки.
            var folder = AppDomain.CurrentDomain.BaseDirectory.ToLowerInvariant();
            var mutexName = "TableManager." + folder.GetHashCode().ToString("x8");
            bool created;
            using (var mutex = new Mutex(true, mutexName, out created))
            {
                if (!created)
                {
                    MessageBox.Show("Расстановщик уже запущен — значок в трее.", "Расстановщик столов");
                    return;
                }
#if ACTION_KEYS
                // Обычный расстановщик и сборка с клавишами двигали бы одни и те же столы.
                if (Process.GetProcessesByName("TableManager").Length > 0)
                {
                    MessageBox.Show("Запущен обычный расстановщик — закройте его (значок в трее → Выход) "
                        + "и запустите эту сборку снова.", "Расстановщик столов");
                    return;
                }
#endif
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayApp());
                GC.KeepAlive(mutex);
            }
        }
    }

    /// <summary>Значок в трее, меню, свои горячие клавиши менеджера и таймер.</summary>
    public class TrayApp : ApplicationContext
    {
        private readonly string _configPath = Config.PathNextToExe("config.json");
        private readonly string _logPath = Config.PathNextToExe("TableManager.log");
        private readonly NotifyIcon _icon;
        private readonly System.Windows.Forms.Timer _timer;
        private readonly HotkeyWindow _hotkeys;
        private readonly ToolStripMenuItem _pauseItem;
        private readonly ToolStripMenuItem _relayoutItem;
        private readonly ToolStripMenuItem _slotsItem;
        private readonly ToolStripMenuItem _pickItem;
        private readonly ToolStripMenuItem _notTableItem;
        private Manager _manager;
        private Config _config;
#if ACTION_KEYS
        private readonly ActionKeys _actionKeys;
#endif

        public TrayApp()
        {
            _config = LoadConfig();
            _manager = new Manager(_config);
            _manager.Log += WriteLog;
#if ACTION_KEYS
            _actionKeys = new ActionKeys(_config, () => _manager.Tables, Notify, WriteLog,
                delegate { Config.Save(_configPath, _config); });
#endif

            _relayoutItem = new ToolStripMenuItem("Расставить все столы", null, delegate { Relayout(); });
            _slotsItem = new ToolStripMenuItem("Показать ячейки", null, delegate { ShowSlots(); });
            _pauseItem = new ToolStripMenuItem("Пауза", null, delegate { TogglePause(); });
            _pickItem = new ToolStripMenuItem("Указать стол…", null,
                delegate { PickOverlay.Run("Кликните по столу, который не расставляется", p => MarkAt(p, true)); });
            _notTableItem = new ToolStripMenuItem("Это не стол…", null,
                delegate { PickOverlay.Run("Кликните по окну, которое не стол", p => MarkAt(p, false)); });
            var menu = new ContextMenuStrip();
            menu.Items.Add(_relayoutItem);
            menu.Items.Add(_slotsItem);
            menu.Items.Add(_pauseItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(_pickItem);
            menu.Items.Add(_notTableItem);
#if ACTION_KEYS
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(_actionKeys.Menu);
#endif
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Настройки (config.json)", null, delegate { Open(_configPath); }));
            menu.Items.Add(new ToolStripMenuItem("Перечитать настройки", null, delegate { Reload(); }));
            menu.Items.Add(new ToolStripMenuItem("Список окон (чтобы добавить рум)", null, delegate { ShowWindowList(); }));
            menu.Items.Add(new ToolStripMenuItem("Журнал", null, delegate { Open(_logPath); }));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Выход", null, delegate { ExitThread(); }));

            _icon = new NotifyIcon
            {
                Icon = MakeIcon(),
                Text = "Расстановщик столов",
                ContextMenuStrip = menu,
                Visible = true,
            };
            _icon.DoubleClick += delegate { ShowSlots(); };

            _hotkeys = new HotkeyWindow();
            _hotkeys.Pressed += OnHotkey;
            RegisterHotkeys();

            _timer = new System.Windows.Forms.Timer { Interval = _config.PollMs };
            _timer.Tick += delegate { SafeTick(); };
            _timer.Start();
            WriteLog("Запуск. Настройки: " + _configPath);
#if ACTION_KEYS
            WriteLog("Клавиши за столами: " + _actionKeys.Describe());
#endif
        }

        private Config LoadConfig()
        {
            try
            {
                return Config.Load(_configPath);
            }
            catch (Exception e)
            {
                WriteLog("Ошибка в config.json: " + e.Message + " — беру настройки по умолчанию");
                MessageBox.Show("Ошибка в config.json:\n" + e.Message + "\n\nРаботаю с настройками по умолчанию.",
                    "Расстановщик столов");
                return Config.Default();
            }
        }

        private void SafeTick()
        {
            try
            {
                _manager.Tick();
                var text = string.Format("Столов: {0}, сетка {1}{2}", _manager.TableCount, _manager.LevelName,
                    _manager.Paused ? " (пауза)" : "");
                if (text.Length > 63) text = text.Substring(0, 63);
                if (_icon.Text != text) _icon.Text = text;
            }
            catch (Exception e)
            {
                WriteLog("Ошибка: " + e);
            }
        }

        private void Relayout()
        {
            try
            {
                _manager.RelayoutAll();
            }
            catch (Exception e)
            {
                WriteLog("Ошибка расстановки: " + e);
            }
        }

        private void TogglePause()
        {
            _manager.Paused = !_manager.Paused;
            _pauseItem.Checked = _manager.Paused;
            WriteLog(_manager.Paused ? "Пауза: новые столы не расставляются" : "Пауза снята");
        }

        private void Reload()
        {
            _config = LoadConfig();
            _manager.Apply(_config);
            _timer.Interval = _config.PollMs;
#if ACTION_KEYS
            _actionKeys.Apply(_config);
#endif
            RegisterHotkeys();
            WriteLog("Настройки перечитаны");
        }

        private void RegisterHotkeys()
        {
            _hotkeys.UnregisterAll();
            Register(1, _config.RelayoutHotkey, _relayoutItem, "Расставить все столы");
            Register(2, _config.ShowSlotsHotkey, _slotsItem, "Показать ячейки");
            Register(3, _config.PickTableHotkey, _pickItem, "Указать стол…");
            Register(4, _config.NotTableHotkey, _notTableItem, "Это не стол…");
#if ACTION_KEYS
            _actionKeys.RegisterHotkeys(Register);
#endif
        }

        private void Register(int id, string hotkey, ToolStripMenuItem item, string label)
        {
            item.Text = label;
            if (string.IsNullOrEmpty(hotkey)) return;
            string error;
            if (_hotkeys.Register(id, hotkey, out error))
            {
                item.Text = label + " (" + hotkey + ")";
            }
            else
            {
                WriteLog("Клавиша " + hotkey + " не назначена: " + error);
            }
        }

        private void OnHotkey(int id)
        {
            if (id == 1) Relayout();
            if (id == 2) ShowSlots();
            // Клавишей — окно под курсором, без клика по столу.
            if (id == 3 || id == 4)
            {
                POINT p;
                if (Native.GetCursorPos(out p)) MarkAt(p, id == 3);
            }
#if ACTION_KEYS
            if (id >= ActionKeys.ToggleId) _actionKeys.OnHotkey(id);
#endif
        }

        /// <summary>«Указать стол» / «Это не стол» для окна в точке экрана.</summary>
        private void MarkAt(POINT point, bool isTable)
        {
            try
            {
                var window = Desktop.WindowAt(point.X, point.Y);
                if (window == null)
                {
                    Notify("Под курсором нет окна программы");
                    return;
                }
                bool changed;
                var message = isTable ? _manager.MarkTable(window, out changed) : _manager.MarkNotTable(window, out changed);
                if (changed) Config.Save(_configPath, _config);
                WriteLog(message);
                Notify(message);
            }
            catch (Exception e)
            {
                WriteLog("Ошибка: " + e);
            }
        }

        private void Notify(string text)
        {
            _icon.ShowBalloonTip(5000, "Расстановщик столов", text, ToolTipIcon.Info);
        }

        private void ShowSlots()
        {
            var slots = _manager.CurrentSlots();
            for (var i = 0; i < slots.Count; i++)
            {
                new SlotOverlay(slots[i], i + 1).Show();
            }
        }

        private void ShowWindowList()
        {
            var path = Config.PathNextToExe("windows.txt");
            var lines = new List<string>
            {
                "Видимые окна: процесс | класс | рамка | заголовок. Отмеченные «<= СТОЛ» уже распознаются.",
                "Чтобы добавить рум: в config.json → Rooms укажите Process (имя процесса) и Table",
                "(часть заголовка стола, регулярное выражение), лобби — в Exclude.",
                "",
            };
            lines.AddRange(Desktop.DescribeWindows(CompiledRule.From(_config.Rooms)));
            File.WriteAllLines(path, lines.ToArray(), new UTF8Encoding(true));
            Open(path);
        }

        private static void Open(string path)
        {
            try
            {
                if (!File.Exists(path)) File.WriteAllText(path, "");
                Process.Start("notepad.exe", "\"" + path + "\"");
            }
            catch (Exception)
            {
            }
        }

        private void WriteLog(string text)
        {
            try
            {
                var info = new FileInfo(_logPath);
                if (info.Exists && info.Length > 1024 * 1024) info.Delete();
                File.AppendAllText(_logPath, DateTime.Now.ToString("dd.MM HH:mm:ss") + "  " + text + Environment.NewLine,
                    new UTF8Encoding(false));
            }
            catch (Exception)
            {
            }
        }

        protected override void ExitThreadCore()
        {
            _timer.Stop();
#if ACTION_KEYS
            _actionKeys.Dispose();
#endif
            _hotkeys.UnregisterAll();
            _hotkeys.DestroyHandle();
            _icon.Visible = false;
            _icon.Dispose();
            WriteLog("Выход");
            base.ExitThreadCore();
        }

        /// <summary>Значок: сетка 2×2 из зелёных столов.</summary>
        private static Icon MakeIcon()
        {
            var bmp = new Bitmap(32, 32);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using (var felt = new SolidBrush(Color.FromArgb(46, 140, 87)))
                using (var rim = new Pen(Color.FromArgb(212, 175, 55), 1.6f))
                {
                    foreach (var p in new[] { new Point(1, 3), new Point(17, 3), new Point(1, 18), new Point(17, 18) })
                    {
                        g.FillEllipse(felt, p.X, p.Y, 14, 11);
                        g.DrawEllipse(rim, p.X, p.Y, 14, 11);
                    }
                }
            }
            return Icon.FromHandle(bmp.GetHicon());
        }
    }

    /// <summary>Невидимое окно для своих глобальных клавиш менеджера.</summary>
    public class HotkeyWindow : NativeWindow
    {
        private readonly List<int> _ids = new List<int>();

        public event Action<int> Pressed;

        public HotkeyWindow()
        {
            CreateHandle(new CreateParams());
        }

        public bool Register(int id, string hotkey, out string error)
        {
            uint modifiers;
            Keys key;
            if (!Parse(hotkey, out modifiers, out key))
            {
                error = "не понял сочетание";
                return false;
            }
            if (!Native.RegisterHotKey(Handle, id, modifiers | Native.MOD_NOREPEAT, (uint)key))
            {
                error = "занято другой программой";
                return false;
            }
            _ids.Add(id);
            error = null;
            return true;
        }

        public void UnregisterAll()
        {
            foreach (var id in _ids) Native.UnregisterHotKey(Handle, id);
            _ids.Clear();
        }

        /// <summary>«Ctrl+Alt+L», «Shift+F9»…</summary>
        public static bool Parse(string text, out uint modifiers, out Keys key)
        {
            modifiers = 0;
            key = Keys.None;
            foreach (var raw in text.Split('+'))
            {
                var part = raw.Trim();
                switch (part.ToLowerInvariant())
                {
                    case "ctrl":
                    case "control":
                        modifiers |= Native.MOD_CONTROL;
                        break;
                    case "alt":
                        modifiers |= Native.MOD_ALT;
                        break;
                    case "shift":
                        modifiers |= Native.MOD_SHIFT;
                        break;
                    case "win":
                        modifiers |= Native.MOD_WIN;
                        break;
                    default:
                        try
                        {
                            key = (Keys)Enum.Parse(typeof(Keys), part, true);
                        }
                        catch (ArgumentException)
                        {
                            return false;
                        }
                        break;
                }
            }
            return key != Keys.None;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_HOTKEY)
            {
                var handler = Pressed;
                if (handler != null) handler(m.WParam.ToInt32());
            }
            base.WndProc(ref m);
        }
    }

    /// <summary>Выбор окна кликом: экран слегка затемняется, клик ловит это
    /// затемнение (до стола он не доходит), Esc или 15 секунд — отмена.</summary>
    public class PickOverlay : Form
    {
        private readonly Box _box;
        private readonly List<PickOverlay> _group;
        private readonly Action<POINT> _picked;
        private static List<PickOverlay> _active;

        public static void Run(string hint, Action<POINT> picked)
        {
            if (_active != null) Close(_active);
            var group = new List<PickOverlay>();
            _active = group;
            foreach (var monitor in Desktop.Monitors())
            {
                group.Add(new PickOverlay(monitor.Bounds, hint, group, picked));
            }
            var timer = new System.Windows.Forms.Timer { Interval = 15000 };
            timer.Tick += delegate
            {
                timer.Stop();
                timer.Dispose();
                Close(group);
            };
            timer.Start();
            foreach (var overlay in group) overlay.Show();
            if (group.Count > 0) group[0].Activate();
        }

        private static void Close(List<PickOverlay> group)
        {
            foreach (var overlay in group.ToArray())
            {
                if (!overlay.IsDisposed) overlay.Close();
            }
            group.Clear();
            if (_active == group) _active = null;
        }

        private PickOverlay(Box box, string hint, List<PickOverlay> group, Action<POINT> picked)
        {
            _box = box;
            _group = group;
            _picked = picked;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.Black;
            Opacity = 0.35;
            Cursor = Cursors.Cross;
            KeyPreview = true;
            var label = new Label
            {
                Text = hint + "\nEsc — отмена",
                ForeColor = Color.Gold,
                Font = new Font("Segoe UI", 28, FontStyle.Bold),
                Dock = DockStyle.Top,
                Height = 160,
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Cross,
            };
            Controls.Add(label);
            MouseDown += OnPick;
            label.MouseDown += OnPick;
            KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape) Close(_group);
            };
        }

        private void OnPick(object sender, MouseEventArgs e)
        {
            POINT p;
            Native.GetCursorPos(out p);
            // Сначала убрать затемнение, потом искать окно под точкой.
            Close(_group);
            _picked(p);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            // Физические пиксели: процесс понимает масштаб каждого монитора.
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST, _box.X, _box.Y, _box.W, _box.H, Native.SWP_SHOWWINDOW);
        }
    }

    /// <summary>Полупрозрачная рамка ячейки на 3 секунды. Сквозь неё проходят
    /// клики, фокус она не забирает.</summary>
    public class SlotOverlay : Form
    {
        private readonly Box _box;

        public SlotOverlay(Box box, int number)
        {
            _box = box;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(30, 90, 60);
            Opacity = 0.45;
            var label = new Label
            {
                Text = number + "\n" + box.W + "×" + box.H,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 20, FontStyle.Bold),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
            };
            Controls.Add(label);
            Paint += delegate(object sender, PaintEventArgs e)
            {
                using (var pen = new Pen(Color.Gold, 6)) e.Graphics.DrawRectangle(pen, 3, 3, ClientSize.Width - 6, ClientSize.Height - 6);
            };
            var timer = new System.Windows.Forms.Timer { Interval = 3000 };
            timer.Tick += delegate
            {
                timer.Stop();
                timer.Dispose();
                Close();
            };
            timer.Start();
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

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            // Физические пиксели: процесс понимает масштаб каждого монитора.
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST, _box.X, _box.Y, _box.W, _box.H,
                Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
        }
    }
}
