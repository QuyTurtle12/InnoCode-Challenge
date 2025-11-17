using AutoMapper;
using BusinessLogic.IServices.Contests;
using BusinessLogic.IServices.Students;
using DataAccess.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Repository.DTOs.TeamDTOs;
using Repository.DTOs.TeamMemberDTOs;
using Repository.IRepositories;
using Utility.Constant;
using Utility.ExceptionCustom;
using Utility.PaginatedList;

namespace BusinessLogic.Services.Students
{
    public class TeamService : ITeamService
    {
        private readonly IUOW _unitOfWork;
        private readonly IMapper _mapper;
        private readonly ILeaderboardEntryService _leaderboardEntryService;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public TeamService(IUOW unitOfWork, IMapper mapper, ILeaderboardEntryService leaderboardEntryService, IHttpContextAccessor httpContextAccessor)
        {
            _unitOfWork = unitOfWork;
            _mapper = mapper;
            _leaderboardEntryService = leaderboardEntryService;
            _httpContextAccessor = httpContextAccessor; 
        }


        public async Task<PaginatedList<TeamDTO>> GetAsync(TeamQueryParams queryParams)
        {
            var teamRepository = _unitOfWork.GetRepository<Team>();

            IQueryable<Team> teamsQuery = teamRepository.Entities
                .Where(t => t.DeletedAt == null)
                .Include(t => t.Contest)
                .Include(t => t.School)
                .Include(t => t.Mentor).ThenInclude(m => m.User)
                .AsNoTracking();

            if (queryParams.ContestId.HasValue)
                teamsQuery = teamsQuery.Where(t => t.ContestId == queryParams.ContestId.Value);

            if (queryParams.SchoolId.HasValue)
                teamsQuery = teamsQuery.Where(t => t.SchoolId == queryParams.SchoolId.Value);

            if (queryParams.MentorId.HasValue)
                teamsQuery = teamsQuery.Where(t => t.MentorId == queryParams.MentorId.Value);

            if (!string.IsNullOrWhiteSpace(queryParams.Search))
            {
                string keyword = queryParams.Search.Trim().ToLower();
                teamsQuery = teamsQuery.Where(t => t.Name.ToLower().Contains(keyword));
            }

            teamsQuery = (queryParams.SortBy?.ToLowerInvariant()) switch
            {
                "name" => queryParams.Desc ? teamsQuery.OrderByDescending(t => t.Name)
                                                   : teamsQuery.OrderBy(t => t.Name),
                "contestname" => queryParams.Desc ? teamsQuery.OrderByDescending(t => t.Contest.Name)
                                                   : teamsQuery.OrderBy(t => t.Contest.Name),
                "schoolname" => queryParams.Desc ? teamsQuery.OrderByDescending(t => t.School.Name)
                                                   : teamsQuery.OrderBy(t => t.School.Name),
                "mentorname" => queryParams.Desc ? teamsQuery.OrderByDescending(t => t.Mentor.User.Fullname)
                                                   : teamsQuery.OrderBy(t => t.Mentor.User.Fullname),
                _ => queryParams.Desc ? teamsQuery.OrderByDescending(t => t.CreatedAt)
                                                   : teamsQuery.OrderBy(t => t.CreatedAt),
            };

            var paged = await teamRepository.GetPagingAsync(teamsQuery, queryParams.Page, queryParams.PageSize);
            var items = paged.Items.Select(_mapper.Map<TeamDTO>).ToList();

            return new PaginatedList<TeamDTO>(items, paged.TotalCount, paged.PageNumber, paged.PageSize);
        }

        public async Task<TeamDTO> GetByIdAsync(Guid id)
        {
            var teamRepository = _unitOfWork.GetRepository<Team>();
            var team = await teamRepository.Entities
                .Include(t => t.Contest)
                .Include(t => t.School)
                .Include(t => t.Mentor).ThenInclude(m => m.User)
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.TeamId == id && t.DeletedAt == null);

