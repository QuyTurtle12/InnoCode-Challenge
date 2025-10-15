using AutoMapper;
using BusinessLogic.IServices;
using DataAccess.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Repository.DTOs.TeamMemberDTOs;
using Repository.IRepositories;
using Utility.ExceptionCustom;
using Utility.PaginatedList;

namespace BusinessLogic.Services.Students
{
    public class TeamMemberService : ITeamMemberService
    {
        private static readonly string[] AllowedMemberRoles = { "Captain", "Member" };

        private readonly IUOW _unitOfWork;
        private readonly IMapper _mapper;

        public TeamMemberService(IUOW unitOfWork, IMapper mapper)
        {
            _unitOfWork = unitOfWork;
            _mapper = mapper;
        }

        public async Task<PaginatedList<TeamMemberDTO>> GetAsync(TeamMemberQueryParams queryParams)
        {
            var teamMemberRepository = _unitOfWork.GetRepository<TeamMember>();

            IQueryable<TeamMember> query = teamMemberRepository.Entities
                .Include(tm => tm.Team)
                .Include(tm => tm.Student).ThenInclude(s => s.User)
                .AsNoTracking();

            if (queryParams.TeamId.HasValue)
                query = query.Where(tm => tm.TeamId == queryParams.TeamId.Value);

            if (queryParams.StudentId.HasValue)
                query = query.Where(tm => tm.StudentId == queryParams.StudentId.Value);

            if (!string.IsNullOrWhiteSpace(queryParams.MemberRole))
            {
                string role = NormalizeRole(queryParams.MemberRole);
                query = query.Where(tm => tm.MemberRole == role);
            }

            if (!string.IsNullOrWhiteSpace(queryParams.Search))
            {
                string keyword = queryParams.Search.Trim().ToLower();
                query = query.Where(tm =>
                    tm.Student.User.Fullname.ToLower().Contains(keyword) ||
                    tm.Student.User.Email.ToLower().Contains(keyword));
            }

            query = (queryParams.SortBy?.ToLowerInvariant()) switch
            {
                "studentname" => queryParams.Desc ? query.OrderByDescending(tm => tm.Student.User.Fullname)
                                                  : query.OrderBy(tm => tm.Student.User.Fullname),
                "teamname" => queryParams.Desc ? query.OrderByDescending(tm => tm.Team.Name)
                                                  : query.OrderBy(tm => tm.Team.Name),
                "memberrole" => queryParams.Desc ? query.OrderByDescending(tm => tm.MemberRole)
                                                  : query.OrderBy(tm => tm.MemberRole),
                _ => queryParams.Desc ? query.OrderByDescending(tm => tm.JoinedAt)
                                                  : query.OrderBy(tm => tm.JoinedAt),
            };

            var paged = await teamMemberRepository.GetPagingAsync(query, queryParams.Page, queryParams.PageSize);
            var items = paged.Items.Select(_mapper.Map<TeamMemberDTO>).ToList();

            return new PaginatedList<TeamMemberDTO>(items, paged.TotalCount, paged.PageNumber, paged.PageSize);
        }

        public async Task<TeamMemberDTO> GetByIdAsync(Guid teamId, Guid studentId)
        {
            var teamMemberRepository = _unitOfWork.GetRepository<TeamMember>();
            var teamMember = await teamMemberRepository.Entities
                .Include(tm => tm.Team)
                .Include(tm => tm.Student).ThenInclude(s => s.User)
                .AsNoTracking()
                .FirstOrDefaultAsync(tm => tm.TeamId == teamId && tm.StudentId == studentId);

            if (teamMember == null)
                throw new ErrorException(StatusCodes.Status404NotFound, "TEAM_MEMBER_NOT_FOUND",
                    $"No team member with TeamId={teamId} & StudentId={studentId}");

            return _mapper.Map<TeamMemberDTO>(teamMember);
        }

