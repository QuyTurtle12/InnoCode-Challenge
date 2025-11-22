using Microsoft.AspNetCore.Http;
using System;
using System.ComponentModel.DataAnnotations;

namespace Repository.DTOs.ContestDTOs
{
    public class CreateContestAdvancedDTO
    {
        [Required, Range(1900, 9999)]
        public int Year { get; set; }

        [Required, MaxLength(200)]
        public string Name { get; set; } = null!;

        public IFormFile? ImageFile { get; set; }

        public string? Description { get; set; }

        // Contest timeline 
        public DateTime? Start { get; set; }
        public DateTime? End { get; set; }

        // Registration timeline
        public DateTime? RegistrationStart { get; set; }
        public DateTime? RegistrationEnd { get; set; }

        [Range(1, 50)]
        public int? TeamMembersMax { get; set; }  

        [Range(1, 10000)]
        public int? TeamLimitMax { get; set; }    

        // Free text rewards / notes
        [MaxLength(4000)]
        public string? RewardsText { get; set; }
    }
}
