using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Repository.DTOs.TeamInviteDTOs
{
    public class TeamInvitePublicActionDTO
    {
        public string Token { get; set; } = default!;
        public string Email { get; set; } = default!;
    }
}