        public async Task<TeamMemberDTO> CreateAsync(CreateTeamMemberDTO dto)
        {
            var teamRepository = _unitOfWork.GetRepository<Team>();
            var studentRepository = _unitOfWork.GetRepository<Student>();
            var teamMemberRepository = _unitOfWork.GetRepository<TeamMember>();

            string role = NormalizeRole(dto.MemberRole ?? "Member");
            ValidateRole(role);

            var team = await teamRepository.Entities
                .FirstOrDefaultAsync(t => t.TeamId == dto.TeamId && t.DeletedAt == null);
            if (team == null)
                throw new ErrorException(StatusCodes.Status404NotFound, "TEAM_NOT_FOUND", $"No team with ID={dto.TeamId}");

            var student = await studentRepository.Entities
                .FirstOrDefaultAsync(s => s.StudentId == dto.StudentId && s.DeletedAt == null);
            if (student == null)
                throw new ErrorException(StatusCodes.Status404NotFound, "STUDENT_NOT_FOUND", $"No student with ID={dto.StudentId}");

            if (student.SchoolId != team.SchoolId)
                throw new ErrorException(StatusCodes.Status400BadRequest, "SCHOOL_MISMATCH",
                    "Student must belong to the same school as the team.");

            bool alreadyMember = await teamMemberRepository.Entities
                .AnyAsync(tm => tm.TeamId == dto.TeamId && tm.StudentId == dto.StudentId);
            if (alreadyMember)
                throw new ErrorException(StatusCodes.Status400BadRequest, "ALREADY_MEMBER",
                    "This student is already a member of the team.");

            if (role == "Captain")
            {
                bool hasCaptain = await teamMemberRepository.Entities
                    .AnyAsync(tm => tm.TeamId == dto.TeamId && tm.MemberRole == "Captain");
                if (hasCaptain)
                    throw new ErrorException(StatusCodes.Status400BadRequest, "CAPTAIN_EXISTS",
                        "This team already has a captain.");
            }

            var teamMember = new TeamMember
            {
                TeamId = dto.TeamId,
                StudentId = dto.StudentId,
                MemberRole = role,
                JoinedAt = DateTime.UtcNow
            };

            await teamMemberRepository.InsertAsync(teamMember);
            await _unitOfWork.SaveAsync();

            var created = await teamMemberRepository.Entities
                .Include(tm => tm.Team)
                .Include(tm => tm.Student).ThenInclude(s => s.User)
                .AsNoTracking()
                .FirstAsync(tm => tm.TeamId == dto.TeamId && tm.StudentId == dto.StudentId);

            return _mapper.Map<TeamMemberDTO>(created);
        }

        public async Task<TeamMemberDTO> UpdateAsync(Guid teamId, Guid studentId, UpdateTeamMemberDTO dto)
        {
            var teamMemberRepository = _unitOfWork.GetRepository<TeamMember>();

            string role = NormalizeRole(dto.MemberRole);
            ValidateRole(role);

            var teamMember = await teamMemberRepository.Entities
                .Include(tm => tm.Team)
                .FirstOrDefaultAsync(tm => tm.TeamId == teamId && tm.StudentId == studentId);

            if (teamMember == null)
                throw new ErrorException(StatusCodes.Status404NotFound, "TEAM_MEMBER_NOT_FOUND",
                    $"No team member with TeamId={teamId} & StudentId={studentId}");

            if (role == "Captain" && teamMember.MemberRole != "Captain")
            {
                bool hasOtherCaptain = await teamMemberRepository.Entities
                    .AnyAsync(tm => tm.TeamId == teamId && tm.StudentId != studentId && tm.MemberRole == "Captain");
                if (hasOtherCaptain)
                    throw new ErrorException(StatusCodes.Status400BadRequest, "CAPTAIN_EXISTS",
                        "This team already has a captain.");
            }

            teamMember.MemberRole = role;

            teamMemberRepository.Update(teamMember);
            await _unitOfWork.SaveAsync();

            var updated = await teamMemberRepository.Entities
                .Include(tm => tm.Team)
                .Include(tm => tm.Student).ThenInclude(s => s.User)
                .AsNoTracking()
                .FirstAsync(tm => tm.TeamId == teamId && tm.StudentId == studentId);

            return _mapper.Map<TeamMemberDTO>(updated);
        }

        public async Task DeleteAsync(Guid teamId, Guid studentId)
        {
            var teamMemberRepository = _unitOfWork.GetRepository<TeamMember>();

            var teamMember = await teamMemberRepository.Entities
                .FirstOrDefaultAsync(tm => tm.TeamId == teamId && tm.StudentId == studentId);

            if (teamMember == null)
                throw new ErrorException(StatusCodes.Status404NotFound, "TEAM_MEMBER_NOT_FOUND",
                    $"No team member with TeamId={teamId} & StudentId={studentId}");

            teamMemberRepository.Delete(teamMember);
            await _unitOfWork.SaveAsync();
        }

        private static string NormalizeRole(string role) => role.Trim().ToLowerInvariant() switch
        {
            "captain" => "Captain",
            "member" => "Member",
            _ => role 
        };

        private static void ValidateRole(string role)
        {
            if (!AllowedMemberRoles.Contains(role))
                throw new ErrorException(StatusCodes.Status400BadRequest, "INVALID_ROLE",
                    "MemberRole must be Captain or Member.");
        }
    }
}
