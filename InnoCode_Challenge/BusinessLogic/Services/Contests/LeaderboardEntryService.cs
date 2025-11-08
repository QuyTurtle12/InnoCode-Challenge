using AutoMapper;
using BusinessLogic.IServices.Contests;
using DataAccess.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Repository.DTOs.LeaderboardEntryDTOs;
using Repository.IRepositories;
using Utility.Constant;
using Utility.Enums;
using Utility.ExceptionCustom;
using Utility.PaginatedList;

namespace BusinessLogic.Services.Contests
{
    public class LeaderboardEntryService : ILeaderboardEntryService
    {
        private readonly IMapper _mapper;
        private readonly IUOW _unitOfWork;
        private readonly ILeaderboardRealtimeService _realtimeService;

        // Constructor
        public LeaderboardEntryService(IMapper mapper, IUOW uow, ILeaderboardRealtimeService realtimeService)
        {
            _mapper = mapper;
            _unitOfWork = uow;
            _realtimeService = realtimeService;
        }

        public async Task<string> ToggleLeaderboardFreezeAsync(Guid contestId)
        {
            try
            {
                // Start a new transaction
                _unitOfWork.BeginTransaction();

                // Get the repository for Contest
                IGenericRepository<Contest> contestRepo = _unitOfWork.GetRepository<Contest>();

                // Retrieve the contest
                Contest? contest = await contestRepo.Entities
                    .FirstOrDefaultAsync(c => c.ContestId == contestId && c.DeletedAt == null);

                // Validate contest existence
                if (contest == null)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        $"Contest with ID {contestId} not found");
                }

                string newStatus;

