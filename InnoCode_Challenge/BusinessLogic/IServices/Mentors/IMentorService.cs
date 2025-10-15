using Repository.DTOs.MentorDTOs;
using Utility.PaginatedList;

namespace BusinessLogic.IServices.Mentors
{
    public interface IMentorService
    {
        Task<PaginatedList<MentorDTO>> GetAsync(MentorQueryParams queryParams);
        Task<MentorDTO> GetByIdAsync(Guid id);
        Task<MentorDTO> CreateAsync(CreateMentorDTO dto);
        Task<MentorDTO> UpdateAsync(Guid id, UpdateMentorDTO dto);
        Task DeleteAsync(Guid id);
    }
}
