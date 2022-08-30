﻿using StableDiffusionGui.Data;
using StableDiffusionGui.Io;
using StableDiffusionGui.Ui;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace StableDiffusionGui.Main
{
    internal class MainUi
    {
        public static int CurrentSteps;
        public static float CurrentScale;

        public static int CurrentResW;
        public static int CurrentResH;

        public static float CurrentInitStrength;

        public static string CurrentEmbeddingPath;

        private static readonly string[] validInitImgExtensions = new string[] { ".png", ".jpeg", ".jpg", ".jfif", ".bmp" };

        public static void HandleDroppedFiles(string[] paths)
        {
            foreach (string path in paths.Where(x => Path.GetExtension(x) == ".png"))
            {
                ImageMetadata meta = IoUtils.GetImageMetadata(path);

                if (!string.IsNullOrWhiteSpace(meta.Prompt))
                    Logger.Log($"Found metadata in {Path.GetFileName(path)}:\n{meta.ParsedText}");
            }

            if (paths.Length == 1)
            {
                if (validInitImgExtensions.Contains(paths[0])) // Ask to use as init img
                {
                    DialogResult dialogResult = UiUtils.ShowMessageBox($"Do you want to load this image as an initialization image?", $"Dropped {Path.GetFileName(paths[0]).Trunc(40)}", MessageBoxButtons.YesNo);

                    if (dialogResult == DialogResult.Yes)
                        Program.MainForm.TextboxInitImgPath.Text = paths[0];
                }

                if (Path.GetExtension(paths[0]) == ".pt") // Ask to use as embedding (finetuned model)
                {
                    DialogResult dialogResult = UiUtils.ShowMessageBox($"Do you want to load this embedding?", $"Dropped {Path.GetFileName(paths[0]).Trunc(40)}", MessageBoxButtons.YesNo);

                    if (dialogResult == DialogResult.Yes)
                        CurrentEmbeddingPath = paths[0];
                }
            }
        }

        public static List<float> GetScales(string customScalesText)
        {
            List<float> scales = new List<float> { CurrentScale };

            if (customScalesText.MatchesWildcard("* > * *"))
            {
                var splitMinMax = customScalesText.Trim().Split('>');
                float min = splitMinMax[0].GetFloat();
                float max = splitMinMax[1].Trim().Split(' ').First().GetFloat();
                float step = splitMinMax.Last().Split(' ').Last().GetFloat();

                List<float> incrementScales = new List<float>();

                for (float f = min; f < (max + 0.01f); f += step)
                    incrementScales.Add(f);

                if (incrementScales.Count > 0)
                    scales = incrementScales; // Replace list, don't use the regular scale slider at all in this mode
            }
            else
            {
                scales.AddRange(customScalesText.Replace(" ", "").Split(",").Select(x => x.GetFloat()).Where(x => x > 0.05f));
            }

            return scales;
        }
    }
}