                // Toggle between Ongoing and Paused
                if (contest.Status == ContestStatusEnum.Ongoing.ToString())
                {
                    // Freeze: Ongoing -> Paused
                    newStatus = ContestStatusEnum.Paused.ToString();
                }
                else if (contest.Status == ContestStatusEnum.Paused.ToString())
                {
                    DateTime now = DateTime.UtcNow;

                    // Verify contest time is still valid
                    if (contest.End.HasValue && now >= contest.End.Value)
                    {
                        throw new ErrorException(StatusCodes.Status400BadRequest,
                            ResponseCodeConstants.BADREQUEST,
                            "Cannot unfreeze. Contest has already ended.");
                    }

                    if (contest.Start.HasValue && now < contest.Start.Value)
                    {
                        throw new ErrorException(StatusCodes.Status400BadRequest,
                            ResponseCodeConstants.BADREQUEST,
                            "Cannot unfreeze. Contest has not started yet.");
                    }

                    // Unfreeze: Paused -> Ongoing
                    newStatus = ContestStatusEnum.Ongoing.ToString();
                }
                else
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest,
                        ResponseCodeConstants.BADREQUEST,
                        $"Cannot toggle freeze status. Contest must be Ongoing or Paused. Current status: {contest.Status}");
                }

                // Update contest status
                contest.Status = newStatus;
                await contestRepo.UpdateAsync(contest);
                await _unitOfWork.SaveAsync();

                // Commit the transaction
                _unitOfWork.CommitTransaction();

                return newStatus;
            }
            catch (Exception ex)
            {
                // If something fails, roll back the transaction
                _unitOfWork.RollBack();

                if (ex is ErrorException)
                {
                    throw;
                }

                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error toggling leaderboard freeze status: {ex.Message}");
            }
        }

        private async Task ValidateLeaderboardNotFrozenAsync(Guid contestId)
        {
            IGenericRepository<Contest> contestRepo = _unitOfWork.GetRepository<Contest>();
            IGenericRepository<Round> roundRepo = _unitOfWork.GetRepository<Round>();

            // Get contest
            Contest? contest = await contestRepo.Entities
                .FirstOrDefaultAsync(c => c.ContestId == contestId && c.DeletedAt == null);

            // Validate contest existence
            if (contest == null)
            {
                throw new ErrorException(StatusCodes.Status404NotFound,
                    ResponseCodeConstants.NOT_FOUND,
                    $"Contest with ID {contestId} not found");
            }

            // Check if contest is completed
            if (contest.Status == ContestStatusEnum.Completed.ToString())
            {
                throw new ErrorException(StatusCodes.Status403Forbidden,
                    ResponseCodeConstants.FORBIDDEN,
                    $"Cannot update leaderboard. Contest {contest.Name} has been completed and the leaderboard is frozen.");
            }

            // Check if contest is paused
            if (contest.Status == ContestStatusEnum.Paused.ToString())
            {
                throw new ErrorException(StatusCodes.Status403Forbidden,
                    ResponseCodeConstants.FORBIDDEN,
                    $"Cannot update leaderboard. Contest {contest.Name} is paused and the leaderboard is temporarily frozen.");
            }

            // Verify all rounds have ended
            DateTime now = DateTime.UtcNow;
            List<Round> rounds = await roundRepo.Entities
                .Where(r => r.ContestId == contestId && !r.DeletedAt.HasValue)
                .ToListAsync();

            if (rounds.Any() && rounds.All(r => now >= r.End))
            {
                throw new ErrorException(StatusCodes.Status403Forbidden,
                    ResponseCodeConstants.FORBIDDEN,
                    $"Cannot update leaderboard. All rounds in contest {contest.Name} have ended and the leaderboard is frozen.");
            }
        }

        public async Task AddTeamToLeaderboardAsync(Guid contestId, Guid teamId)
        {
            try
            {
                // Validate inputs
                if (contestId == Guid.Empty)
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest,
                        ResponseCodeConstants.BADREQUEST,
                        "Contest ID cannot be empty.");
                }

                if (teamId == Guid.Empty)
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest,
                        ResponseCodeConstants.BADREQUEST,
                        "Team ID cannot be empty.");
                }

                // Get repositories
                IGenericRepository<Contest> contestRepo = _unitOfWork.GetRepository<Contest>();
                IGenericRepository<Team> teamRepo = _unitOfWork.GetRepository<Team>();
                IGenericRepository<LeaderboardEntry> leaderboardRepo = _unitOfWork.GetRepository<LeaderboardEntry>();

                // Verify contest exists
                Contest? contest = await contestRepo.GetByIdAsync(contestId);

                if (contest == null || contest.DeletedAt.HasValue)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        "Contest not found.");
                }

                // Verify team exists and belongs to the contest
                Team? team = await teamRepo.Entities
                    .FirstOrDefaultAsync(t => t.TeamId == teamId && t.ContestId == contestId && t.DeletedAt == null);

                if (team == null)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        "Team not found or does not belong to this contest.");
                }

                // Check if team already exists in leaderboard
                bool exists = await leaderboardRepo.Entities
                    .AnyAsync(l => l.ContestId == contestId && l.TeamId == teamId);

                if (exists)
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest,
                        ResponseCodeConstants.BADREQUEST,
                        $"Team {teamId} already exists in leaderboard for contest {contestId}");
                }

                // Get current last rank to determine new team's rank
                int? lastRank = await leaderboardRepo.Entities
                    .Where(l => l.ContestId == contestId)
                    .MaxAsync(l => l.Rank);

                int newRank = (lastRank ?? 0) + 1;

                // Create new leaderboard entry
                LeaderboardEntry newEntry = new LeaderboardEntry
                {
                    EntryId = Guid.NewGuid(),
                    ContestId = contestId,
                    TeamId = teamId,
                    Score = 0,
                    Rank = newRank,
                    SnapshotAt = DateTime.UtcNow
                };

                // Insert the entry
                await leaderboardRepo.InsertAsync(newEntry);

                // Save changes
                await _unitOfWork.SaveAsync();

            }
            catch (Exception ex)
            {
                if (ex is ErrorException)
                {
                    throw;
                }

                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error adding team to leaderboard: {ex.Message}");
            }
        }

        public async Task UpdateLeaderboardAsync(Guid contestId)
        {
            try
            {
                // Start a new transaction
                _unitOfWork.BeginTransaction();

                // Get the repository for LeaderboardEntry
                IGenericRepository<LeaderboardEntry> leaderboardRepo = _unitOfWork.GetRepository<LeaderboardEntry>();

                // Get all leaderboard entries for the specified contest with Team info
                IList<LeaderboardEntry> entries = leaderboardRepo.Entities
                    .Include(e => e.Team)
                    .Where(e => e.ContestId == contestId)
                    .OrderByDescending(e => e.Score)  // Order by score in descending order
                    .ToList();

                // If no entries found, throw an exception
                if (!entries.Any())
                {
                    throw new ErrorException(StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        $"No leaderboard entries found for contest ID: {contestId}");
                }

                // Generate a shared snapshot time for all entries
                DateTime sharedSnapshot = DateTime.UtcNow;

                // Collect team info for broadcasting
                List<TeamInfo> teamInfoList = new List<TeamInfo>();

                // Update ranks based on the ordered scores
                int currentRank = 1;
                foreach (LeaderboardEntry entry in entries)
                {
                    entry.Rank = currentRank;
                    entry.SnapshotAt = sharedSnapshot;
                    await leaderboardRepo.UpdateAsync(entry);

                    // Collect team info
                    teamInfoList.Add(new TeamInfo
                    {
                        TeamId = entry.TeamId,
                        TeamName = entry.Team?.Name ?? "Unknown",
                        Rank = currentRank,
                        Score = entry.Score ?? 0
                    });

                    currentRank++;
                }

                // Save changes to the database
                await _unitOfWork.SaveAsync();

                // Commit the transaction if all operations succeed
                _unitOfWork.CommitTransaction();

                // Broadcast real-time leaderboard update
                await _realtimeService.BroadcastLeaderboardUpdateAsync(contestId, teamInfoList);
            }
            catch (Exception ex)
            {
                // If something fails, roll back the transaction
                _unitOfWork.RollBack();

                if (ex is ErrorException)
                {
                    throw;
                }

                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error updating leaderboard rankings: {ex.Message}");
            }
        }

        public async Task<PaginatedList<GetLeaderboardEntryDTO>> GetPaginatedLeaderboardAsync(int pageNumber, int pageSize, Guid? idSearch, Guid? contestIdSearch, string? contestNameSearch)
        {
            try
            {
                // Validate pageNumber and pageSize
                if (pageNumber < 1 || pageSize < 1)
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Page number and page size must be greater than or equal to 1.");
                }

                // Get the repository for LeaderboardEntry
                IGenericRepository<LeaderboardEntry> leaderboardRepo = _unitOfWork.GetRepository<LeaderboardEntry>();

                // Start with base query
                IQueryable<LeaderboardEntry> query = leaderboardRepo
                    .Entities
                    .Include(l => l.Contest)
                    .Include(l => l.Team);

                // Apply filters if provided
                if (idSearch.HasValue)
                {
                    query = query.Where(l => l.EntryId == idSearch.Value);
                }

                if (contestIdSearch.HasValue)
                {
                    query = query.Where(l => l.ContestId == contestIdSearch.Value);
                }

                if (!string.IsNullOrEmpty(contestNameSearch))
                {
                    query = query.Where(l => l.Contest.Name.Contains(contestNameSearch));
                }

                // Order by rank 
                query = query.OrderBy(l => l.Rank);

                // Execute query to get all matching entries
                List<LeaderboardEntry> allEntries = await query.ToListAsync();

                // Group entries by ContestId
                var groupedEntries = allEntries
                    .GroupBy(entry => entry.ContestId)
                    .Select(group =>
                    {
                        // Get first entry to extract common information
                        LeaderboardEntry firstEntry = group.First();

                        // Map to DTO
                        GetLeaderboardEntryDTO dto = _mapper.Map<GetLeaderboardEntryDTO>(firstEntry);

                        // Create the team list with teams ordered by rank
                        dto.teamIdList = group.OrderBy(e => e.Rank).Select(entry => new TeamInfo
                        {
                            TeamId = entry.TeamId,
                            TeamName = entry.Team.Name,
                            Rank = entry.Rank ?? 0,
                            Score = entry.Score ?? 0
                        }).ToList();

                        return dto;
                    })
                    .ToList();

                // Apply pagination to the grouped results
                int totalCount = groupedEntries.Count;
                var paginatedGroups = groupedEntries
                    .Skip((pageNumber - 1) * pageSize)
                    .Take(pageSize)
                    .ToList();

                // Create and return a new paginated list with the DTOs
                return new PaginatedList<GetLeaderboardEntryDTO>(
                    paginatedGroups,
                    totalCount,
                    pageNumber,
                    pageSize
                );
            }
            catch (Exception ex)
            {
                if (ex is ErrorException)
                {
                    throw;
                }

                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error retrieving grouped leaderboard: {ex.Message}");
            }
        }

        public async Task UpdateTeamScoreAsync(Guid contestId, Guid teamId, double newScore)
        {
            try
            {
                // Validate that the leaderboard is not frozen
                await ValidateLeaderboardNotFrozenAsync(contestId);

                _unitOfWork.BeginTransaction();

                IGenericRepository<LeaderboardEntry> leaderboardRepo = _unitOfWork.GetRepository<LeaderboardEntry>();

                // Find the team's leaderboard entry
                LeaderboardEntry? entry = await leaderboardRepo.Entities
                    .Include(e => e.Team)
                    .FirstOrDefaultAsync(e => e.ContestId == contestId && e.TeamId == teamId);

                if (entry == null)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        $"Leaderboard entry not found for team {teamId} in contest {contestId}");
                }

                // Update score
                entry.Score = newScore;
                entry.SnapshotAt = DateTime.UtcNow;
                await leaderboardRepo.UpdateAsync(entry);
                await _unitOfWork.SaveAsync();

                _unitOfWork.CommitTransaction();

                // Recalculate ranks and broadcast full leaderboard update
                await RecalculateRanksAsync(contestId);
            }
            catch (Exception ex)
            {
                _unitOfWork.RollBack();

                if (ex is ErrorException)
                {
                    throw;
                }

                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error updating team score: {ex.Message}");
            }
        }

        public async Task AddScoreToTeamAsync(Guid contestId, Guid teamId, double scoreToAdd)
        {
            try
            {
                // Validate that the leaderboard is not frozen
                await ValidateLeaderboardNotFrozenAsync(contestId);

                // Get the repository for LeaderboardEntry
                IGenericRepository<LeaderboardEntry> leaderboardRepo = _unitOfWork.GetRepository<LeaderboardEntry>();

                // Find the team's leaderboard entry
                LeaderboardEntry? entry = await leaderboardRepo.Entities
                    .FirstOrDefaultAsync(e => e.ContestId == contestId && e.TeamId == teamId);

                // If entry not found, throw an exception
                if (entry == null)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        $"Leaderboard entry not found for team {teamId} in contest {contestId}");
                }

                // Add to existing score
                entry.Score = (entry.Score ?? 0) + scoreToAdd;
                entry.SnapshotAt = DateTime.UtcNow;

                // Update the entry in the repository
                leaderboardRepo.Update(entry);
                await _unitOfWork.SaveAsync();

                // Recalculate ranks and broadcast
                await RecalculateRanksAsync(contestId);
            }
            catch (Exception ex)
            {
                if (ex is ErrorException)
                {
                    throw;
                }

                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error adding score to team: {ex.Message}");
            }
        }

        public async Task RecalculateRanksAsync(Guid contestId)
        {
            try
            {
                IGenericRepository<LeaderboardEntry> leaderboardRepo = _unitOfWork.GetRepository<LeaderboardEntry>();

                // Get all leaderboard entries for the contest ordered by score
                List<LeaderboardEntry> allEntries = await leaderboardRepo.Entities
                    .Include(e => e.Team)
                    .Where(e => e.ContestId == contestId)
                    .OrderByDescending(e => e.Score)
                    .ToListAsync();

                // Validate that entries exist
                if (!allEntries.Any())
                {
                    throw new ErrorException(StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        $"No leaderboard entries found for contest ID: {contestId}");
                }

                // Generate shared snapshot time
                DateTime sharedSnapshot = DateTime.UtcNow;

                int rank = 1;
                List<TeamInfo> teamInfoList = new List<TeamInfo>();

                // Update ranks and prepare team info for broadcasting
                foreach (LeaderboardEntry e in allEntries)
                {
                    e.Rank = rank;
                    e.SnapshotAt = sharedSnapshot;
                    await leaderboardRepo.UpdateAsync(e);

                    teamInfoList.Add(new TeamInfo
                    {
                        TeamId = e.TeamId,
                        TeamName = e.Team?.Name ?? "Unknown",
                        Rank = rank,
                        Score = e.Score ?? 0
                    });

                    rank++;
                }

                await _unitOfWork.SaveAsync();

                // Broadcast the updated leaderboard in real-time
                await _realtimeService.BroadcastLeaderboardUpdateAsync(contestId, teamInfoList);
            }
            catch (Exception ex)
            {
                if (ex is ErrorException)
                {
                    throw;
                }

                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error recalculating ranks: {ex.Message}");
            }
        }
    }
}
