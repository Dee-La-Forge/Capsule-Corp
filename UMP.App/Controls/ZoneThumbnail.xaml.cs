using System.IO;
using System.Windows;
using System.Windows.Input;
using UMP.Core.Models;
using Color = System.Windows.Media.Color;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace UMP.App.Controls;

public partial class ZoneThumbnail : System.Windows.Controls.UserControl
{
    public event Action<ZoneThumbnail>? Selected;
    private Zone? _zone;

    public ZoneThumbnail() { InitializeComponent(); }

    public void Initialize(Zone zone)
    {
        _zone = zone;
        TxtName.Text = zone.Name;
        TxtScreen.Text = FormatScreenLabel(zone);
    }

    // REGLE : pas de UpdateLayout, pas de InvalidateVisual, pas de File.Exists
    public void Refresh()
    {
        if (_zone is null) return;
        TxtName.Text = _zone.Name;
        TxtScreen.Text = FormatScreenLabel(_zone);
        var hasVideo = !string.IsNullOrEmpty(_zone.MediaFilePath);
        VideoIndicator.Visibility = hasVideo
            ? Visibility.Visible : Visibility.Collapsed;
        if (hasVideo)
            TxtVideoStatus.Text = $"> {Path.GetFileName(_zone.MediaFilePath)}";
        else
            TxtVideoStatus.Text = string.Empty;
    }

    public void SetSelected(bool selected)
    {
        ThumbBorder.BorderBrush = selected
            ? new SolidColorBrush(Color.FromRgb(92, 79, 191))
            : new SolidColorBrush(Color.FromRgb(42, 42, 64));
        ThumbBorder.BorderThickness = new System.Windows.Thickness(selected ? 2 : 1.5);
    }

    private static string FormatScreenLabel(Zone zone)
    {
        if (!string.IsNullOrEmpty(zone.ScreenDeviceName))
            return $"Ecran {zone.ScreenIndex + 1} ({zone.ScreenDeviceName})";
        return $"Ecran {zone.ScreenIndex + 1}";
    }

    private void OnClick(object sender, MouseButtonEventArgs e)
        => Selected?.Invoke(this);
}
