using AutoMapper;
using BusinessLogic.IServices.Contests;
using DataAccess.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Repository.DTOs.LeaderboardEntryDTOs;
using Repository.IRepositories;
using Utility.Constant;
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

        public async Task CreateLeaderboardAsync(CreateLeaderboardEntryDTO leaderboardDTO)
        {
            try
            {
                // Start a new transaction
                _unitOfWork.BeginTransaction();

                // Generate a shared info for all entries
                Guid sharedGuid = Guid.NewGuid();
                DateTime sharedSnapshot = DateTime.UtcNow;

                // Initialize count based on the number of teams for ranking assignment
                int count = leaderboardDTO.teamIdList.Count;

                // Get the repository for LeaderboardEntry
                IGenericRepository<LeaderboardEntry> leaderboardRepo = _unitOfWork.GetRepository<LeaderboardEntry>();

                List<TeamInfo> teamInfoList = new List<TeamInfo>();

                foreach (Guid item in leaderboardDTO.teamIdList)
                {
                    // Map DTO to entity
                    LeaderboardEntry leaderboardEntry = _mapper.Map<LeaderboardEntry>(leaderboardDTO);

                    // Assign the shared GUID
                    leaderboardEntry.EntryId = sharedGuid;

                    // Assign the TeamId from the list
                    leaderboardEntry.TeamId = item;

                    // Assign Rank based on the current count
                    leaderboardEntry.Rank = count;

                    // Initialize Score to 0
                    leaderboardEntry.Score = 0;

                    // Set the SnapshotAt to current time
                    leaderboardEntry.SnapshotAt = sharedSnapshot;

                    // Insert the new Leaderboard Entry
                    await leaderboardRepo.InsertAsync(leaderboardEntry);

                    // Collect team info for broadcasting
                    teamInfoList.Add(new TeamInfo
                    {
                        TeamId = item,
                        TeamName = string.Empty, // Will be populated from database
                        Rank = count,
                        Score = 0
                    });

                    // Decrement the count for the next rank
                    count--;
                }

                // Save changes to the database
                await _unitOfWork.SaveAsync();

                // Commit the transaction if all operations succeed
                _unitOfWork.CommitTransaction();

                // Broadcast real-time update
                await _realtimeService.BroadcastLeaderboardUpdateAsync(leaderboardDTO.ContestId, teamInfoList);
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
                    $"Error creating Leaderboard: {ex.Message}");
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
                // Start a new transaction
                _unitOfWork.BeginTransaction();

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

                // Commit the transaction
                _unitOfWork.CommitTransaction();

                // Recalculate ranks and broadcast
                await RecalculateRanksAsync(contestId);
            }
            catch (Exception ex)
            {
                // Roll back the transaction in case of error
                _unitOfWork.RollBack();

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
                _unitOfWork.BeginTransaction();

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
                _unitOfWork.CommitTransaction();

                // Broadcast the updated leaderboard in real-time
                await _realtimeService.BroadcastLeaderboardUpdateAsync(contestId, teamInfoList);
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
                    $"Error recalculating ranks: {ex.Message}");
            }
        }
    }
}
