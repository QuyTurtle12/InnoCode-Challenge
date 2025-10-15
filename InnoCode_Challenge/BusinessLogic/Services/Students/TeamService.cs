using AutoMapper;
using BusinessLogic.IServices;
using DataAccess.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Repository.DTOs.TeamDTOs;
using Repository.IRepositories;
using Utility.ExceptionCustom;
using Utility.PaginatedList;

namespace BusinessLogic.Services.Students
{
    public class TeamService : ITeamService
    {
        private readonly IUOW _unitOfWork;
        private readonly IMapper _mapper;

        public TeamService(IUOW unitOfWork, IMapper mapper)
        {
            _unitOfWork = unitOfWork;
            _mapper = mapper;
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

            bool contestExists = await contestRepository.Entities.AnyAsync(c => c.ContestId == dto.ContestId);
            if (!contestExists)
                throw new ErrorException(StatusCodes.Status404NotFound, "CONTEST_NOT_FOUND", $"No contest with ID={dto.ContestId}");

            bool schoolExists = await schoolRepository.Entities.AnyAsync(s => s.SchoolId == dto.SchoolId && s.DeletedAt == null);
            if (!schoolExists)
                throw new ErrorException(StatusCodes.Status404NotFound, "SCHOOL_NOT_FOUND", $"No school with ID={dto.SchoolId}");

            var mentor = await mentorRepository.Entities
                .Include(m => m.User)
                .FirstOrDefaultAsync(m => m.MentorId == dto.MentorId);
            if (mentor == null)
                throw new ErrorException(StatusCodes.Status404NotFound, "MENTOR_NOT_FOUND", $"No mentor with ID={dto.MentorId}");

            var trimmedName = dto.Name.Trim();

            bool duplicateName = await teamRepository.Entities.AnyAsync(t =>
                t.ContestId == dto.ContestId &&
                t.DeletedAt == null &&
                t.Name.ToLower() == trimmedName.ToLower());
            if (duplicateName)
                throw new ErrorException(StatusCodes.Status400BadRequest, "NAME_EXISTS",
                    "A team with this name already exists in the contest.");

            if (mentor.SchoolId != dto.SchoolId) throw new ErrorException(StatusCodes.Status409Conflict,"MENTOR_NOT_BELONG_TO_SCHOOL","This mentor is not belong to this school.");

            var now = DateTime.UtcNow;
            var team = new Team
            {
                TeamId = Guid.NewGuid(),
                Name = trimmedName,
                ContestId = dto.ContestId,
                SchoolId = dto.SchoolId,
                MentorId = dto.MentorId,
                CreatedAt = now,
                DeletedAt = null
            };

            await teamRepository.InsertAsync(team);
            await _unitOfWork.SaveAsync();

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
    }
}
