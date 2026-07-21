using System.IO;
using System.Text.Json.Serialization;

namespace UMP.Core.Models;

public enum ImageSlideDuration { Fixed, UntilClick }

public class SequenceItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string? MediaPath { get; set; }
    public string? ImageSlidePath { get; set; }
    public long? DurationMs { get; set; }
    public ImageSlideDuration SlideDuration { get; set; } = ImageSlideDuration.Fixed;
    public SequenceTransition Transition { get; set; } = SequenceTransition.Cut;
    public List<ButtonConfig> Buttons { get; set; } = new();
    public List<ImageOverlayConfig> ImageOverlays { get; set; } = new();
    public List<SubtitleConfig> Subtitles { get; set; } = new();
    public List<PipConfig> PictureInPictures { get; set; } = new();

    /// <summary>Si true, cet item se rejoue indefiniment</summary>
    public bool IsLooping { get; set; } = false;

    // Proprietes calculees — exclues de la serialisation : leur valeur figee a la
    // sauvegarde etait trompeuse (ex. "HasValidMedia": true sans garantie actuelle).
    [JsonIgnore] public bool IsImageSlide => !string.IsNullOrEmpty(ImageSlidePath);
    [JsonIgnore] public bool IsVideo => !string.IsNullOrEmpty(MediaPath)
                          && string.IsNullOrEmpty(ImageSlidePath);
    [JsonIgnore] public bool HasValidMedia => IsImageSlide
        ? File.Exists(ImageSlidePath)
        : !string.IsNullOrEmpty(MediaPath) && File.Exists(MediaPath);
    [JsonIgnore] public string? EffectivePath => IsImageSlide ? ImageSlidePath : MediaPath;
}
