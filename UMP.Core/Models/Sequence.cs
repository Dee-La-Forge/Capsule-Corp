namespace UMP.Core.Models;

public enum SequenceMode { Single, Scenario }
public enum SequenceTransition { Cut }

public class Sequence
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public SequenceMode Mode { get; set; } = SequenceMode.Single;
    public bool IsLooping { get; set; } = false;
    public List<SequenceItem> Items { get; set; } = new();
    public int CurrentIndex { get; set; } = 0;
}
