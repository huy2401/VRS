using Avalonia.Controls;
using Avalonia.Interactivity;
using VSR.Models;

namespace VSR.Views;

public partial class ConfirmDeleteAccountDialog : Window
{
    public bool IsConfirmed { get; private set; }
    public AccountCredential? Account { get; }

    public ConfirmDeleteAccountDialog()
    {
        InitializeComponent();
    }

    public ConfirmDeleteAccountDialog(AccountCredential account) : this()
    {
        Account = account;
        var displayName = !string.IsNullOrWhiteSpace(account.UnitName) && account.UnitName != account.Username
            ? $"{account.Username} ({account.UnitName})"
            : account.Username;

        var txtAccount = this.FindControl<TextBlock>("TxtAccountName");
        if (txtAccount != null)
        {
            txtAccount.Text = $"Tài khoản: {displayName}";
        }
    }

    private void OnConfirmDeleteClick(object? sender, RoutedEventArgs e)
    {
        IsConfirmed = true;
        Close();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        IsConfirmed = false;
        Close();
    }
}
