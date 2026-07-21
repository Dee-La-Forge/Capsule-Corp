namespace UMP.Core.Models;

public class SubtitleConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>Chemin du fichier SRT/VTT</summary>
    public string FilePath { get; set; } = "";
    /// <summary>Position Y en fraction (0.0 a 1.0, defaut bas)</summary>
    public double Y { get; set; } = 0.85;
    /// <summary>Largeur en fraction (0.0 a 1.0)</summary>
    public double Width { get; set; } = 0.8;
    /// <summary>Position X en fraction (0.0 a 1.0, centre)</summary>
    public double X { get; set; } = 0.1;
    public string FontFamily { get; set; } = "Segoe UI";
    /// <summary>Chemin vers un fichier .ttf/.otf personnalise (vide = police systeme)</summary>
    public string CustomFontPath { get; set; } = "";
    public string FontWeight { get; set; } = "Bold";
    /// <summary>Taille de police en points</summary>
    public double FontSize { get; set; } = 18;
    public string TextColor { get; set; } = "#FFFFFF";
    public string BackgroundColor { get; set; } = "#99000000";
    public double Opacity { get; set; } = 1.0;
    public double Padding { get; set; } = 8;
    public double CornerRadius { get; set; } = 0.05;
    public double BorderWidth { get; set; } = 0;
    public string BorderColor { get; set; } = "#FFFFFF";
    /// <summary>Alignement : Left, Center, Right</summary>
    public string TextAlign { get; set; } = "Center";
    /// <summary>Rotation en degres</summary>
    public double Rotation { get; set; } = 0;
    /// <summary>Ombre portee — couleur</summary>
    public string ShadowColor { get; set; } = "#000000";
    /// <summary>Ombre portee — flou (0 = desactive)</summary>
    public double ShadowBlur { get; set; } = 0;
    /// <summary>Ombre portee — decalage X</summary>
    public double ShadowOffsetX { get; set; } = 2;
    /// <summary>Ombre portee — decalage Y</summary>
    public double ShadowOffsetY { get; set; } = 2;
    /// <summary>Contour — epaisseur (0 = desactive)</summary>
    public double OutlineWidth { get; set; } = 0;
    /// <summary>Contour — couleur</summary>
    public string OutlineColor { get; set; } = "#000000";
}
