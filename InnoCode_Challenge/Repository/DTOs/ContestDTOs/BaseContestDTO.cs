using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Utility.Enums;

namespace Repository.DTOs.ContestDTOs
{
    public class BaseContestDTO
    {
        public int Year { get; set; }

        [Required]
        public string Name { get; set; } = null!;

        public string Description { get; set; } = null!;

        public string ImgUrl { get; set; } = null!;

        public DateTime? Start { get; set; }

        public DateTime? End { get; set; }
    }
}
