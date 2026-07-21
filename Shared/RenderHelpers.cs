using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace UMP.Shared;

/// <summary>
/// Helpers de rendu WPF partages (apercu, player, editeur).
/// Remplace les copies privees ParseBrush/SafeSize/ParseFontWeight/... qui
/// vivaient dans PlayerWindow, PreviewWindow et les panneaux.
/// </summary>
public static class RenderHelpers
{
    public static SolidColorBrush ParseBrush(string hex, Color fallback)
    {
        try { return new SolidColorBrush((Color)System.Windows.Media.ColorConverter.ConvertFromString(hex)); }
        catch { return new SolidColorBrush(fallback); }
    }

    /// <summary>
    /// Garantit une taille d'overlay finie et non nulle. Un Border de largeur 0 (ou NaN)
    /// contenant un TextBlock TextWrapping=Wrap renvoie une DesiredSize NaN, ce qui fait
    /// planter la passe de layout WPF (InvalidOperationException relancee a chaque rendu).
    /// </summary>
    public static double SafeSize(double v) => double.IsFinite(v) && v >= 1 ? v : 1;

    public static FontWeight ParseFontWeight(string weight) => weight switch
    {
        "Thin" => FontWeights.Thin,
        "Light" => FontWeights.Light,
        "Normal" => FontWeights.Normal,
        "Medium" => FontWeights.Medium,
        "SemiBold" => FontWeights.SemiBold,
        "Bold" => FontWeights.Bold,
        "ExtraBold" => FontWeights.ExtraBold,
        "Black" => FontWeights.Black,
        _ => FontWeights.Bold
    };

    public static TextAlignment ParseTextAlignment(string align) => align switch
    {
        "Left" => TextAlignment.Left,
        "Right" => TextAlignment.Right,
        _ => TextAlignment.Center
    };

    /// <summary>Cree un FontFamily a partir d'un fichier .ttf/.otf.</summary>
    public static FontFamily CreateFontFamily(string fontFilePath)
    {
        var dir = System.IO.Path.GetDirectoryName(fontFilePath)!.Replace('\\', '/');
        var glyph = new GlyphTypeface(new Uri(fontFilePath));
        var familyName = glyph.FamilyNames.Values.FirstOrDefault() ?? "Unknown";
        return new FontFamily(new Uri("file:///" + dir + "/"), "./#" + familyName);
    }

    /// <summary>
    /// Resout une police : fichier custom si present sur disque, sinon police systeme.
    /// Ne leve jamais (fallback Segoe UI).
    /// </summary>
    public static FontFamily ResolveFontFamily(string? customFontPath, string familyName)
    {
        try
        {
            if (!string.IsNullOrEmpty(customFontPath) && System.IO.File.Exists(customFontPath))
                return CreateFontFamily(customFontPath);
            return new FontFamily(familyName);
        }
        catch { return new FontFamily("Segoe UI"); }
    }

    public static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T t) return t;
            var r = FindChild<T>(child);
            if (r is not null) return r;
        }
        return null;
    }
}
