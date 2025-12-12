using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Utility.Constant;

namespace Repository.DTOs.UserDTOs
{
    public class UpdateCurrentProfileDTO
    {
        public string? FullName { get; set; }
        //Only mentor use

        [MaxLength(20)]
        [RegularExpression(ValidationConstants.VietnamPhoneRegex, ErrorMessage = ValidationConstants.VietnamPhoneErrorMessage)]
        public string? Phone { get; set; }
    }
}
