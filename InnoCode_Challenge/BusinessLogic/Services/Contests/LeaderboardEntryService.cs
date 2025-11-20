using AutoMapper;
using BusinessLogic.IServices.Contests;
using DataAccess.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Repository.DTOs.LeaderboardEntryDTOs;
using Repository.IRepositories;
using Repository.Repositories;
using System.Security.Claims;
using System.Text.Json;
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
        private readonly IHttpContextAccessor _httpContextAccessor;

        // Constructor
        public LeaderboardEntryService(IMapper mapper, IUOW uow, ILeaderboardRealtimeService realtimeService, IHttpContextAccessor httpContextAccessor)
        {
            _mapper = mapper;
            _unitOfWork = uow;
            _realtimeService = realtimeService;
            _httpContextAccessor = httpContextAccessor;
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

        public async Task ApplyEliminationAsync(Guid contestId, Guid roundId)
        {
            try
            {
                _unitOfWork.BeginTransaction();

                IGenericRepository<Contest> contestRepo = _unitOfWork.GetRepository<Contest>();
                IGenericRepository<Round> roundRepo = _unitOfWork.GetRepository<Round>();
                IGenericRepository<LeaderboardEntry> leaderboardRepo = _unitOfWork.GetRepository<LeaderboardEntry>();
                IGenericRepository<Team> teamRepo = _unitOfWork.GetRepository<Team>();   

                // Validate contest
                Contest? contest = await contestRepo.Entities
                    .FirstOrDefaultAsync(c => c.ContestId == contestId && c.DeletedAt == null);

                if (contest == null)
                    throw new ErrorException(StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        "Contest not found.");

                // Validate round and ensure it belongs to this contest
                Round? round = await roundRepo.Entities
                    .FirstOrDefaultAsync(r => r.RoundId == roundId && r.DeletedAt == null);

                if (round == null || round.ContestId != contestId)
                    throw new ErrorException(StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        "Round not found in this contest.");

                // Determine round index (1-based) by Start time ordering
                List<Round> rounds = await roundRepo.Entities
                    .Where(r => r.ContestId == contestId && !r.DeletedAt.HasValue)
                    .OrderBy(r => r.Start)
                    .ThenBy(r => r.RoundId)
                    .ToListAsync();

                int roundIndex = rounds.FindIndex(r => r.RoundId == roundId) + 1;
                if (roundIndex <= 0)
                    throw new ErrorException(StatusCodes.Status500InternalServerError,
                        ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                        "Unable to determine round index for elimination.");

                // Get elimination rule
                Dictionary<int, int>? rules = await GetEliminationRuleAsync(contestId);
                if (rules == null || !rules.TryGetValue(roundIndex, out int topN) || topN <= 0)
                {
                    // no elimination rule for this round => do nothing
                    _unitOfWork.CommitTransaction();
                    return;
                }

                // Get current leaderboard ordered by rank
                List<LeaderboardEntry> entries = await leaderboardRepo.Entities
                    .Where(e => e.ContestId == contestId)
                    .OrderBy(e => e.Rank)
                    .ThenByDescending(e => e.Score)
                    .ToListAsync();

                if (!entries.Any())
                {
                    throw new ErrorException(StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        "No leaderboard entries found for this contest.");
                }

                // Determine survivors vs eliminated
                var survivors = entries
                    .Take(topN)
                    .Select(e => e.TeamId)
                    .Distinct()
                    .ToHashSet();

                var toEliminateIds = entries
                    .Skip(topN)
                    .Select(e => e.TeamId)
                    .Distinct()
                    .ToList();


                DateTime now = DateTime.UtcNow;

                if (toEliminateIds.Any())
                {
                    var teamsToEliminate = await teamRepo.Entities
                        .Where(t => t.ContestId == contestId
                                    && toEliminateIds.Contains(t.TeamId)
                                    && t.DeletedAt == null)
                        .ToListAsync();

                    foreach (var team in teamsToEliminate)
                    {
                        if (!string.Equals(team.Status, TeamStatusConstants.Eliminated, StringComparison.OrdinalIgnoreCase))
                        {
                            team.Status = TeamStatusConstants.Eliminated;
                            await teamRepo.UpdateAsync(team);
                        }
                    }
                }

                await _unitOfWork.SaveAsync();
                _unitOfWork.CommitTransaction();
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
                    $"Error applying elimination: {ex.Message}");
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

        public async Task<GetLeaderboardEntryDTO> GetLeaderboardAsync(int pageNumber, int pageSize, Guid contestIdSearch)
        {
            try
            {
                // Validate pageNumber and pageSize
                if (pageNumber < 1 || pageSize < 1)
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Page number and page size must be greater than or equal to 1.");
                }

                // Get current user information
                string? userIdClaim = _httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
                string? userRole = _httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.Role);

                Guid? currentUserId = null;
                if (Guid.TryParse(userIdClaim, out Guid parsedUserId))
                {
                    currentUserId = parsedUserId;
                }

                // Get repositories
                IGenericRepository<LeaderboardEntry> leaderboardRepo = _unitOfWork.GetRepository<LeaderboardEntry>();
                IGenericRepository<TeamMember> teamMemberRepo = _unitOfWork.GetRepository<TeamMember>();
                IGenericRepository<McqAttempt> mcqAttemptRepo = _unitOfWork.GetRepository<McqAttempt>();
                IGenericRepository<Submission> submissionRepo = _unitOfWork.GetRepository<Submission>();
                IGenericRepository<Round> roundRepo = _unitOfWork.GetRepository<Round>();
                IGenericRepository<Config> configRepo = _unitOfWork.GetRepository<Config>();
                IGenericRepository<Student> studentRepo = _unitOfWork.GetRepository<Student>();
                IGenericRepository<Mentor> mentorRepo = _unitOfWork.GetRepository<Mentor>();
                IGenericRepository<Team> teamRepo = _unitOfWork.GetRepository<Team>();

                // Determine user's team if they're a student or mentor
                Guid? userTeamId = null;
                if (currentUserId.HasValue)
                {
                    if (userRole == RoleConstants.Student)
                    {
                        Student? student = await studentRepo.Entities
                            .FirstOrDefaultAsync(s => s.UserId == currentUserId.Value && s.DeletedAt == null);

                        if (student != null)
                        {
                            TeamMember? teamMember = await teamMemberRepo.Entities
                                .Include(tm => tm.Team)
                                .FirstOrDefaultAsync(tm => tm.StudentId == student.StudentId
                                                          && tm.Team.ContestId == contestIdSearch
                                                          && tm.Team.DeletedAt == null);
                            userTeamId = teamMember?.TeamId;
                        }
                    }
                    else if (userRole == RoleConstants.Mentor)
                    {
                        Mentor? mentor = await mentorRepo.Entities
                            .FirstOrDefaultAsync(m => m.UserId == currentUserId.Value && m.DeletedAt == null);

                        if (mentor != null)
                        {
                            Team? team = await teamRepo.Entities
                                .FirstOrDefaultAsync(t => t.MentorId == mentor.MentorId
                                                         && t.ContestId == contestIdSearch
                                                         && t.DeletedAt == null);
                            userTeamId = team?.TeamId;
                        }
                    }
                }

                // Get all leaderboard entries for the contest
                List<LeaderboardEntry> allEntries = await leaderboardRepo.Entities
                    .Where(l => l.ContestId == contestIdSearch)
                    .Include(l => l.Contest)
                    .Include(l => l.Team)
                    .OrderBy(l => l.Rank)
                    .ToListAsync();

                if (!allEntries.Any())
                {
                    throw new ErrorException(StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        "No leaderboard entries found for the specified contest.");
                }

                // Get the first entry to extract contest information
                LeaderboardEntry firstEntry = allEntries.First();

                // Map to DTO
                GetLeaderboardEntryDTO dto = _mapper.Map<GetLeaderboardEntryDTO>(firstEntry);

                // Create team info list from all entries
                var allTeams = allEntries.Select(entry => new TeamInfo
                {
                    TeamId = entry.TeamId,
                    TeamName = entry.Team.Name,
                    Rank = entry.Rank ?? 0,
                    Score = entry.Score ?? 0,
                    Members = new List<MemberInfo>()
                }).ToList();

                // Set total team count
                int totalTeamCount = allTeams.Count;

                // Apply pagination to teams
                List<TeamInfo> paginatedTeams = allTeams
                    .Skip((pageNumber - 1) * pageSize)
                    .Take(pageSize)
                    .ToList();

                // Determine if should show all members or only user's team members
                bool showAllMembers = userRole != RoleConstants.Student && userRole != RoleConstants.Mentor;

                // Get all rounds for this contest
                List<Round> rounds = await roundRepo.Entities
                    .Where(r => r.ContestId == contestIdSearch && !r.DeletedAt.HasValue)
                    .OrderBy(r => r.Start)
                    .ToListAsync();

                // Populate member details for paginated teams only
                foreach (var teamData in paginatedTeams)
                {
                    bool isUserTeam = userTeamId.HasValue && userTeamId.Value == teamData.TeamId;

                    // If user is student/mentor and this is not their team, skip member details
                    if (!showAllMembers && !isUserTeam)
                    {
                        continue;
                    }

                    // Get team members with their student and user information
                    List<TeamMember> teamMembers = await teamMemberRepo.Entities
                        .Include(tm => tm.Student)
                            .ThenInclude(s => s.User)
                        .Where(tm => tm.TeamId == teamData.TeamId)
                        .ToListAsync();

                    // Process each member
                    foreach (TeamMember teamMember in teamMembers)
                    {
                        MemberInfo memberInfo = new MemberInfo
                        {
                            MemberId = teamMember.StudentId,
                            MemberName = teamMember.Student.User.Fullname,
                            MemberRole = teamMember.MemberRole,
                            TotalScore = 0,
                            RoundScores = new List<RoundScoreDetail>()
                        };

                        // Calculate scores for each round
                        foreach (Round round in rounds)
                        {
                            double roundScore = 0;
                            string roundType = string.Empty;
                            DateTime? completedAt = null;

                            // Get key
                            string key = ConfigKeys.RoundStudent(round.RoundId, teamMember.StudentId);
                            Config? config = await configRepo.GetByIdAsync(key);

                            // Skip if this student hasn't finished this round
                            if (config == null || config.DeletedAt != null)
                            {
                                continue;
                            }

                            // Check if round has MCQ test
                            McqAttempt? mcqAttempt = await mcqAttemptRepo.Entities
                                .Where(ma => ma.RoundId == round.RoundId
                                            && ma.StudentId == teamMember.StudentId
                                            && ma.End.HasValue)
                                .OrderByDescending(ma => ma.End)
                                .FirstOrDefaultAsync();

                            if (mcqAttempt != null)
                            {
                                roundScore = mcqAttempt.Score ?? 0;
                                roundType = ProblemTypeEnum.McqTest.ToString();
                                completedAt = config.UpdatedAt;
                            }
                            else
                            {
                                // Check if round has Problem submission
                                Submission? submission = await submissionRepo.Entities
                                    .Include(s => s.Problem)
                                    .Where(s => s.Problem.RoundId == round.RoundId
                                               && s.SubmittedByStudentId == teamMember.StudentId
                                               && s.TeamId == teamData.TeamId
                                               && s.Status == SubmissionStatusEnum.Finished.ToString())
                                    .OrderByDescending(s => s.CreatedAt)
                                    .FirstOrDefaultAsync();

                                if (submission != null)
                                {
                                    roundScore = submission.Score;
                                    roundType = submission.Problem.Type ?? "Unknown Type";
                                    completedAt = config.UpdatedAt;
                                }
                            }

                            // Add round score detail
                            memberInfo.RoundScores.Add(new RoundScoreDetail
                            {
                                RoundId = round.RoundId,
                                RoundName = round.Name,
                                Score = roundScore,
                                RoundType = roundType,
                                CompletedAt = completedAt
                            });

                            // Add to total score
                            memberInfo.TotalScore += roundScore;
                        }

                        // Add member info to team
                        teamData.Members.Add(memberInfo);
                    }
                }

                // Set the paginated team list and total count
                dto.teamIdList = paginatedTeams;
                dto.TotalTeamCount = totalTeamCount;

                return dto;
            }
            catch (Exception ex)
            {
                if (ex is ErrorException)
                {
                    throw;
                }

                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error retrieving leaderboard: {ex.Message}");
            }
        }

        public async Task UpdateTeamScoreAsync(Guid contestId, Guid teamId, double newScore)
        {
            try
            {
                // Validate that the leaderboard is not frozen
                await ValidateLeaderboardNotFrozenAsync(contestId);
                await ValidateTeamNotEliminatedAsync(contestId, teamId);

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
                await ValidateTeamNotEliminatedAsync(contestId, teamId);

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
        private async Task ValidateTeamNotEliminatedAsync(Guid contestId, Guid teamId)
        {
            var teamRepo = _unitOfWork.GetRepository<Team>();

            var team = await teamRepo.Entities
                .FirstOrDefaultAsync(t => t.TeamId == teamId
                                          && t.ContestId == contestId
                                          && t.DeletedAt == null);

            if (team == null)
            {
                throw new ErrorException(StatusCodes.Status404NotFound,
                    ResponseCodeConstants.NOT_FOUND,
                    $"Team {teamId} not found in contest {contestId}");
            }

            if (string.Equals(team.Status, TeamStatusConstants.Eliminated, StringComparison.OrdinalIgnoreCase))
            {
                throw new ErrorException(StatusCodes.Status403Forbidden,
                    ResponseCodeConstants.FORBIDDEN,
                    "This team has been eliminated and cannot receive new scores.");
            }
        }

        private async Task<Dictionary<int, int>?> GetEliminationRuleAsync(Guid contestId)
        {
            IGenericRepository<Config> configRepo = _unitOfWork.GetRepository<Config>();

            string key = ConfigKeys.ContestPolicy(contestId, ContestPolicyKeys.EliminationRule);

            string? raw = await configRepo.Entities
                .Where(c => c.Key == key && c.Scope == "contest" && c.DeletedAt == null)
                .Select(c => c.Value)
                .FirstOrDefaultAsync();

            if (string.IsNullOrWhiteSpace(raw))
                return null;

            try
            {
                var dictString = JsonSerializer.Deserialize<Dictionary<string, int>>(raw!);
                if (dictString == null || dictString.Count == 0)
                    return null;

                var result = new Dictionary<int, int>();
                foreach (var kv in dictString)
                {
                    if (int.TryParse(kv.Key, out int roundIndex) && kv.Value > 0)
                    {
                        result[roundIndex] = kv.Value;
                    }
                }

                return result.Count > 0 ? result : null;
            }
            catch
            {
                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    "INVALID_ELIMINATION_RULE",
                    "Elimination rule policy is not a valid JSON map of roundIndex -> topN.");
            }
        }
    }
}
