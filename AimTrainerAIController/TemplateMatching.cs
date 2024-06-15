using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.IO;

namespace AimTrainerAIController;

public class TemplateMatching
{
    private Dictionary<string, List<Mat>> templates;

    public TemplateMatching(string gameFolder)
    {
        templates = new Dictionary<string, List<Mat>>();
        LoadTemplates(gameFolder);
    }

    private void LoadTemplates(string gameFolder)
    {
        string[] categories = { "crosshairs", "hit_vfx", "bullet_traces", "hit_markers" };
        foreach (var category in categories)
        {
            var categoryPath = Path.Combine(gameFolder, category);
            if (Directory.Exists(categoryPath))
            {
                templates[category] = new List<Mat>();
                foreach (var file in Directory.GetFiles(categoryPath))
                {
                    templates[category].Add(Cv2.ImRead(file, ImreadModes.Grayscale));
                }
            }
        }
    }

    public (bool, Point) MatchTemplate(Mat frame, string category)
    {
        if (!templates.ContainsKey(category)) return (false, new Point());

        foreach (var template in templates[category])
        {
            Mat result = new Mat();
            Cv2.MatchTemplate(frame, template, result, TemplateMatchModes.CCoeffNormed);
            Cv2.MinMaxLoc(result, out double minVal, out double maxVal, out Point minLoc, out Point maxLoc);

            if (maxVal > 0.8) // Adjust threshold as needed
            {
                return (true, maxLoc);
            }
        }

        return (false, new Point());
    }
}