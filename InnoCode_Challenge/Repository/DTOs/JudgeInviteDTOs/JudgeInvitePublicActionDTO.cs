using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Repository.DTOs.JudgeInviteDTOs
{
    public class JudgeInvitePublicActionDTO
    {
        public string InviteCode { get; set; } = default!;
        public string Email { get; set; } = default!;
    }
}
