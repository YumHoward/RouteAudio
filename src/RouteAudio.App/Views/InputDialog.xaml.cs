using System.Windows;

namespace RouteAudio.App.Views;

public partial class InputDialog : Window
{
    private readonly bool _allowEmpty;

    public InputDialog(string title, string label, string initialValue = "", bool allowEmpty = false)
    {
        InitializeComponent();
        _allowEmpty = allowEmpty;
        Title = title;
        LabelText.Text = label;
        ValueBox.Text = initialValue;
        Loaded += (_, _) =>
        {
            ValueBox.Focus();
            ValueBox.SelectAll();
        };
    }

    public string Value => ValueBox.Text.Trim();

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (!_allowEmpty && string.IsNullOrWhiteSpace(ValueBox.Text)) return;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
