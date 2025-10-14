using Repository.DTOs.AppealEvidenceDTOs;

namespace BusinessLogic.IServices
{
    public interface IAppealEvidenceService
    {
        Task CreateAppealEvidenceAsync(CreateAppealEvidenceDTO AppealEvidenceDTO);
        Task DeleteAppealEvidenceAsync(Guid id);
    }
}
