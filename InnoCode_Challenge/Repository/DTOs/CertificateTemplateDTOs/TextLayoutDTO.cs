using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Repository.DTOs.CertificateTemplateDTOs
{
    public class TextLayoutDTO
    {
        public string FontFamily { get; set; } = "Arial";
        public float FontSize { get; set; } = 64f;
        public string ColorHex { get; set; } = "#1F2937"; 
        public int X { get; set; } = 960;   
        public int Y { get; set; } = 540;
        public int MaxWidth { get; set; } = 1600;
        public string Align { get; set; } = "center";
    }
}
