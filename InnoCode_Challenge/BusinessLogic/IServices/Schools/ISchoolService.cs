using Repository.DTOs.SchoolDTOs;
using Utility.PaginatedList;

namespace BusinessLogic.IServices.Schools
{
    public interface ISchoolService
    {
        Task<PaginatedList<SchoolDTO>> GetAsync(SchoolQueryParams queryParams);
        Task<SchoolDTO> GetByIdAsync(Guid id);
        Task<SchoolDTO> CreateAsync(CreateSchoolDTO dto);
        Task<SchoolDTO> UpdateAsync(Guid id, UpdateSchoolDTO dto);
        Task DeleteAsync(Guid id);
    }
}
