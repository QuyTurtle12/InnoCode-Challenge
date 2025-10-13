using Repository.DTOs.ProvinceDTOs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Utility.PaginatedList;

namespace BusinessLogic.IServices
{
    public interface IProvinceService
    {
        Task<PaginatedList<ProvinceDTO>> GetAsync(ProvinceQueryParams query);
        Task<ProvinceDTO> GetByIdAsync(Guid id);
        Task<ProvinceDTO> CreateAsync(CreateProvinceDTO dto);
        Task<ProvinceDTO> UpdateAsync(Guid id, UpdateProvinceDTO dto);
        Task DeleteAsync(Guid id);
    }
}
