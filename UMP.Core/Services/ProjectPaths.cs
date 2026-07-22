using UMP.Core.Models;

namespace UMP.Core.Services;

/// <summary>
/// Parcours generique de TOUS les chemins de fichiers d'un projet.
/// Utilise par la sauvegarde/chargement (chemins relatifs portables) et par
/// l'export (collecte + remapping). Un seul endroit a maintenir : ajouter un
/// champ chemin dans un modele = l'ajouter ici, et tout le monde en profite.
/// </summary>
public static class ProjectPaths
{
    /// <summary>
    /// Applique <paramref name="f"/> a chaque chemin du projet et reaffecte
    /// le resultat. Pour une simple collecte, retourner la valeur inchangee.
    /// </summary>
    public static void Transform(Project project, Func<string?, string?> f)
    {
        foreach (var zone in project.Zones)
        {
            zone.MediaFilePath = f(zone.MediaFilePath);
            if (zone.Sequence is null) continue;
            foreach (var item in zone.Sequence.Items)
            {
                item.MediaPath = f(item.MediaPath);
                item.ImageSlidePath = f(item.ImageSlidePath);
                foreach (var btn in item.Buttons)
                {
                    btn.ImagePath = f(btn.ImagePath);
                    btn.ImagePathOn = f(btn.ImagePathOn);
                    btn.CustomFontPath = f(btn.CustomFontPath) ?? "";
                    foreach (var a in btn.Actions) a.MediaPath = f(a.MediaPath);
                    foreach (var a in btn.ActionsOn) a.MediaPath = f(a.MediaPath);
                }
                foreach (var img in item.ImageOverlays)
                    img.ImagePath = f(img.ImagePath) ?? "";
                foreach (var sub in item.Subtitles)
                {
                    sub.FilePath = f(sub.FilePath) ?? "";
                    sub.CustomFontPath = f(sub.CustomFontPath) ?? "";
                }
                foreach (var pip in item.PictureInPictures)
                    pip.VideoPath = f(pip.VideoPath) ?? "";
            }
        }
        foreach (var pb in project.PhysicalButtons)
            foreach (var a in pb.Actions)
                a.MediaPath = f(a.MediaPath);
    }
}
