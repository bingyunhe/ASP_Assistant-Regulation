// Added for ASP Assistant Arknights on 2026-11-13; see NOTICE.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace ASPAssistant.App.Controls;

public static class TextBoxClear
{
    public static RoutedUICommand ClearCommand { get; } = new("清空文字", "ClearText", typeof(TextBoxClear));

    static TextBoxClear()
    {
        EventManager.RegisterClassHandler(typeof(TextBox), TextBoxBase.TextChangedEvent,
            new TextChangedEventHandler((_, _) => CommandManager.InvalidateRequerySuggested()));
        CommandManager.RegisterClassCommandBinding(typeof(TextBox), new CommandBinding(ClearCommand,
            (sender, e) =>
            {
                var input = (TextBox)sender;
                input.Clear();
                input.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
                input.Focus();
                e.Handled = true;
            },
            (sender, e) =>
            {
                var input = (TextBox)sender;
                e.CanExecute = !input.IsReadOnly && input.IsEnabled && input.Text.Length > 0;
                e.Handled = true;
            }));
    }
}
