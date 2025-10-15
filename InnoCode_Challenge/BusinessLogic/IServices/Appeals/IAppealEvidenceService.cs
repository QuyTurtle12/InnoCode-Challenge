using Repository.DTOs.AppealEvidenceDTOs;

namespace BusinessLogic.IServices.Appeals
{
    public interface IAppealEvidenceService
    {
        Task CreateAppealEvidenceAsync(CreateAppealEvidenceDTO AppealEvidenceDTO);
        Task DeleteAppealEvidenceAsync(Guid id);
    }
}
