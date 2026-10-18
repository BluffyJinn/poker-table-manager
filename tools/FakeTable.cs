using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

/// <summary>Тестовые «столы»: окна с заголовком как у стола, чтобы проверять
/// расстановщик без настоящих клиентов. При появлении фокус не забирают (как
/// стол — по клику) и сами закрываются. Внизу кнопки Fold / Call / Bet, как у
/// клиентов, и свои клавиши стола Ctrl+F1 / F2 / F3 (как у ПокерОК): нажатия
/// видны на столе и пишутся в FakeTable.clicks.txt.
/// FakeTable.exe количество x y ширина высота секунд [заголовок]
/// С заголовком у всех окон он одинаковый (как у QQPK).</summary>
public static class FakeTable
{
    [STAThread]
    public static void Main(string[] args)
    {
        var count = args.Length > 0 ? int.Parse(args[0]) : 3;
        var x = args.Length > 1 ? int.Parse(args[1]) : 100;
        var y = args.Length > 2 ? int.Parse(args[2]) : 100;
        var w = args.Length > 3 ? int.Parse(args[3]) : 800;
        var h = args.Length > 4 ? int.Parse(args[4]) : 600;
        var seconds = args.Length > 5 ? int.Parse(args[5]) : 60;
        var title = args.Length > 6 ? args[6] : null;

        Application.EnableVisualStyles();
        var forms = new Form[count];
        for (var i = 0; i < count; i++)
        {
            forms[i] = new FakeForm(i + 1, new Rectangle(x + i * 40, y + i * 40, w, h), title);
            forms[i].Show();
        }
        var timer = new Timer { Interval = seconds * 1000 };
        timer.Tick += delegate { Application.Exit(); };
        timer.Start();
        Application.Run();
    }

    private class FakeForm : Form
    {
        /// <summary>Кнопки действий: место — доли окна, при изменении размера остаются там же.</summary>
        private static readonly string[] Actions = { "Fold", "Call", "Bet" };

        private readonly Rectangle _bounds;
        private readonly int _number;
        private readonly Label _label;
        private readonly Button[] _buttons = new Button[Actions.Length];
        private int _clicks;

        public FakeForm(int number, Rectangle bounds, string title)
        {
            _bounds = bounds;
            _number = number;
            Text = title ?? "Fake NL Hold'em Table #" + number;
            BackColor = Color.FromArgb(21, 55, 40);
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            _label = new Label
            {
                Text = "Тестовый стол " + number,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 18, FontStyle.Bold),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
            };
            Controls.Add(_label);
            for (var i = 0; i < Actions.Length; i++)
            {
                var action = Actions[i];
                var button = new Button
                {
                    Text = action,
                    ForeColor = Color.White,
                    BackColor = Color.FromArgb(120, 30, 30),
                    FlatStyle = FlatStyle.Flat,
                    Font = new Font("Segoe UI", 12, FontStyle.Bold),
                    TabStop = false,
                };
                button.Click += delegate { Clicked(action); };
                Controls.Add(button);
                button.BringToFront();
                _buttons[i] = button;
            }
            Resize += delegate { PlaceButtons(); };
            PlaceButtons();
            KeyPreview = true;
            KeyDown += delegate(object sender, KeyEventArgs e)
            {
                var index = e.KeyCode - Keys.F1;
                if (!e.Control || index < 0 || index >= Actions.Length) return;
                Clicked(Actions[index] + " (клавиша)");
                e.Handled = true;
            };
        }

        private void PlaceButtons()
        {
            var size = ClientSize;
            for (var i = 0; i < _buttons.Length; i++)
            {
                _buttons[i].SetBounds((int)(size.Width * (0.52 + i * 0.16)), (int)(size.Height * 0.88),
                    (int)(size.Width * 0.14), (int)(size.Height * 0.08));
            }
        }

        private void Clicked(string action)
        {
            _clicks++;
            _label.Text = "Тестовый стол " + _number + "\n" + action + " (нажатий: " + _clicks + ")";
            try
            {
                File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "FakeTable.clicks.txt"),
                    DateTime.Now.ToString("HH:mm:ss.fff") + "  стол " + _number + ": " + action + Environment.NewLine);
            }
            catch (Exception)
            {
            }
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            SetBoundsCore(_bounds.X, _bounds.Y, _bounds.Width, _bounds.Height, BoundsSpecified.All);
        }
    }
}
