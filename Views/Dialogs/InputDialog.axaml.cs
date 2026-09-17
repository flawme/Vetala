using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Vetala.Views;

public partial class InputDialog : Window, INotifyPropertyChanged
{
    private string _title = "";
    public string DialogTitle
    {
        get => _title;
        set { _title = value; OnPropertyChanged(); }
    }

    private string _prompt = "";
    public string Prompt
    {
        get => _prompt;
        set { _prompt = value; OnPropertyChanged(); }
    }

    private string _inputText = "";
    public string InputText
    {
        get => _inputText;
        set { _inputText = value; OnPropertyChanged(); }
    }

    public InputDialog()
    {
        InitializeComponent();
        DataContext = this;
        Opened += (s, e) => InputTextBox.Focus();
    }

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        Close(InputText);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }


    public new event PropertyChangedEventHandler? PropertyChanged;
    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
