using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using Point = System.Windows.Point;
using Color = System.Windows.Media.Color;

namespace UMP.App.Controls;

public partial class ColorPickerPanel : System.Windows.Controls.UserControl
{
    private double _hue;
    private double _sat = 1;
    private double _val = 1;
    private byte _alpha = 255;
    private bool _draggingSv, _draggingHue, _draggingAlpha;
    private bool _updatingHex;
    private bool _initialized;

    public event Action<string>? ColorSelected;
    public event Action? CloseRequested;

    public ColorPickerPanel()
    {
        InitializeComponent();
        Loaded += (s, e) => { _initialized = true; BuildHueBar(); UpdateAll(); };
        SvCanvas.SizeChanged += (s, e) => { if (_initialized) UpdateSvGradient(); };
        HueCanvas.SizeChanged += (s, e) => { if (_initialized) BuildHueBar(); };
        AlphaCanvas.SizeChanged += (s, e) => { if (_initialized) UpdateAlphaGradient(); };
        IsVisibleChanged += (s, e) => { if (_initialized && IsVisible) { BuildHueBar(); UpdateAll(); } };
    }

    public void SetColor(string hex)
    {
        _updatingHex = true;
        try
        {
            var h = hex.TrimStart('#');
            byte r = 0, g = 0, b = 0;
            if (h.Length == 8)
            {
                _alpha = Convert.ToByte(h.Substring(0, 2), 16);
                r = Convert.ToByte(h.Substring(2, 2), 16);
                g = Convert.ToByte(h.Substring(4, 2), 16);
                b = Convert.ToByte(h.Substring(6, 2), 16);
            }
            else if (h.Length == 6)
            {
                _alpha = 255;
                r = Convert.ToByte(h.Substring(0, 2), 16);
                g = Convert.ToByte(h.Substring(2, 2), 16);
                b = Convert.ToByte(h.Substring(4, 2), 16);
            }
            RgbToHsv(r, g, b, out _hue, out _sat, out _val);
        }
        catch { _hue = 0; _sat = 1; _val = 1; _alpha = 255; }
        UpdateAll();
        _updatingHex = false;
    }

    private void UpdateAll()
    {
        if (!_initialized) return;
        UpdateSvGradient();
        UpdateSvCursor();
        UpdateHueCursor();
        UpdateAlphaGradient();
        UpdateAlphaCursor();
        UpdatePreview();
        if (!_updatingHex)
        {
            _updatingHex = true;
            TxtHex.Text = GetHexString();
            _updatingHex = false;
        }
        TxtAlpha.Text = _alpha.ToString();
    }

    private string GetHexString()
    {
        HsvToRgb(_hue, _sat, _val, out var r, out var g, out var b);
        return _alpha < 255
            ? $"#{_alpha:X2}{r:X2}{g:X2}{b:X2}"
            : $"#{r:X2}{g:X2}{b:X2}";
    }

    // ===== SV SQUARE =====

    private void UpdateSvGradient()
    {
        var w = SvCanvas.ActualWidth;
        var h = SvCanvas.ActualHeight;
        if (w <= 0 || h <= 0) return;

        HsvToRgb(_hue, 1, 1, out var hr, out var hg, out var hb);
        SvCanvas.Background = new SolidColorBrush(Color.FromRgb(hr, hg, hb));

        SvWhite.Width = w; SvWhite.Height = h;
        SvWhite.Fill = new LinearGradientBrush(
            Colors.White, Color.FromArgb(0, 255, 255, 255),
            new Point(0, 0), new Point(1, 0));

        SvBlack.Width = w; SvBlack.Height = h;
        SvBlack.Fill = new LinearGradientBrush(
            Color.FromArgb(0, 0, 0, 0), Colors.Black,
            new Point(0, 0), new Point(0, 1));
    }

    private void UpdateSvCursor()
    {
        var w = SvCanvas.ActualWidth;
        var h = SvCanvas.ActualHeight;
        if (w <= 0 || h <= 0) return;
        Canvas.SetLeft(SvCursor, _sat * w - 7);
        Canvas.SetTop(SvCursor, (1 - _val) * h - 7);
    }

