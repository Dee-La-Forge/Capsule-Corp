namespace UMP.Core.Models;

public class Zone
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "Zone";
    public string? MediaFilePath { get; set; }
    public int ScreenIndex { get; set; } = 0;
    /// <summary>Identifiant stable de l'ecran (DeviceName), utilise en priorite sur ScreenIndex</summary>
    public string ScreenDeviceName { get; set; } = "";
    public int Volume { get; set; } = 100;
    public bool IsMuted { get; set; } = false;
    public bool IsLooping { get; set; } = false;
    public Sequence? Sequence { get; set; }
}
