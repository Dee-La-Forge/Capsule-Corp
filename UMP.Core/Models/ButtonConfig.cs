using System.Text.Json.Serialization;

namespace UMP.Core.Models;

public enum ButtonActionType
{
    Play, Pause, Stop,
    PlaySequence, PauseSequence, StopSequence, ToggleSequence,
    JumpToItem, PlayMedia, SwitchMedia,
    StopAllScreens,
    JumpToItemAllScreens,
    TogglePip, ShowPip, HidePip
}

public class ButtonAction
{
    public ButtonActionType Type { get; set; } = ButtonActionType.Play;
    public int? TargetItemIndex { get; set; }
    /// <summary>Dernier item de la plage pour JumpToItemAllScreens (inclus)</summary>
    public int? EndItemIndex { get; set; }
    public string? MediaPath { get; set; }
    public string? ZoneId { get; set; }
}

public enum BorderPosition { Inside, Outside }

public class ButtonConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>Position X en fraction de la zone de rendu (0.0 a 1.0)</summary>
    public double X { get; set; } = 0.05;
    /// <summary>Position Y en fraction de la zone de rendu (0.0 a 1.0)</summary>
    public double Y { get; set; } = 0.1;
    /// <summary>Largeur en fraction de la zone de rendu (0.0 a 1.0)</summary>
    public double Width { get; set; } = 0.09;
    /// <summary>Hauteur en fraction de la zone de rendu (0.0 a 1.0)</summary>
    public double Height { get; set; } = 0.06;
    public string Label { get; set; } = "Bouton";
    public string BackgroundColor { get; set; } = "#C85C4FBF";
    public string TextColor { get; set; } = "#FFFFFF";
    /// <summary>Taille de police en points (pt)</summary>
    public double FontSize { get; set; } = 14;
    /// <summary>CornerRadius en fraction de la plus petite dimension du bouton</summary>
    public double CornerRadius { get; set; } = 0.25;
    public string FontFamily { get; set; } = "Segoe UI";
    /// <summary>Chemin vers un fichier .ttf/.otf personnalise (vide = police systeme)</summary>
    public string CustomFontPath { get; set; } = "";
    public string FontWeight { get; set; } = "SemiBold";
    /// <summary>Padding interieur en pixels</summary>
    public double Padding { get; set; } = 6;
    /// <summary>Epaisseur de bordure en pixels</summary>
    public double BorderWidth { get; set; } = 0;
    /// <summary>Couleur de bordure</summary>
    public string BorderColor { get; set; } = "#FFFFFF";
    /// <summary>Position de la bordure</summary>
    public BorderPosition BorderPos { get; set; } = BorderPosition.Inside;
    /// <summary>Opacite du bouton (0.0 a 1.0)</summary>
    public double Opacity { get; set; } = 1.0;
    /// <summary>Rotation en degres</summary>
    public double Rotation { get; set; } = 0;
    public string? ImagePath { get; set; }
    public string? ImagePathOn { get; set; }
    public bool IsToggle { get; set; } = false;
    public string LabelOn { get; set; } = "ON";
    public double FontSizeHover { get; set; } = 14;
    public string FontWeightHover { get; set; } = "SemiBold";
    public double BorderWidthHover { get; set; } = 0;
    public string BorderColorHover { get; set; } = "#FFFFFF";
    public string BackgroundColorHover { get; set; } = "#C85C4FBF";
    public string TextColorHover { get; set; } = "#FFFFFF";
    public double FontSizeOn { get; set; } = 14;
    public string FontWeightOn { get; set; } = "Bold";
    public double BorderWidthOn { get; set; } = 0;
    public string BorderColorOn { get; set; } = "#FFFFFF";
    public string BackgroundColorOn { get; set; } = "#CC5C4FBF";
    public string TextColorOn { get; set; } = "#FFFFFF";
    /// <summary>Apparition du bouton en ms (0 = debut)</summary>
    public long InMs { get; set; } = 0;
    /// <summary>Disparition du bouton en ms (0 = toujours visible)</summary>
    public long OutMs { get; set; } = 0;
    [JsonIgnore] public bool IsToggleActive { get; set; } = false;
    public List<ButtonAction> Actions { get; set; } = new();
    public List<ButtonAction> ActionsOn { get; set; } = new();
}
