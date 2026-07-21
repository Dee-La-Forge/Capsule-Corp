using UMP.Core.Models;
using UMP.Modules.Media;

namespace UMP.Shared;

public class ZoneRuntime
{
    public Zone Zone { get; }
    public MediaModule MediaModule { get; }

    public Action<string>? PlayAction { get; set; }
    public Action? PauseAction { get; set; }
    public Action? StopAction { get; set; }
    public Func<long>? GetCurrentMsAction { get; set; }

    public ZoneRuntime(Zone zone, MediaModule mediaModule)
    {
        Zone = zone;
        MediaModule = mediaModule;
    }
}
