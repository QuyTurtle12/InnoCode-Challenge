using Repository.DTOs.SubmissionDetailDTOs;
using Utility.PaginatedList;

namespace BusinessLogic.IServices
{
    public interface ISubmissionDetailService
    {
        Task<PaginatedList<GetSubmissionDetailDTO>> GetPaginatedSubmissionDetailAsync(int pageNumber, int pageSize, Guid? idSearch, Guid? submissionIdSearch, Guid? TestcaseId);
        Task CreateSubmissionDetailAsync(CreateSubmissionDetailDTO SubmissionDetailDTO);
        Task UpdateSubmissionDetailAsync(Guid id, UpdateSubmissionDetailDTO SubmissionDetailDTO);
        Task DeleteSubmissionDetailAsync(Guid id);
    }
}
