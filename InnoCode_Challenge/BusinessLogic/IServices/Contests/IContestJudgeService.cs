using Repository.DTOs.ContestDTOs;

namespace BusinessLogic.IServices.Contests
{
    public interface IContestJudgeService
    {
        Task ParticipateAsync(JudgeContestDTO dto);
        Task LeaveAsync(JudgeContestDTO dto);

        Task<IList<JudgeInContestDTO>> GetJudgesByContestAsync(Guid contestId);
        Task<IList<JudgeContestDTO>> GetContestsOfCurrentJudgeAsync();
        Task<IList<JudgeContestDTO>> GetContestsOfJudgeAsync(Guid judgeUserId);

    }
}
