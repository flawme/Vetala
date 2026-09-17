using Avalonia.Controls;
using Avalonia.Interactivity;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Vetala.Views;

public partial class ConfirmDialog : Window, INotifyPropertyChanged
{
    private string _title = "";
    public string DialogTitle
    {
        get => _title;
        set { _title = value; OnPropertyChanged(); }
    }

    private string _message = "";
    public string Message
    {
        get => _message;
        set { _message = value; OnPropertyChanged(); }
    }

    public ConfirmDialog()
    {
        InitializeComponent();
        DataContext = this;
        Opened += (s, e) => 
        {
            var btn = this.FindControl<Button>("ConfirmBtn");
            btn?.Focus();
        };
    }

    private void OnConfirmClick(object? sender, RoutedEventArgs e)
    {
        Close(true);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }

    public new event PropertyChangedEventHandler? PropertyChanged;
    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
