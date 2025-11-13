using Repository.DTOs.ConfigDTOs;
using Utility.Enums;
using Utility.PaginatedList;

namespace BusinessLogic.IServices
{
    public interface IConfigService
    {
        Task<PaginatedList<ConfigDTO>> GetAsync(ConfigQueryParams query);
        Task<ConfigDTO> GetByKeyAsync(string key);
        Task<ConfigDTO> CreateAsync(CreateConfigDTO dto, string performedByRole);
        Task<ConfigDTO> UpdateAsync(string key, UpdateConfigDTO dto, string performedByRole);
        Task DeleteAsync(string key, string performedByRole);

        Task SetRegistrationWindowAsync(Guid contestId, SetRegistrationWindowDTO dto, string performedByRole);
        Task SetContestPolicyAsync(Guid contestId, SetContestPolicyDTO dto, string performedByRole);

        Task<bool> AreSubmissionsDistributedAsync(Guid roundId);
        Task MarkSubmissionsAsDistributedAsync(Guid roundId);
        Task ResetDistributionStatusAsync(Guid roundId);
        Task<string> DownloadImportTemplate(ImportTemplateEnum template);
    }
}
