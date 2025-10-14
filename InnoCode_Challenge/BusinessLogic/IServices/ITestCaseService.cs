using Repository.DTOs.TestCaseDTOs;
using Utility.PaginatedList;

namespace BusinessLogic.IServices
{
    public interface ITestCaseService
    {
        Task<PaginatedList<GetTestCaseDTO>> GetPaginatedTestCaseAsync(int pageNumber, int pageSize, Guid? idSearch, Guid? problemIdSearch);
        Task CreateTestCaseAsync(CreateTestCaseDTO TestCaseDTO);
        Task UpdateTestCaseAsync(Guid id, UpdateTestCaseDTO TestCaseDTO);
        Task DeleteTestCaseAsync(Guid id);
    }
}
