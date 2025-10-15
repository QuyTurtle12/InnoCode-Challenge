using Repository.DTOs.ConfigDTOs;
using Utility.PaginatedList;

namespace BusinessLogic.IServices
{
    public interface IConfigService
    {
        Task<PaginatedList<ConfigDTO>> GetAsync(ConfigQueryParams query);
        Task<ConfigDTO> GetByKeyAsync(string key);
        Task<ConfigDTO> CreateAsync(CreateConfigDTO dto);
        Task<ConfigDTO> UpdateAsync(string key, UpdateConfigDTO dto);
        Task DeleteAsync(string key);
    }
}
