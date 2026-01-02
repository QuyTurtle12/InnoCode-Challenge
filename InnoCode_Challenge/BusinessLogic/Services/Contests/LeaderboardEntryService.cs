using AutoMapper;
using BusinessLogic.IServices.Contests;
using DataAccess.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Repository.DTOs.LeaderboardEntryDTOs;
using Repository.IRepositories;
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

        public async Task<GetLeaderboardEntryDTO?> GetLeaderboardAsync(int pageNumber, int pageSize, Guid contestIdSearch)
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
                    return null;
                }

                // Get the first entry to extract contest information
                LeaderboardEntry firstEntry = allEntries.First();

                // Map to DTO
                GetLeaderboardEntryDTO dto = _mapper.Map<GetLeaderboardEntryDTO>(firstEntry);

                // Set snapshot time
                dto.SnapshotAt = firstEntry.SnapshotAt;

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
                bool showAllMembers = userRole != RoleConstants.Student && userRole != RoleConstants.Mentor && !string.IsNullOrWhiteSpace(userIdClaim);

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
                                            && ma.End.HasValue
                                            && ma.Status == McqAttemptStatusEnum.Finished.ToString())
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

        public async Task SetTeamScoreAsync(Guid contestId, Guid teamId, double newScore)
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

                // Update the score and snapshot time
                entry.Score = newScore;
                entry.SnapshotAt = DateTime.UtcNow;

                // Save changes
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

        public async Task RecalculateRanksAsync(Guid contestId)
        {
            try
            {
                // Get current user information for role-based filtering
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

                // Get all leaderboard entries for the contest
                List<LeaderboardEntry> allEntries = await leaderboardRepo.Entities
                    .Include(e => e.Team)
                    .Where(e => e.ContestId == contestId)
                    .ToListAsync();

                // Validate that entries exist
                if (!allEntries.Any())
                {
                    throw new ErrorException(StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        $"No leaderboard entries found for contest ID: {contestId}");
                }

                // Calculate earliest completion time for each team
                Dictionary<Guid, DateTime?> teamEarliestCompletionTime = new Dictionary<Guid, DateTime?>();

                foreach (var entry in allEntries)
                {
                    DateTime? earliestTime = await GetTeamEarliestCompletionTimeAsync(
                        contestId,
                        entry.TeamId,
                        teamMemberRepo,
                        roundRepo,
                        configRepo,
                        mcqAttemptRepo,
                        submissionRepo);

                    teamEarliestCompletionTime[entry.TeamId] = earliestTime;
                }

                // Order by Score desc, EarliestCompletionTime asc, TeamId asc
                allEntries = allEntries
                    .OrderByDescending(e => e.Score)
                    .ThenBy(e => teamEarliestCompletionTime[e.TeamId] ?? DateTime.MaxValue)
                    .ThenBy(e => e.TeamId)
                    .ToList();

                // Determine user's team if they're a student or mentor
                Guid? userTeamId = null;
                if (currentUserId.HasValue)
                {
                    if (userRole == RoleConstants.Student)
                    {
                        Student? student = await studentRepo.Entities
                            .Where(s => s.UserId == currentUserId.Value && s.DeletedAt == null)
                            .FirstOrDefaultAsync();

                        if (student != null)
                        {
                            TeamMember? teamMember = await teamMemberRepo.Entities
                                .Include(tm => tm.Team)
                                .Where(tm => tm.StudentId == student.StudentId
                                    && tm.Team.ContestId == contestId
                                    && tm.Team.DeletedAt == null)
                                .FirstOrDefaultAsync();
                            userTeamId = teamMember?.TeamId;
                        }
                    }
                    else if (userRole == RoleConstants.Mentor)
                    {
                        Mentor? mentor = await mentorRepo.Entities
                            .Where(m => m.UserId == currentUserId.Value && m.DeletedAt == null)
                            .FirstOrDefaultAsync();

                        if (mentor != null)
                        {
                            Team? team = await teamRepo.Entities
                                .Where(t => t.MentorId == mentor.MentorId
                                    && t.ContestId == contestId
                                    && t.DeletedAt == null)
                                .FirstOrDefaultAsync();
                            userTeamId = team?.TeamId;
                        }
                    }
                }

                // Determine if should show all members or only user's team members
                bool showAllMembers = userRole != RoleConstants.Student
                    && userRole != RoleConstants.Mentor
                    && !string.IsNullOrWhiteSpace(userIdClaim);

                // Get all rounds for this contest
                List<Round> rounds = await roundRepo.Entities
                    .Where(r => r.ContestId == contestId && !r.DeletedAt.HasValue)
                    .OrderBy(r => r.Start)
                    .ToListAsync();

                // Determine which teams need member details
                List<Guid> teamIdsToLoadMembers = new List<Guid>();
                if (showAllMembers)
                {
                    // Organizer: Load all teams
                    teamIdsToLoadMembers = allEntries.Select(e => e.TeamId).ToList();
                }
                else if (userTeamId.HasValue)
                {
                    // Student/Mentor: Load only their team
                    teamIdsToLoadMembers.Add(userTeamId.Value);
                }

                // Load all team members for relevant teams
                Dictionary<Guid, List<TeamMember>> membersByTeam = new Dictionary<Guid, List<TeamMember>>();
                if (teamIdsToLoadMembers.Any())
                {
                    List<TeamMember> allTeamMembers = await teamMemberRepo.Entities
                        .Include(tm => tm.Student)
                            .ThenInclude(s => s.User)
                        .Where(tm => teamIdsToLoadMembers.Contains(tm.TeamId))
                        .ToListAsync();

                    membersByTeam = allTeamMembers
                        .GroupBy(tm => tm.TeamId)
                        .ToDictionary(g => g.Key, g => g.ToList());
                }

                // Load all student IDs for config/attempt/submission lookups
                List<Guid> allStudentIds = membersByTeam.Values
                    .SelectMany(members => members.Select(m => m.StudentId))
                    .Distinct()
                    .ToList();

                // Load all configs for all students and rounds
                Dictionary<string, Config> configLookup = new Dictionary<string, Config>();
                if (allStudentIds.Any() && rounds.Any())
                {
                    List<string> configKeys = allStudentIds
                        .SelectMany(studentId => rounds.Select(round =>
                            ConfigKeys.RoundStudent(round.RoundId, studentId)))
                        .ToList();

                    List<Config> configs = await configRepo.Entities
                        .Where(c => configKeys.Contains(c.Key) && c.DeletedAt == null)
                        .ToListAsync();

                    configLookup = configs.ToDictionary(c => c.Key, c => c);
                }

                // Load all MCQ attempts for all students and rounds
                Dictionary<(Guid RoundId, Guid StudentId), McqAttempt> mcqAttemptLookup =
                    new Dictionary<(Guid, Guid), McqAttempt>();
                if (allStudentIds.Any() && rounds.Any())
                {
                    List<Guid> roundIds = rounds.Select(r => r.RoundId).ToList();

                    List<McqAttempt> mcqAttempts = await mcqAttemptRepo.Entities
                        .Where(ma => roundIds.Contains(ma.RoundId)
                                    && allStudentIds.Contains(ma.StudentId)
                                    && ma.End.HasValue
                                    && ma.Status == McqAttemptStatusEnum.Finished.ToString())
                        .ToListAsync();

                    // Group by (RoundId, StudentId) and take the latest attempt
                    mcqAttemptLookup = mcqAttempts
                        .GroupBy(ma => (ma.RoundId, ma.StudentId))
                        .ToDictionary(
                            g => g.Key,
                            g => g.OrderByDescending(ma => ma.End).First()
                        );
                }

                // Load all submissions for all students, rounds, and teams
                Dictionary<(Guid RoundId, Guid StudentId, Guid TeamId), Submission> submissionLookup =
                    new Dictionary<(Guid, Guid, Guid), Submission>();
                if (allStudentIds.Any() && rounds.Any())
                {
                    List<Guid> roundIds = rounds.Select(r => r.RoundId).ToList();
                    List<Guid> teamIds = teamIdsToLoadMembers;

                    List<Submission> submissions = await submissionRepo.Entities
                        .Include(s => s.Problem)
                        .Where(s => roundIds.Contains(s.Problem.RoundId)
                                   && allStudentIds.Contains(s.SubmittedByStudentId)
                                   && teamIds.Contains(s.TeamId)
                                   && s.Status == SubmissionStatusEnum.Finished.ToString())
                        .ToListAsync();

                    // Group by (RoundId, StudentId, TeamId) and take the latest submission
                    submissionLookup = submissions
                        .GroupBy(s => (s.Problem.RoundId, s.SubmittedByStudentId, s.TeamId))
                        .ToDictionary(
                            g => g.Key,
                            g => g.OrderByDescending(s => s.CreatedAt).First()
                        );
                }

                int currentRank = 1;
                List<TeamInfo> teamInfoList = new List<TeamInfo>();

                // Assign ranks and populate team details with members
                foreach (LeaderboardEntry e in allEntries)
                {
                    e.Rank = currentRank;
                    await leaderboardRepo.UpdateAsync(e);

                    TeamInfo teamInfo = new TeamInfo
                    {
                        TeamId = e.TeamId,
                        TeamName = e.Team?.Name ?? "Unknown",
                        Rank = currentRank,
                        Score = e.Score ?? 0,
                        Members = new List<MemberInfo>()
                    };

                    // Check if we should load members for this team
                    bool shouldLoadMembers = showAllMembers || (userTeamId.HasValue && userTeamId.Value == e.TeamId);

                    if (shouldLoadMembers && membersByTeam.TryGetValue(e.TeamId, out List<TeamMember>? teamMembers))
                    {
                        // Populate member details using batch-loaded data
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

                            // Calculate scores for each round using batch-loaded data
                            foreach (Round round in rounds)
                            {
                                double roundScore = 0;
                                string roundType = string.Empty;
                                DateTime? completedAt = null;

                                // Check config round finished using lookup
                                string configKey = ConfigKeys.RoundStudent(round.RoundId, teamMember.StudentId);
                                if (!configLookup.TryGetValue(configKey, out Config? config))
                                {
                                    // Student hasn't finished this round
                                    continue;
                                }

                                // Check MCQ attempt using lookup
                                if (mcqAttemptLookup.TryGetValue((round.RoundId, teamMember.StudentId), out McqAttempt? mcqAttempt))
                                {
                                    roundScore = mcqAttempt.Score ?? 0;
                                    roundType = ProblemTypeEnum.McqTest.ToString();
                                    completedAt = config.UpdatedAt;
                                }
                                else if (submissionLookup.TryGetValue((round.RoundId, teamMember.StudentId, e.TeamId), out Submission? submission))
                                {
                                    // Check submission using lookup
                                    roundScore = submission.Score;
                                    roundType = submission.Problem.Type ?? "Unknown Type";
                                    completedAt = config.UpdatedAt;
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

                            // Add member to team
                            teamInfo.Members.Add(memberInfo);
                        }
                    }

                    teamInfoList.Add(teamInfo);
                    currentRank++;
                }

                await _unitOfWork.SaveAsync();

                // Broadcast the updated leaderboard with full member details in real-time
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

        private async Task<DateTime?> GetTeamEarliestCompletionTimeAsync(
            Guid contestId,
            Guid teamId,
            IGenericRepository<TeamMember> teamMemberRepo,
            IGenericRepository<Round> roundRepo,
            IGenericRepository<Config> configRepo,
            IGenericRepository<McqAttempt> mcqAttemptRepo,
            IGenericRepository<Submission> submissionRepo)
        {
            // Get team members
            List<TeamMember> teamMembers = await teamMemberRepo.Entities
                .Where(tm => tm.TeamId == teamId)
                .ToListAsync();

            if (!teamMembers.Any())
                return null;

            // Get all rounds for contest
            List<Round> rounds = await roundRepo.Entities
                .Where(r => r.ContestId == contestId && !r.DeletedAt.HasValue)
                .OrderBy(r => r.Start)
                .ToListAsync();

            DateTime? latestCompletionTime = null;

            // Find the latest completion time across all members and rounds
            foreach (TeamMember member in teamMembers)
            {
                foreach (Round round in rounds)
                {
                    // Check if student finished this round using config
                    string configKey = ConfigKeys.RoundStudent(round.RoundId, member.StudentId);
                    Config? config = await configRepo.GetByIdAsync(configKey);

                    if (config == null || config.DeletedAt != null)
                        continue;

                    // Use the config UpdatedAt as the completion time
                    DateTime completionTime = config.UpdatedAt ?? DateTime.MaxValue;

                    // Track latest time to determine team's earliest completion
                    if (!latestCompletionTime.HasValue || completionTime > latestCompletionTime.Value)
                    {
                        latestCompletionTime = completionTime;
                    }
                }
            }

            return latestCompletionTime;
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

        public async Task<PaginatedList<TeamInfo>> GetAllTeamsInContestAsync(int pageNumber, int pageSize, Guid contestIdSearch)
        {
            try
            {
                // Validate page params
                if (pageNumber < 1 || pageSize < 1)
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Page number and page size must be greater than or equal to 1.");

                // Get current user info
                string? userIdClaim = _httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
                string? userRole = _httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.Role);

                Guid? currentUserId = null;
                if (Guid.TryParse(userIdClaim, out Guid parsedUserId))
                    currentUserId = parsedUserId;

                // Repositories
                IGenericRepository<LeaderboardEntry> leaderboardRepo = _unitOfWork.GetRepository<LeaderboardEntry>();
                IGenericRepository<TeamMember> teamMemberRepo = _unitOfWork.GetRepository<TeamMember>();
                IGenericRepository<McqAttempt> mcqAttemptRepo = _unitOfWork.GetRepository<McqAttempt>();
                IGenericRepository<Submission> submissionRepo = _unitOfWork.GetRepository<Submission>();
                IGenericRepository<Round> roundRepo = _unitOfWork.GetRepository<Round>();
                IGenericRepository<Config> configRepo = _unitOfWork.GetRepository<Config>();
                IGenericRepository<Student> studentRepo = _unitOfWork.GetRepository<Student>();
                IGenericRepository<Mentor> mentorRepo = _unitOfWork.GetRepository<Mentor>();
                IGenericRepository<Team> teamRepo = _unitOfWork.GetRepository<Team>();

                // Determine user's team if student or mentor
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

                // Load all leaderboard entries for contest ordered by Rank
                List<LeaderboardEntry> allEntries = await leaderboardRepo.Entities
                    .Where(l => l.ContestId == contestIdSearch)
                    .Include(l => l.Team)
                    .OrderBy(l => l.Rank)
                    .ToListAsync();

                if (!allEntries.Any())
                {
                    throw new ErrorException(StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        "No leaderboard entries found for the specified contest.");
                }

                // Map to TeamInfo
                List<TeamInfo> allTeams = allEntries.Select(entry => new TeamInfo
                {
                    TeamId = entry.TeamId,
                    TeamName = entry.Team?.Name ?? string.Empty,
                    Rank = entry.Rank ?? 0,
                    Score = entry.Score ?? 0,
                    Members = new List<MemberInfo>()
                }).ToList();

                int totalTeamCount = allTeams.Count;

                // Apply pagination
                List<TeamInfo> paginatedTeams = allTeams
                    .Skip((pageNumber - 1) * pageSize)
                    .Take(pageSize)
                    .ToList();

                // Decide whether to show all members
                bool showAllMembers = userRole != RoleConstants.Student && userRole != RoleConstants.Mentor && !string.IsNullOrWhiteSpace(userIdClaim);

                // Load rounds for contest
                List<Round> rounds = await roundRepo.Entities
                    .Where(r => r.ContestId == contestIdSearch && !r.DeletedAt.HasValue)
                    .OrderBy(r => r.Start)
                    .ToListAsync();

                // Populate member details for paginated teams only
                foreach (var teamData in paginatedTeams)
                {
                    bool isUserTeam = userTeamId.HasValue && userTeamId.Value == teamData.TeamId;

                    if (!showAllMembers && !isUserTeam)
                        continue;

                    // Get team members
                    List<TeamMember> teamMembers = await teamMemberRepo.Entities
                        .Include(tm => tm.Student)
                            .ThenInclude(s => s.User)
                        .Where(tm => tm.TeamId == teamData.TeamId)
                        .ToListAsync();

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

                        foreach (Round round in rounds)
                        {
                            double roundScore = 0;
                            string roundType = string.Empty;
                            DateTime? completedAt = null;

                            // Check finished flag in config
                            string key = ConfigKeys.RoundStudent(round.RoundId, teamMember.StudentId);
                            Config? config = await configRepo.GetByIdAsync(key);

                            if (config == null || config.DeletedAt != null)
                            {
                                continue;
                            }

                            // Check MCQ attempt
                            McqAttempt? mcqAttempt = await mcqAttemptRepo.Entities
                                .Where(ma => ma.RoundId == round.RoundId
                                             && ma.StudentId == teamMember.StudentId
                                             && ma.End.HasValue
                                             && ma.Status == McqAttemptStatusEnum.Finished.ToString())
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
                                // Check submission
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

                            memberInfo.RoundScores.Add(new RoundScoreDetail
                            {
                                RoundId = round.RoundId,
                                RoundName = round.Name,
                                Score = roundScore,
                                RoundType = roundType,
                                CompletedAt = completedAt
                            });

                            memberInfo.TotalScore += roundScore;
                        }

                        teamData.Members.Add(memberInfo);
                    }
                }

                return new PaginatedList<TeamInfo>(paginatedTeams, totalTeamCount, pageNumber, pageSize);
            }
            catch (Exception ex)
            {
                if (ex is ErrorException) throw;

                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error retrieving team list in leaderboard: {ex.Message}");
            }
        }

        public async Task UpdateTeamScoreAsync(Guid contestId, Guid teamId)
        {
            try
            {
                IGenericRepository<LeaderboardEntry> leaderboardRepo = _unitOfWork.GetRepository<LeaderboardEntry>();

                // Find the leaderboard entry for the team
                LeaderboardEntry? entry = await leaderboardRepo.Entities
                    .Where(e => e.ContestId == contestId && e.TeamId == teamId)
                    .FirstOrDefaultAsync();

                if (entry == null)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        $"Leaderboard entry not found for team {teamId} in contest {contestId}");
                }

                // Recalculate average score
                double averageScore = await CalculateTeamAverageScoreAsync(contestId, teamId);
                entry.Score = averageScore;
                entry.SnapshotAt = DateTime.UtcNow;

                await leaderboardRepo.UpdateAsync(entry);
                await _unitOfWork.SaveAsync();

                // Recalculate ranks after score change
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
                    $"Error refreshing leaderboard after score update: {ex.Message}");
            }
        }

        private async Task<double> CalculateTeamAverageScoreAsync(Guid contestId, Guid teamId)
        {
            IGenericRepository<TeamMember> teamMemberRepo = _unitOfWork.GetRepository<TeamMember>();
            IGenericRepository<Round> roundRepo = _unitOfWork.GetRepository<Round>();
            IGenericRepository<McqAttempt> mcqAttemptRepo = _unitOfWork.GetRepository<McqAttempt>();
            IGenericRepository<Submission> submissionRepo = _unitOfWork.GetRepository<Submission>();
            IGenericRepository<Config> configRepo = _unitOfWork.GetRepository<Config>();

            // Get team members count
            List<TeamMember> teamMembers = await teamMemberRepo.Entities
                .Where(tm => tm.TeamId == teamId)
                .ToListAsync();

            if (!teamMembers.Any())
            {
                return 0;
            }

            int teamSize = teamMembers.Count;

            // Get all rounds for the contest
            List<Round> rounds = await roundRepo.Entities
                .Where(r => r.ContestId == contestId && !r.DeletedAt.HasValue)
                .ToListAsync();

            double totalTeamScore = 0;

            // Calculate total score across all members and rounds
            foreach (TeamMember member in teamMembers)
            {
                foreach (Round round in rounds)
                {
                    // Check if student finished this round
                    string configKey = ConfigKeys.RoundStudent(round.RoundId, member.StudentId);
                    Config? config = await configRepo.GetByIdAsync(configKey);

                    if (config == null || config.DeletedAt != null)
                    {
                        continue;
                    }

                    // Check MCQ attempt
                    McqAttempt? mcqAttempt = await mcqAttemptRepo.Entities
                        .Where(ma => ma.RoundId == round.RoundId
                                    && ma.StudentId == member.StudentId
                                    && ma.End.HasValue
                                    && ma.DeletedAt == null
                                    && ma.Status == McqAttemptStatusEnum.Finished.ToString())
                        .OrderByDescending(ma => ma.End)
                        .FirstOrDefaultAsync();

                    if (mcqAttempt != null)
                    {
                        totalTeamScore += mcqAttempt.Score ?? 0;
                        continue;
                    }

                    // Check submission
                    Submission? submission = await submissionRepo.Entities
                        .Include(s => s.Problem)
                        .Where(s => s.Problem.RoundId == round.RoundId
                                   && s.SubmittedByStudentId == member.StudentId
                                   && s.TeamId == teamId
                                   && s.Status == SubmissionStatusEnum.Finished.ToString()
                                   && s.DeletedAt == null)
                        .OrderByDescending(s => s.CreatedAt)
                        .FirstOrDefaultAsync();

                    if (submission != null)
                    {
                        totalTeamScore += submission.Score;
                    }
                }
            }

            // Return average score
            return totalTeamScore / teamSize;
        }
    }
}