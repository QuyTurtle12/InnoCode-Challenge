using Repository.DTOs.SubmissionArtifactDTOs;
using Utility.PaginatedList;

namespace BusinessLogic.IServices.Submissions
{
    public interface ISubmissionArtifactService
    {
        Task<PaginatedList<GetSubmissionArtifactDTO>> GetPaginatedSubmissionArtifactAsync(int pageNumber, int pageSize, Guid? idSearch, Guid? submissionIdSearch);
        Task CreateSubmissionArtifactAsync(CreateSubmissionArtifactDTO SubmissionArtifactDTO);
        Task UpdateSubmissionArtifactAsync(Guid id, UpdateSubmissionArtifactDTO SubmissionArtifactDTO);
        Task DeleteSubmissionArtifactAsync(Guid id);
    }
}
