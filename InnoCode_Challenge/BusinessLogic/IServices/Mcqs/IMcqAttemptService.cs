using Repository.DTOs.McqAttemptDTOs;
using Utility.PaginatedList;

namespace BusinessLogic.IServices.Mcqs
{
    public interface IMcqAttemptService
    {
        Task<PaginatedList<GetMcqAttemptDTO>> GetPaginatedMcqAttemptAsync(int pageNumber, int pageSize, Guid? idSearch, Guid? testIdSearch, Guid? roundIdSearch, Guid? studentIdSearch, string? testName, string? roundName, string? studentName);
        Task CreateMcqAttemptAsync(CreateMcqAttemptDTO mcqAttemptDTO);
    }
}
