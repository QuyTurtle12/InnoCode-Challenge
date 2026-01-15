using Repository.DTOs.JudgeDTOs;

namespace BusinessLogic.IServices.Contests
{
    public interface IJudge0Service
    {
        Task<JudgeSubmissionResultDTO> AutoEvaluateSubmissionAsync(JudgeSubmissionRequestDTO request);
    }
}
