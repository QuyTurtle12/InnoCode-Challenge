using Repository.DTOs.AttachmentDTOs;
using Utility.PaginatedList;

namespace BusinessLogic.IServices
{
    public interface IAttachmentService
    {
        Task<PaginatedList<AttachmentDTO>> GetAsync(AttachmentQueryParams query);
        Task<AttachmentDTO> GetByIdAsync(Guid id);
        Task<AttachmentDTO> CreateAsync(CreateAttachmentDTO dto);
        Task<AttachmentDTO> UpdateAsync(Guid id, UpdateAttachmentDTO dto);
        Task DeleteAsync(Guid id);
    }
}
