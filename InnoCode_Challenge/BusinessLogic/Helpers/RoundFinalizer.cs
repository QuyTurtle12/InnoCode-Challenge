using DataAccess.Entities;
using Microsoft.EntityFrameworkCore;
using Repository.IRepositories;
using Utility.Constant;
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

            await ApplyRankCutoffAsync(unitOfWork, round);
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

        private static async Task ApplyRankCutoffAsync(IUOW unitOfWork, Round round)
        {
            var roundRepo = unitOfWork.GetRepository<Round>();

            // Determine which round's cutoff we should use
            Guid cutoffRoundId = round.RoundId;
            if (round.IsRetakeRound && round.MainRoundId.HasValue)
            {
                cutoffRoundId = round.MainRoundId.Value;
            }

            // If this is a main round with a retake that is not finalized yet -> skip cutoff now
            if (!round.IsRetakeRound)
            {
                Round? retake = await roundRepo.Entities
                    .AsNoTracking()
                    .FirstOrDefaultAsync(r => r.MainRoundId == round.RoundId
                                              && r.IsRetakeRound
                                              && r.DeletedAt == null);
                if (retake != null && !string.Equals(retake.Status, RoundStatusEnum.Finalized.ToString(), StringComparison.OrdinalIgnoreCase))
                {
                    return; // wait for retake finalize
                }
            }

            var configRepo = unitOfWork.GetRepository<Config>();
            string rcKey = ConfigKeys.RoundRankCutoff(cutoffRoundId);
            string? value = await configRepo.Entities
                .Where(c => c.Key == rcKey && c.Scope == "contest" && c.DeletedAt == null)
                .Select(c => c.Value)
                .FirstOrDefaultAsync();

            if (string.IsNullOrWhiteSpace(value) || !int.TryParse(value, out int cutoff) || cutoff <= 0)
                return; // disabled

            var leaderboardRepo = unitOfWork.GetRepository<LeaderboardEntry>();
            var teamRepo = unitOfWork.GetRepository<Team>();

            var entries = await leaderboardRepo.Entities
                .Where(e => e.ContestId == round.ContestId)
                .OrderByDescending(e => e.Score)
                .ThenBy(e => e.SnapshotAt)
                .ToListAsync();

            if (!entries.Any()) return;

            var toEliminate = entries.Skip(cutoff).Select(e => e.TeamId).ToList();
            if (!toEliminate.Any()) return;

            var teams = await teamRepo.Entities
                .Where(t => toEliminate.Contains(t.TeamId) && t.DeletedAt == null)
                .ToListAsync();

            foreach (var team in teams)
            {
                team.Status = TeamStatusConstants.Eliminated;
                await teamRepo.UpdateAsync(team);
            }

            await unitOfWork.SaveAsync();
        }
    }
}
