namespace UMP.Core.Models;

public class Project
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "Nouveau projet";
    public List<Zone> Zones { get; set; } = new();
    public List<PhysicalButtonConfig> PhysicalButtons { get; set; } = new();
}
