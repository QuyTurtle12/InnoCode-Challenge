using Repository.DTOs.ProvinceDTOs;
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
