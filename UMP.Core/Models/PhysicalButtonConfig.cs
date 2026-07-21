namespace UMP.Core.Models;

public class PhysicalButtonConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>Binding source: "key:F1", "key:Space", "joy:0:3" (joystick 0, bouton 3)</summary>
    public string Binding { get; set; } = "";
    public string Label { get; set; } = "Bouton";
    public List<ButtonAction> Actions { get; set; } = new();
}
