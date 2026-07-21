namespace UMP.Core.Models;

public class PipConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>Chemin du fichier video</summary>
    public string VideoPath { get; set; } = "";
    /// <summary>Position X en fraction (0.0 a 1.0)</summary>
    public double X { get; set; } = 0.65;
    /// <summary>Position Y en fraction (0.0 a 1.0)</summary>
    public double Y { get; set; } = 0.05;
    /// <summary>Largeur en fraction (0.0 a 1.0)</summary>
    public double Width { get; set; } = 0.3;
    /// <summary>Hauteur en fraction (0.0 a 1.0)</summary>
    public double Height { get; set; } = 0.25;
    public double Opacity { get; set; } = 1.0;
    public double CornerRadius { get; set; } = 0.05;
    public double BorderWidth { get; set; } = 0;
    public string BorderColor { get; set; } = "#FFFFFF";
    /// <summary>Rotation en degres</summary>
    public double Rotation { get; set; } = 0;
    /// <summary>Apparition en ms (0 = debut)</summary>
    public long InMs { get; set; } = 0;
    /// <summary>Disparition en ms (0 = toujours visible)</summary>
    public long OutMs { get; set; } = 0;
    /// <summary>Volume 0-100</summary>
    public int Volume { get; set; } = 0;
    /// <summary>Boucler la video PiP</summary>
    public bool IsLooping { get; set; } = true;
    /// <summary>Demarre cache (la video tourne mais le PiP est invisible)</summary>
    public bool StartHidden { get; set; } = false;
}
