using Repository.DTOs.SchoolDTOs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Utility.PaginatedList;

namespace BusinessLogic.IServices.Schools
{
    public interface ISchoolCreationRequestService
    {
        Task<SchoolCreationRequestDetailDTO> CreateAsync(CreateSchoolCreationRequestFormDTO dto, Guid requestedByUserId);
        Task<PaginatedList<SchoolCreationRequestListDTO>> GetListAsync(SchoolCreationRequestQueryParams query);
        Task<PaginatedList<SchoolCreationRequestListDTO>> GetMyAsync(Guid requestedByUserId, SchoolCreationRequestQueryParams query);
        Task<SchoolCreationRequestDetailDTO> GetByIdAsync(Guid requestId, Guid requesterUserId, string requesterRole);
        Task ApproveAsync(Guid requestId, Guid reviewerUserId);
        Task DenyAsync(Guid requestId, Guid reviewerUserId, string denyReason);
    }

}
