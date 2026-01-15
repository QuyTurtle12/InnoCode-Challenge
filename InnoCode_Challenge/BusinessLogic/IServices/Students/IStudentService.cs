using Repository.DTOs.StudentDTOs;
using Utility.PaginatedList;

namespace BusinessLogic.IServices.Students
{
    public interface IStudentService
    {
        Task<PaginatedList<StudentDTO>> GetAsync(StudentQueryParams queryParams);
        Task<StudentDTO> GetByIdAsync(Guid id);
        Task<StudentDTO> CreateAsync(CreateStudentDTO dto);
        Task<StudentDTO> UpdateAsync(Guid id, UpdateStudentDTO dto);
        Task DeleteAsync(Guid id);
    }
}