    private void SvCanvas_MouseDown(object s, MouseButtonEventArgs e)
    { _draggingSv = true; SvCanvas.CaptureMouse(); UpdateSvFromMouse(e.GetPosition(SvCanvas)); }
    private void SvCanvas_MouseMove(object s, MouseEventArgs e)
    { if (_draggingSv) UpdateSvFromMouse(e.GetPosition(SvCanvas)); }
    private void SvCanvas_MouseUp(object s, MouseButtonEventArgs e)
    { _draggingSv = false; SvCanvas.ReleaseMouseCapture(); }

    private void UpdateSvFromMouse(Point p)
    {
        var w = SvCanvas.ActualWidth;
        var h = SvCanvas.ActualHeight;
        if (w <= 0 || h <= 0) return;
        _sat = Math.Clamp(p.X / w, 0, 1);
        _val = Math.Clamp(1 - p.Y / h, 0, 1);
        UpdateAll();
    }

    // ===== HUE BAR =====

    private void BuildHueBar()
    {
        var h = HueCanvas.ActualHeight;
        if (h <= 0) return;
        var ih = (int)h;
        var bmp = new WriteableBitmap(1, ih, 96, 96, PixelFormats.Bgra32, null);
        var px = new byte[ih * 4];
        for (int y = 0; y < ih; y++)
        {
            HsvToRgb((double)y / ih * 360.0, 1, 1, out var r, out var g, out var b);
            px[y * 4] = b; px[y * 4 + 1] = g; px[y * 4 + 2] = r; px[y * 4 + 3] = 255;
        }
        bmp.WritePixels(new Int32Rect(0, 0, 1, ih), px, 4, 0);
        HueRect.Height = h;
        HueRect.Fill = new ImageBrush(bmp) { Stretch = Stretch.Fill };
        UpdateHueCursor();
    }

    private void UpdateHueCursor()
    {
        var h = HueCanvas.ActualHeight;
        if (h <= 0) return;
        Canvas.SetTop(HueCursor, _hue / 360.0 * h - 3);
    }

    private void HueCanvas_MouseDown(object s, MouseButtonEventArgs e)
    { _draggingHue = true; HueCanvas.CaptureMouse(); UpdateHueFromMouse(e.GetPosition(HueCanvas)); }
    private void HueCanvas_MouseMove(object s, MouseEventArgs e)
    { if (_draggingHue) UpdateHueFromMouse(e.GetPosition(HueCanvas)); }
    private void HueCanvas_MouseUp(object s, MouseButtonEventArgs e)
    { _draggingHue = false; HueCanvas.ReleaseMouseCapture(); }

    private void UpdateHueFromMouse(Point p)
    {
        var h = HueCanvas.ActualHeight;
        if (h <= 0) return;
        _hue = Math.Clamp(p.Y / h * 360.0, 0, 359.99);
        UpdateAll();
    }

    // ===== ALPHA =====

    private void UpdateAlphaGradient()
    {
        var w = AlphaCanvas.ActualWidth;
        if (w <= 0) return;
        HsvToRgb(_hue, _sat, _val, out var r, out var g, out var b);
        AlphaGradient.Width = w;
        AlphaGradient.Fill = new LinearGradientBrush(
            Color.FromArgb(0, r, g, b),
            Color.FromArgb(255, r, g, b),
            new Point(0, 0.5), new Point(1, 0.5));
        AlphaTrackBg.Width = w;
    }

    private void UpdateAlphaCursor()
    {
        var w = AlphaCanvas.ActualWidth;
        if (w <= 0) return;
        Canvas.SetLeft(AlphaCursor, _alpha / 255.0 * w - 8);
    }

    private void AlphaCanvas_MouseDown(object s, MouseButtonEventArgs e)
    { _draggingAlpha = true; AlphaCanvas.CaptureMouse(); UpdateAlphaFromMouse(e.GetPosition(AlphaCanvas)); }
    private void AlphaCanvas_MouseMove(object s, MouseEventArgs e)
    { if (_draggingAlpha) UpdateAlphaFromMouse(e.GetPosition(AlphaCanvas)); }
    private void AlphaCanvas_MouseUp(object s, MouseButtonEventArgs e)
    { _draggingAlpha = false; AlphaCanvas.ReleaseMouseCapture(); }