            if (team == null)
                throw new ErrorException(StatusCodes.Status404NotFound, "TEAM_NOT_FOUND", $"No team with ID={id}");

            return _mapper.Map<TeamDTO>(team);
        }

        public async Task<TeamDTO> CreateAsync(CreateTeamDTO dto)
        {
            var teamRepository = _unitOfWork.GetRepository<Team>();
            var contestRepository = _unitOfWork.GetRepository<Contest>();
            var schoolRepository = _unitOfWork.GetRepository<School>();
            var mentorRepository = _unitOfWork.GetRepository<Mentor>();

            // mentorId diff userId
            string userId = GetCurrentUserIdOrThrow();
            bool hasUserGuid = Guid.TryParse(userId, out Guid userGuid); 

            var meAsMentor = await mentorRepository.Entities
                .Include(m => m.User)
                .FirstOrDefaultAsync(m =>
                    m.User != null &&
                    ((hasUserGuid && EF.Property<Guid>(m.User, "UserId") == userGuid) ||
                     m.User.UserId.ToString() == userId));
            if (meAsMentor == null)
                throw new ErrorException(StatusCodes.Status403Forbidden, "NOT_MENTOR",
                    "Only mentors can create teams."); 

            bool contestExists = await contestRepository.Entities.AnyAsync(c => c.ContestId == dto.ContestId);
            if (!contestExists)
                throw new ErrorException(StatusCodes.Status404NotFound, "CONTEST_NOT_FOUND", $"No contest with ID={dto.ContestId}");

            bool schoolExists = await schoolRepository.Entities.AnyAsync(s => s.SchoolId == dto.SchoolId && s.DeletedAt == null);
            if (!schoolExists)
                throw new ErrorException(StatusCodes.Status404NotFound, "SCHOOL_NOT_FOUND", $"No school with ID={dto.SchoolId}");

            if (meAsMentor.SchoolId != dto.SchoolId) 
                throw new ErrorException(StatusCodes.Status409Conflict, "MENTOR_NOT_BELONG_TO_SCHOOL",
                    "This mentor does not belong to the selected school."); 

            var trimmedName = dto.Name.Trim();

            bool duplicateName = await teamRepository.Entities.AnyAsync(t =>
                t.ContestId == dto.ContestId &&
                t.DeletedAt == null &&
                t.Name.ToLower() == trimmedName.ToLower());
            if (duplicateName)
                throw new ErrorException(StatusCodes.Status400BadRequest, "NAME_EXISTS",
                    "A team with this name already exists in the contest.");

            var now = DateTime.UtcNow;
            var team = new Team
            {
                TeamId = Guid.NewGuid(),
                Name = trimmedName,
                ContestId = dto.ContestId,
                SchoolId = dto.SchoolId,
                MentorId = meAsMentor.MentorId,    
                CreatedAt = now,
                DeletedAt = null,
                Status = TeamStatusConstants.Active
            };

            await teamRepository.InsertAsync(team);
            await _unitOfWork.SaveAsync();

            await _leaderboardEntryService.AddTeamToLeaderboardAsync(dto.ContestId, team.TeamId);

            var created = await teamRepository.Entities
                .Include(t => t.Contest)
                .Include(t => t.School)
                .Include(t => t.Mentor).ThenInclude(m => m.User)
                .AsNoTracking()
                .FirstAsync(t => t.TeamId == team.TeamId);

            return _mapper.Map<TeamDTO>(created);
        }


