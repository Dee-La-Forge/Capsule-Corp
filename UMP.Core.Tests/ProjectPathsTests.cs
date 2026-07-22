using UMP.Core.Models;
using UMP.Core.Services;

namespace UMP.Core.Tests;

public class ProjectPathsTests
{
    /// <summary>
    /// Projet couvrant TOUS les champs chemin du modele, chacun avec un
    /// marqueur unique. Si un nouveau champ chemin est ajoute a un modele
    /// sans etre ajoute a ProjectPaths.Transform, ce test doit etre enrichi.
    /// </summary>
    private static Project BuildFullProject() => new()
    {
        Zones =
        {
            new Zone
            {
                MediaFilePath = "zone.media",
                Sequence = new Sequence
                {
                    Items =
                    {
                        new SequenceItem
                        {
                            MediaPath = "item.media",
                            ImageSlidePath = "item.slide",
                            Buttons =
                            {
                                new ButtonConfig
                                {
                                    ImagePath = "btn.img",
                                    ImagePathOn = "btn.imgon",
                                    CustomFontPath = "btn.font",
                                    Actions = { new ButtonAction { MediaPath = "btn.action" } },
                                    ActionsOn = { new ButtonAction { MediaPath = "btn.actionon" } }
                                }
                            },
                            ImageOverlays = { new ImageOverlayConfig { ImagePath = "overlay.img" } },
                            Subtitles = { new SubtitleConfig { FilePath = "sub.srt", CustomFontPath = "sub.font" } },
                            PictureInPictures = { new PipConfig { VideoPath = "pip.video" } }
                        }
                    }
                }
            }
        },
        PhysicalButtons =
        {
            new PhysicalButtonConfig
            {
                Actions = { new ButtonAction { MediaPath = "physical.action" } }
            }
        }
    };

    private static readonly string[] AllMarkers =
    {
        "zone.media", "item.media", "item.slide",
        "btn.img", "btn.imgon", "btn.font", "btn.action", "btn.actionon",
        "overlay.img", "sub.srt", "sub.font", "pip.video", "physical.action"
    };

    [Fact]
    public void Transform_VisiteTousLesChampsChemin()
    {
        var project = BuildFullProject();
        var visited = new List<string?>();

        ProjectPaths.Transform(project, p => { visited.Add(p); return p; });

        foreach (var marker in AllMarkers)
            Assert.Contains(marker, visited);
    }

    [Fact]
    public void Transform_ReaffecteLesValeursTransformees()
    {
        var project = BuildFullProject();

        ProjectPaths.Transform(project, p => p is null ? null : "X_" + p);

        var zone = project.Zones[0];
        var item = zone.Sequence!.Items[0];
        Assert.Equal("X_zone.media", zone.MediaFilePath);
        Assert.Equal("X_item.media", item.MediaPath);
        Assert.Equal("X_item.slide", item.ImageSlidePath);
        Assert.Equal("X_btn.img", item.Buttons[0].ImagePath);
        Assert.Equal("X_btn.imgon", item.Buttons[0].ImagePathOn);
        Assert.Equal("X_btn.font", item.Buttons[0].CustomFontPath);
        Assert.Equal("X_btn.action", item.Buttons[0].Actions[0].MediaPath);
        Assert.Equal("X_btn.actionon", item.Buttons[0].ActionsOn[0].MediaPath);
        Assert.Equal("X_overlay.img", item.ImageOverlays[0].ImagePath);
        Assert.Equal("X_sub.srt", item.Subtitles[0].FilePath);
        Assert.Equal("X_sub.font", item.Subtitles[0].CustomFontPath);
        Assert.Equal("X_pip.video", item.PictureInPictures[0].VideoPath);
        Assert.Equal("X_physical.action", project.PhysicalButtons[0].Actions[0].MediaPath);
    }

    [Fact]
    public void Transform_ZoneSansSequence_NePlantePas()
    {
        var project = new Project { Zones = { new Zone { MediaFilePath = "m" } } };
        ProjectPaths.Transform(project, p => p);
        Assert.Equal("m", project.Zones[0].MediaFilePath);
    }

    [Fact]
    public void Transform_ProjetVide_NePlantePas()
    {
        ProjectPaths.Transform(new Project(), p => p);
    }
}
