﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StableDiffusionGui.Data
{
    public class TtiTaskInfo
    {
        public int ImgCount { get; set; }
        public int TargetImgCount { get; set; }
        public string OutPath { get; set; } = "";
        public DateTime StartTime { get; set; } = new DateTime();
    }
}
