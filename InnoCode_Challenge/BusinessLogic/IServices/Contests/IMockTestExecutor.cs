using Repository.DTOs.MockTestDTOs;

namespace BusinessLogic.IServices.Contests
{
    public interface IMockTestExecutor
    {
        Task<MockTestResultDTO> ExecuteMockTestAsync(
            string studentCode,
            string mockTestUrl,
            int timeLimitSec = 30,
            int memoryLimitMb = 512);
    }
}
