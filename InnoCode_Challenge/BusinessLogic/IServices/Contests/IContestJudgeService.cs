using Repository.DTOs.ContestDTOs;

namespace BusinessLogic.IServices.Contests
{
    public interface IContestJudgeService
    {
        Task ParticipateAsync(JudgeParticipateContestDTO dto);
        Task LeaveAsync(JudgeParticipateContestDTO dto);
    }
}
