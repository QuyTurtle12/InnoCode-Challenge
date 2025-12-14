using Repository.DTOs.RoleRegistrationDTOs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Utility.PaginatedList;

namespace BusinessLogic.IServices.Users
{
    public interface IRoleRegistrationService
    {
        Task<RoleRegistrationSubmittedDTO> SubmitAsync(CreateRoleRegistrationDTO dto);
        Task<PaginatedList<RoleRegistrationDTO>> GetAsync(RoleRegistrationQueryParams query);
        Task<RoleRegistrationDetailDTO> GetByIdAsync(Guid id);

        Task ApproveAsync(Guid id, Guid reviewerUserId);
        Task DenyAsync(Guid id, string reason, Guid reviewerUserId);
    }
}