        public async Task<TeamDTO> UpdateAsync(Guid id, UpdateTeamDTO dto)
        {
            var teamRepository = _unitOfWork.GetRepository<Team>();
            var contestRepository = _unitOfWork.GetRepository<Contest>();
            var schoolRepository = _unitOfWork.GetRepository<School>();
            var mentorRepository = _unitOfWork.GetRepository<Mentor>();

            var team = await teamRepository.Entities
                .FirstOrDefaultAsync(t => t.TeamId == id && t.DeletedAt == null);

            if (team == null)
                throw new ErrorException(StatusCodes.Status404NotFound, "TEAM_NOT_FOUND", $"No team with ID={id}");

            Guid targetContestId = dto.ContestId ?? team.ContestId;
            Guid targetSchoolId = dto.SchoolId ?? team.SchoolId;
            Guid targetMentorId = dto.MentorId ?? team.MentorId;

            if (dto.ContestId.HasValue)
            {
                bool contestExists = await contestRepository.Entities.AnyAsync(c => c.ContestId == targetContestId);
                if (!contestExists)
                    throw new ErrorException(StatusCodes.Status404NotFound, "CONTEST_NOT_FOUND",
                        $"No contest with ID={targetContestId}");
                team.ContestId = targetContestId;
            }

            if (dto.SchoolId.HasValue)
            {
                bool schoolExists = await schoolRepository.Entities.AnyAsync(s => s.SchoolId == targetSchoolId && s.DeletedAt == null);
                if (!schoolExists)
                    throw new ErrorException(StatusCodes.Status404NotFound, "SCHOOL_NOT_FOUND",
                        $"No school with ID={targetSchoolId}");
                team.SchoolId = targetSchoolId;
            }

            if (dto.MentorId.HasValue)
            {
                var mentor = await mentorRepository.Entities.Include(m => m.User)
                    .FirstOrDefaultAsync(m => m.MentorId == targetMentorId);
                
                if (mentor == null)
                    throw new ErrorException(StatusCodes.Status404NotFound, "MENTOR_NOT_FOUND",
                        $"No mentor with ID={targetMentorId}");
                
                if (mentor.SchoolId != dto.SchoolId) throw new ErrorException(StatusCodes.Status409Conflict, "MENTOR_NOT_BELONG_TO_SCHOOL", "This mentor is not belong to this school.");
                
                team.MentorId = targetMentorId;
            }

            if (!string.IsNullOrWhiteSpace(dto.Name))
            {
                string newName = dto.Name.Trim();
                bool duplicate = await teamRepository.Entities.AnyAsync(t =>
                    t.TeamId != id &&
                    t.ContestId == team.ContestId &&
                    t.DeletedAt == null &&
                    t.Name.ToLower() == newName.ToLower());
                if (duplicate)
                    throw new ErrorException(StatusCodes.Status400BadRequest, "NAME_EXISTS",
                        "A team with this name already exists in the contest.");

                team.Name = newName;
            }

            teamRepository.Update(team);
            await _unitOfWork.SaveAsync();

            var updated = await teamRepository.Entities
                .Include(t => t.Contest)
                .Include(t => t.School)
                .Include(t => t.Mentor).ThenInclude(m => m.User)
                .AsNoTracking()
                .FirstAsync(t => t.TeamId == id);

            return _mapper.Map<TeamDTO>(updated);
        }

        public async Task DeleteAsync(Guid id)
        {
            var teamRepository = _unitOfWork.GetRepository<Team>();

            var team = await teamRepository.Entities
                .Include(t => t.TeamMembers)
                .Include(t => t.Submissions)
                .Include(t => t.LeaderboardEntries)
                .Include(t => t.Certificates)
                .Include(t => t.Appeals)
                .FirstOrDefaultAsync(t => t.TeamId == id && t.DeletedAt == null);

            if (team == null)
                throw new ErrorException(StatusCodes.Status404NotFound, "TEAM_NOT_FOUND", $"No team with ID={id}");

            bool hasRelations = team.TeamMembers.Any() ||
                                team.Submissions.Any() ||
                                team.LeaderboardEntries.Any() ||
                                team.Certificates.Any() ||
                                team.Appeals.Any();

            if (hasRelations)
                throw new ErrorException(StatusCodes.Status409Conflict, "TEAM_IN_USE",
                    "Cannot delete a team that has related records.");

            team.DeletedAt = DateTime.UtcNow;
            teamRepository.Update(team);
            await _unitOfWork.SaveAsync();
        }

