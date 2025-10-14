using Repository.DTOs.MentorRegistrationDTOs;
using Utility.PaginatedList;

namespace BusinessLogic.IServices
{
    public interface IMentorRegistrationService
    {
        Task<MentorRegistrationAckDTO> SubmitAsync(RegisterMentorDTO dto);
        Task<PaginatedList<MentorRegistrationDTO>> GetAsync(MentorRegistrationQueryParams query);
        Task<MentorRegistrationDTO> GetByIdAsync(Guid id);
        Task<MentorRegistrationDTO> ApproveAsync(Guid id, ApproveMentorRegistrationDTO dto, Guid staffUserId);
        Task<MentorRegistrationDTO> DenyAsync(Guid id, DenyMentorRegistrationDTO dto, Guid staffUserId);
    }
}
