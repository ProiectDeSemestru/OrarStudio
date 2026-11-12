using System.Windows;
using OrarStudio.UI;

namespace OrarStudio.Views;

public enum MessageKind { Info, Question, Danger, Success }

/// <summary>Fereastră de mesaj / confirmare în stilul aplicației (înlocuiește MessageBox).</summary>
public partial class MessageWindow : Window
{
    public MessageWindow(string title, string message, string ok, string? cancel, MessageKind kind)
    {
        InitializeComponent();
        TitleText.Text = title;
        MessageText.Text = message;
        OkButton.Content = ok;
        if (cancel == null) CancelButton.Visibility = Visibility.Collapsed;
        else CancelButton.Content = cancel;

        switch (kind)
        {
            case MessageKind.Danger:
                IconText.Text = "";
                IconBg.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "Brush.DangerSoft");
                IconText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "Brush.Danger");
                if (cancel != null)
                {
                    OkButton.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "Brush.Danger");
                    OkButton.SetResourceReference(System.Windows.Controls.Control.BorderBrushProperty, "Brush.Danger");
                    OkButton.SetResourceReference(Ui.HoverBackgroundProperty, "Brush.Danger");
                }
                break;
            case MessageKind.Question:
                IconText.Text = "";
                break;
            case MessageKind.Success:
                IconText.Text = "";
                IconBg.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "Brush.SuccessSoft");
                IconText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "Brush.Success");
                break;
        }
        Dialogs.EnableDrag(this, Header);
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
