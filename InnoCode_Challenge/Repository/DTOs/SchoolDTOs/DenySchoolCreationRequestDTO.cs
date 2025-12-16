using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Repository.DTOs.SchoolDTOs
{
    public class DenySchoolCreationRequestDTO
    {
        [Required] public string DenyReason { get; set; } = null!;
    }

}
