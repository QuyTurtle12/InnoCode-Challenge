using Repository.DTOs.AppealDTOs;
using Utility.Enums;
using Utility.PaginatedList;

namespace BusinessLogic.IServices.Appeals
{
    public interface IAppealService
    {
        Task<GetAppealDTO> CreateAppealAsync(CreateAppealDTO dto);
        Task<GetAppealDTO> GetAppealByIdAsync(Guid appealId);
        Task<PaginatedList<GetAppealDTO>> GetPaginatedAppealsAsync(
            int pageNumber,
            int pageSize,
            Guid? appealId,
            Guid? teamId,
            Guid? roundId,
            AppealStateEnum? state,
            AppealDecisionEnum? decision,
            bool isMyAppeals);
        Task<GetAppealDTO> ReviewAppealAsync(Guid appealId, ReviewAppealDTO dto);
    }
}
