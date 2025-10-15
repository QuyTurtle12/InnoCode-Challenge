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

        // Constructor
        public LeaderboardEntryService(IMapper mapper, IUOW uow)
        {
            _mapper = mapper;
            _unitOfWork = uow;
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

                    // Decrement the count for the next rank
                    count--;
                }

                // Save changes to the database
                await _unitOfWork.SaveAsync();

                // Commit the transaction if all operations succeed
                _unitOfWork.CommitTransaction();
            }
            catch (Exception ex)
            {
                // If something fails, roll back the transaction
                _unitOfWork.RollBack();
                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error creating Leaderboard: {ex.Message}");
            }
        }

        public async Task UpdateLeaderboardAsync(UpdateLeaderboardEntryDTO leaderboardDTO)
        {
            try
            {
                // Start a new transaction
                _unitOfWork.BeginTransaction();

                // Get the repository for LeaderboardEntry
                IGenericRepository<LeaderboardEntry> leaderboardRepo = _unitOfWork.GetRepository<LeaderboardEntry>();

                // Get all leaderboard entries for the specified contest
                IList<LeaderboardEntry> entries = leaderboardRepo.Entities
                    .Where(e => e.ContestId == leaderboardDTO.ContestId)
                    .OrderByDescending(e => e.Score)  // Order by score in descending order
                    .ToList();

                // If no entries found, throw an exception
                if (!entries.Any())
                {
                    throw new ErrorException(StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        $"No leaderboard entries found for contest ID: {leaderboardDTO.ContestId}");
                }

                // Generate a shared snapshot time for all entries
                DateTime sharedSnapshot = DateTime.UtcNow;

                // Update ranks based on the ordered scores
                int currentRank = 1;
                foreach (LeaderboardEntry entry in entries)
                {
                    entry.Rank = currentRank++;
                    entry.SnapshotAt = sharedSnapshot;
                    await leaderboardRepo.UpdateAsync(entry);
                }

                // Save changes to the database
                await _unitOfWork.SaveAsync();

                // Commit the transaction if all operations succeed
                _unitOfWork.CommitTransaction();
            }
            catch (Exception ex)
            {
                // If something fails, roll back the transaction
                _unitOfWork.RollBack();
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
                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error retrieving grouped leaderboard: {ex.Message}");
            }
        }
    }
}
