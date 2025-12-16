using Repository.DTOs.MentorManagementDTOs;

namespace BusinessLogic.IServices.Mentors
{
    public interface IMentorManagementService
    {
        Task<MentorProfileDTO> CreateMentorAsync(Guid schoolId, MentorManagerRequestDTO dto, Guid requesterUserId);
    }
}
