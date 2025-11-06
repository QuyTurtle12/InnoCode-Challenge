using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Repository.DTOs.ContestDTOs
{
    public class JudgeParticipateContestDTO
    {
        [Required]
        public Guid ContestId { get; set; }
    }
}
