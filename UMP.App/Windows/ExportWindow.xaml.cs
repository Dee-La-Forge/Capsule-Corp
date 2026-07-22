using System.Windows;

namespace UMP.App.Windows;

public partial class ExportWindow : Window
{
    public ExportWindow()
    {
        InitializeComponent();
    }

    public void SetProgress(double percent, string status, string detail = "")
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            var maxWidth = ProgressFill.Parent is FrameworkElement p ? p.ActualWidth : 400;
            ProgressFill.Width = Math.Max(0, maxWidth * percent / 100.0);
            TxtStatus.Text = status;
            if (!string.IsNullOrEmpty(detail)) TxtDetail.Text = detail;
        }));
    }

    public void Finish(bool success, string message)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            var maxWidth = ProgressFill.Parent is FrameworkElement p ? p.ActualWidth : 400;
            ProgressFill.Width = maxWidth;
            ProgressFill.Background = success
                ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(80, 200, 120))
                : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 80, 80));
            TxtTitle.Text = success ? "Export termine !" : "Erreur d'export";
            TxtTitle.Foreground = success
                ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(80, 200, 120))
                : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 80, 80));
            TxtStatus.Text = message;
            LoaderImg.Visibility = Visibility.Collapsed;
            BtnClose.Visibility = Visibility.Visible;
            Title = success ? "Export termine" : "Erreur";
        }));
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
}
