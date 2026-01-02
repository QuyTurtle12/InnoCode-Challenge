using DataAccess.Entities;
using Microsoft.EntityFrameworkCore;
using Repository.IRepositories;
using Utility.Enums;

namespace BusinessLogic.Helpers
{
    /// <summary>
    /// Centralized helper to mark rounds as Finalized once all work is done
    /// (submissions finished, appeals closed, retake finished if any).
    /// </summary>
    public static class RoundFinalizer
    {
        public static async Task TryFinalizeAsync(IUOW unitOfWork, Guid roundId)
        {
            var roundRepo = unitOfWork.GetRepository<Round>();
            Round? round = await roundRepo.Entities
                .FirstOrDefaultAsync(r => r.RoundId == roundId && r.DeletedAt == null);

            if (round == null) return;

            string finalized = RoundStatusEnum.Finalized.ToString();
            string closed = RoundStatusEnum.Closed.ToString();

            if (string.Equals(round.Status, finalized, StringComparison.OrdinalIgnoreCase))
                return;

            // Only finalize after the round has effectively ended
            bool roundEnded = string.Equals(round.Status, closed, StringComparison.OrdinalIgnoreCase)
                              || DateTime.UtcNow >= round.End;
            if (!roundEnded) return;

            if (!await IsRoundWorkDoneAsync(unitOfWork, round.RoundId))
                return;

            round.Status = finalized;
            await roundRepo.UpdateAsync(round);
            await unitOfWork.SaveAsync();
        }

        private static async Task<bool> IsRoundWorkDoneAsync(IUOW unitOfWork, Guid roundId)
        {
            var submissionRepo = unitOfWork.GetRepository<Submission>();
            var appealRepo = unitOfWork.GetRepository<Appeal>();

            bool hasPendingSubs = await submissionRepo.Entities
                .AsNoTracking()
                .AnyAsync(s => s.DeletedAt == null
                               && s.Problem != null
                               && s.Problem.RoundId == roundId
                               && s.Status == SubmissionStatusEnum.Pending.ToString());
            if (hasPendingSubs) return false;

            bool hasOpenAppeals = await appealRepo.Entities
                .AsNoTracking()
                .AnyAsync(a => a.DeletedAt == null
                               && a.TargetId == roundId
                               && (a.State != AppealStateEnum.Closed.ToString()
                                   || a.Decision == AppealDecisionEnum.Pending.ToString()));

            return !hasOpenAppeals;
        }
    }
}
