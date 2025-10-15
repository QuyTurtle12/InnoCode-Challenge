using Repository.DTOs.SubmissionDTOs;
using Utility.PaginatedList;

namespace BusinessLogic.IServices.Submissions
{
    public interface ISubmissionService
    {
        Task<PaginatedList<GetSubmissionDTO>> GetPaginatedSubmissionAsync(int pageNumber, int pageSize, Guid? idSearch, Guid? problemIdSearch, Guid? SubmittedByStudentId, string? teamName, string? studentName);
        Task CreateSubmissionAsync(CreateSubmissionDTO SubmissionDTO);
        Task UpdateSubmissionAsync(Guid id, UpdateSubmissionDTO SubmissionDTO);
    }
}
