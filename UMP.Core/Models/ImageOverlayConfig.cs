namespace UMP.Core.Models;

public class ImageOverlayConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ImagePath { get; set; } = "";
    /// <summary>Position X en fraction de la zone de rendu (0.0 a 1.0)</summary>
    public double X { get; set; } = 0.05;
    /// <summary>Position Y en fraction de la zone de rendu (0.0 a 1.0)</summary>
    public double Y { get; set; } = 0.05;
    /// <summary>Largeur en fraction de la zone de rendu (0.0 a 1.0)</summary>
    public double Width { get; set; } = 0.15;
    /// <summary>Hauteur en fraction de la zone de rendu (0.0 a 1.0)</summary>
    public double Height { get; set; } = 0.1;
    public double Opacity { get; set; } = 1.0;
    public double CornerRadius { get; set; } = 0;
    public double BorderWidth { get; set; } = 0;
    public string BorderColor { get; set; } = "#FFFFFF";
    /// <summary>Rotation en degres</summary>
    public double Rotation { get; set; } = 0;
    /// <summary>Apparition en ms (0 = debut)</summary>
    public long InMs { get; set; } = 0;
    /// <summary>Disparition en ms (0 = toujours visible)</summary>
    public long OutMs { get; set; } = 0;
}