    private void UpdateAlphaFromMouse(Point p)
    {
        var w = AlphaCanvas.ActualWidth;
        if (w <= 0) return;
        _alpha = (byte)Math.Clamp(p.X / w * 255.0, 0, 255);
        UpdateAll();
    }

    // ===== PREVIEW + HEX =====

    private void UpdatePreview()
    {
        HsvToRgb(_hue, _sat, _val, out var r, out var g, out var b);
        if (ColorPreview is not null)
            ColorPreview.Background = new SolidColorBrush(Color.FromArgb(_alpha, r, g, b));
    }

    private void TxtHex_Changed(object s, TextChangedEventArgs e)
    {
        if (_updatingHex) return;
        _updatingHex = true;
        try
        {
            var h = TxtHex.Text.TrimStart('#');
            byte r, g, b;
            if (h.Length == 8)
            {
                _alpha = Convert.ToByte(h.Substring(0, 2), 16);
                r = Convert.ToByte(h.Substring(2, 2), 16);
                g = Convert.ToByte(h.Substring(4, 2), 16);
                b = Convert.ToByte(h.Substring(6, 2), 16);
                RgbToHsv(r, g, b, out _hue, out _sat, out _val);
                UpdateAll();
            }
            else if (h.Length == 6)
            {
                r = Convert.ToByte(h.Substring(0, 2), 16);
                g = Convert.ToByte(h.Substring(2, 2), 16);
                b = Convert.ToByte(h.Substring(4, 2), 16);
                RgbToHsv(r, g, b, out _hue, out _sat, out _val);
                UpdateAll();
            }
        }
        catch { }
        _updatingHex = false;
    }

    // ===== ACTIONS =====

    private void BtnValidate_Click(object s, RoutedEventArgs e)
        => ColorSelected?.Invoke(GetHexString());

    private void BtnClose_Click(object s, RoutedEventArgs e)
        => CloseRequested?.Invoke();

    private void Backdrop_Click(object s, System.Windows.Input.MouseButtonEventArgs e)
        => CloseRequested?.Invoke();

    // ===== HSV <-> RGB =====

    private static void HsvToRgb(double h, double s, double v,
        out byte r, out byte g, out byte b)
    {
        double c = v * s, x = c * (1 - Math.Abs(h / 60.0 % 2 - 1)), m = v - c;
        double r1, g1, b1;
        if (h < 60) { r1 = c; g1 = x; b1 = 0; }
        else if (h < 120) { r1 = x; g1 = c; b1 = 0; }
        else if (h < 180) { r1 = 0; g1 = c; b1 = x; }
        else if (h < 240) { r1 = 0; g1 = x; b1 = c; }
        else if (h < 300) { r1 = x; g1 = 0; b1 = c; }
        else { r1 = c; g1 = 0; b1 = x; }
        r = (byte)Math.Clamp((r1 + m) * 255, 0, 255);
        g = (byte)Math.Clamp((g1 + m) * 255, 0, 255);
        b = (byte)Math.Clamp((b1 + m) * 255, 0, 255);
    }

    private static void RgbToHsv(byte r, byte g, byte b,
        out double h, out double s, out double v)
    {
        double rd = r / 255.0, gd = g / 255.0, bd = b / 255.0;
        double max = Math.Max(rd, Math.Max(gd, bd));
        double min = Math.Min(rd, Math.Min(gd, bd));
        double d = max - min;
        v = max;
        s = max == 0 ? 0 : d / max;
        if (d == 0) h = 0;
        else if (max == rd) h = 60 * ((gd - bd) / d % 6);
        else if (max == gd) h = 60 * ((bd - rd) / d + 2);
        else h = 60 * ((rd - gd) / d + 4);
        if (h < 0) h += 360;
    }
}
