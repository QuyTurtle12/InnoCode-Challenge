using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Repository.DTOs.ContestDTOs
{
    public class ContestPolicyDTO
    {
        [Required]
        public string Key { get; set; } = null!;

        public string? Value { get; set; }
    }
}