        public async Task<IReadOnlyList<TeamWithMembersDTO>> GetMyTeamsAsync()
        {
            string userId = GetCurrentUserIdOrThrow();

            var teamRepo = _unitOfWork.GetRepository<Team>();
            var studentRepo = _unitOfWork.GetRepository<Student>();
            var mentorRepo = _unitOfWork.GetRepository<Mentor>();

            bool hasUserGuid = Guid.TryParse(userId, out Guid userGuid);

            Guid? myStudentId = await studentRepo.Entities
                .Where(s => s.DeletedAt == null
                    && ((hasUserGuid && EF.Property<Guid>(s, "UserId") == userGuid) 
                        || s.UserId.ToString() == userId))                          
                .Select(s => (Guid?)s.StudentId)
                .FirstOrDefaultAsync();

            // mentorId diff userId
            Guid? myMentorId = await mentorRepo.Entities
                .Include(m => m.User)
                .Where(m => m.User != null
                    && ((hasUserGuid && EF.Property<Guid>(m.User, "UserId") == userGuid) 
                        || m.User.UserId.ToString() == userId))                          
                .Select(m => (Guid?)m.MentorId)
                .FirstOrDefaultAsync();

            IQueryable<Team> q = teamRepo.Entities
                .Where(t => t.DeletedAt == null)
                .Include(t => t.Contest)
                .Include(t => t.School)
                .Include(t => t.Mentor).ThenInclude(m => m.User)
                .Include(t => t.TeamMembers).ThenInclude(tm => tm.Student).ThenInclude(s => s.User);

            if (myStudentId.HasValue && myMentorId.HasValue)
            {
                q = q.Where(t => t.MentorId == myMentorId.Value
                              || t.TeamMembers.Any(tm => tm.StudentId == myStudentId.Value));
            }
            else if (myStudentId.HasValue)
            {
                q = q.Where(t => t.TeamMembers.Any(tm => tm.StudentId == myStudentId.Value));
            }
            else if (myMentorId.HasValue)
            {
                q = q.Where(t => t.MentorId == myMentorId.Value);
            }
            else
            {
                return Array.Empty<TeamWithMembersDTO>();
            }

            var teams = await q.OrderByDescending(t => t.CreatedAt).ToListAsync();

            var result = teams.Select(t => new TeamWithMembersDTO
            {
                TeamId = t.TeamId,
                Name = t.Name,
                ContestId = t.ContestId,
                ContestName = t.Contest?.Name ?? "N/A",
                SchoolId = t.SchoolId,
                SchoolName = t.School?.Name ?? "N/A",
                MentorId = t.MentorId,
                MentorName = t.Mentor?.User?.Fullname ?? "N/A",
                CreatedAt = t.CreatedAt,
                Members = t.TeamMembers
                    .OrderByDescending(tm => tm.MemberRole == "Captain")
                    .ThenBy(tm => tm.Student.User.Fullname)
                    .Select(tm => new Repository.DTOs.TeamMemberDTOs.TeamMemberDTO
                    {
                        TeamId = tm.TeamId,
                        TeamName = t.Name,
                        StudentId = tm.StudentId,
                        StudentFullname = tm.Student.User.Fullname,
                        StudentEmail = tm.Student.User.Email,
                        MemberRole = tm.MemberRole ?? "Member",
                        JoinedAt = tm.JoinedAt
                    }).ToList()
            }).ToList();

            return result;
        }
        private string GetCurrentUserIdOrThrow()
        {
            var user = _httpContextAccessor.HttpContext?.User;
            if (user == null || user.Identity == null || !user.Identity.IsAuthenticated)
                throw new ErrorException(StatusCodes.Status401Unauthorized, "UNAUTHENTICATED", "Sign in required.");

            var id = user.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                     ?? user.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value;

            if (string.IsNullOrWhiteSpace(id))
                throw new ErrorException(StatusCodes.Status401Unauthorized, "UNAUTHENTICATED", "Invalid user context.");

            return id;
        }

    }
}
