using Microsoft.AspNetCore.Http;
using Repository.DTOs.TestCaseDTOs;
using Utility.Enums;
using Utility.PaginatedList;

namespace BusinessLogic.IServices.Contests
{
    public interface ITestCaseService
    {
        Task<PaginatedList<GetTestCaseDTO>> GetTestCasesByRoundIdAsync(Guid roundId, int pageNumber, int pageSize);
        Task CreateTestCaseAsync(Guid roundId, CreateTestCaseDTO testCaseDTO);
        Task BulkUpdateTestCasesAsync(Guid roundId, IList<BulkUpdateTestCaseDTO> testCaseDTOs);
        Task DeleteTestCaseAsync(Guid id);
        Task<TestCaseImportResultDTO> ImportTestCasesFromCsvAsync(IFormFile csvFile, Guid roundId);
    }
}
