using Repository.DTOs.SchoolDTOs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Utility.PaginatedList;

namespace BusinessLogic.IServices
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
