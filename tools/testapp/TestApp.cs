using System;
using System.Drawing;
using System.Windows.Forms;

internal static class TestApp
{
    private static int _clicks;
    private static Form _form;
    private static Label _label;

    [STAThread]
    private static void Main()
    {
        _form = new Form
        {
            Text = "TestApp clicks=0",
            Width = 420,
            Height = 240,
            StartPosition = FormStartPosition.CenterScreen,
            BackColor = Color.FromArgb(245, 245, 248),
        };

        _label = new Label
        {
            Text = "Кликните по окну",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 14f),
        };

        _form.Controls.Add(_label);
        _form.Click += (sender, args) =>
        {
            _clicks++;
            _form.Text = "TestApp clicks=" + _clicks;
            _label.Text = "Кликов: " + _clicks;
        };

        Application.Run(_form);
    }
}
