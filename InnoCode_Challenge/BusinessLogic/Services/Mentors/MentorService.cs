using AutoMapper;
using BusinessLogic.IServices;
using DataAccess.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Repository.DTOs.MentorDTOs;
using Repository.IRepositories;
using Utility.Constant;
using Utility.ExceptionCustom;
using Utility.PaginatedList;

namespace BusinessLogic.Services.Mentors
{
    public class MentorService : IMentorService
    {
        private readonly IUOW _unitOfWork;
        private readonly IMapper _mapper;

        public MentorService(IUOW unitOfWork, IMapper mapper)
        {
            _unitOfWork = unitOfWork;
            _mapper = mapper;
        }

        public async Task<PaginatedList<MentorDTO>> GetAsync(MentorQueryParams queryParams)
        {
            var mentorRepository = _unitOfWork.GetRepository<Mentor>();

            IQueryable<Mentor> mentorsQuery = mentorRepository.Entities
                .Where(m => m.DeletedAt == null)
                .Include(m => m.User)
                .Include(m => m.School)
                .AsNoTracking();

            if (queryParams.SchoolId.HasValue)
                mentorsQuery = mentorsQuery.Where(m => m.SchoolId == queryParams.SchoolId.Value);

            if (queryParams.UserId.HasValue)
                mentorsQuery = mentorsQuery.Where(m => m.UserId == queryParams.UserId.Value);

            if (!string.IsNullOrWhiteSpace(queryParams.Search))
            {
                string keyword = queryParams.Search.Trim().ToLower();
                mentorsQuery = mentorsQuery.Where(m =>
                    m.User.Fullname.ToLower().Contains(keyword) ||
                    m.User.Email.ToLower().Contains(keyword) ||
                    m.Phone != null && m.Phone.ToLower().Contains(keyword));
            }

            mentorsQuery = (queryParams.SortBy?.ToLowerInvariant()) switch
            {
                "username" => queryParams.Desc ? mentorsQuery.OrderByDescending(m => m.User.Fullname)
                                                 : mentorsQuery.OrderBy(m => m.User.Fullname),
                "schoolname" => queryParams.Desc ? mentorsQuery.OrderByDescending(m => m.School.Name)
                                                 : mentorsQuery.OrderBy(m => m.School.Name),
                "phone" => queryParams.Desc ? mentorsQuery.OrderByDescending(m => m.Phone)
                                                 : mentorsQuery.OrderBy(m => m.Phone),
                _ => queryParams.Desc ? mentorsQuery.OrderByDescending(m => m.CreatedAt)
                                                 : mentorsQuery.OrderBy(m => m.CreatedAt),
            };

            var paged = await mentorRepository.GetPagingAsync(mentorsQuery, queryParams.Page, queryParams.PageSize);
            var items = paged.Items.Select(_mapper.Map<MentorDTO>).ToList();

            return new PaginatedList<MentorDTO>(items, paged.TotalCount, paged.PageNumber, paged.PageSize);
        }

        public async Task<MentorDTO> GetByIdAsync(Guid id)
        {
            var mentorRepository = _unitOfWork.GetRepository<Mentor>();
            var mentor = await mentorRepository.Entities
                .Include(m => m.User)
                .Include(m => m.School)
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.MentorId == id && m.DeletedAt == null);

            if (mentor == null)
                throw new ErrorException(StatusCodes.Status404NotFound, "MENTOR_NOT_FOUND", $"No mentor with ID={id}");

            return _mapper.Map<MentorDTO>(mentor);
        }

        public async Task<MentorDTO> CreateAsync(CreateMentorDTO dto)
        {
            var mentorRepository = _unitOfWork.GetRepository<Mentor>();
            var userRepository = _unitOfWork.GetRepository<User>();
            var schoolRepository = _unitOfWork.GetRepository<School>();

            // Validate User
            var user = await userRepository.Entities
                .FirstOrDefaultAsync(u => u.UserId == dto.UserId && u.DeletedAt == null);

            if (user == null)
                throw new ErrorException(StatusCodes.Status404NotFound, "USER_NOT_FOUND", $"No user with ID={dto.UserId}");

            if (!string.Equals(user.Role, RoleConstants.Mentor, StringComparison.Ordinal))
                throw new ErrorException(StatusCodes.Status400BadRequest, "USER_NOT_MENTOR", "User role must be Mentor.");

            if (!string.Equals(user.Status, "Active", StringComparison.Ordinal) &&
                !string.Equals(user.Status, "Inactive", StringComparison.Ordinal))
            {
                throw new ErrorException(StatusCodes.Status400BadRequest, "USER_INVALID_STATUS",
                    "User must be Active or Inactive (pending).");
            }

            bool schoolExists = await schoolRepository.Entities
                .AnyAsync(s => s.SchoolId == dto.SchoolId && s.DeletedAt == null);
            if (!schoolExists)
                throw new ErrorException(StatusCodes.Status404NotFound, "SCHOOL_NOT_FOUND", $"No school with ID={dto.SchoolId}");

            bool alreadyMentor = await mentorRepository.Entities
                .AnyAsync(m => m.UserId == dto.UserId && m.DeletedAt == null);
            if (alreadyMentor)
                throw new ErrorException(StatusCodes.Status400BadRequest, "USER_ALREADY_MENTOR", "This user is already a mentor.");

            var now = DateTime.UtcNow;

            var mentor = new Mentor
            {
                MentorId = Guid.NewGuid(),
                UserId = dto.UserId,
                SchoolId = dto.SchoolId,
                Phone = string.IsNullOrWhiteSpace(dto.Phone) ? null : dto.Phone.Trim(),
                CreatedAt = now,
                DeletedAt = null
            };

            await mentorRepository.InsertAsync(mentor);

            if (string.Equals(user.Status, "Inactive", StringComparison.Ordinal))
            {
                user.Status = "Active";
                user.UpdatedAt = now;
                userRepository.Update(user);
            }

            await _unitOfWork.SaveAsync();

            var created = await mentorRepository.Entities
                .Include(m => m.User)
                .Include(m => m.School)
                .AsNoTracking()
                .FirstAsync(m => m.MentorId == mentor.MentorId);

            return _mapper.Map<MentorDTO>(created);
        }

        public async Task<MentorDTO> UpdateAsync(Guid id, UpdateMentorDTO dto)
        {
            var mentorRepository = _unitOfWork.GetRepository<Mentor>();
            var schoolRepository = _unitOfWork.GetRepository<School>();

            var mentor = await mentorRepository.Entities
                .FirstOrDefaultAsync(m => m.MentorId == id && m.DeletedAt == null);

            if (mentor == null)
                throw new ErrorException(StatusCodes.Status404NotFound, "MENTOR_NOT_FOUND", $"No mentor with ID={id}");

            if (dto.SchoolId.HasValue && dto.SchoolId.Value != mentor.SchoolId)
            {
                bool schoolExists = await schoolRepository.Entities
                    .AnyAsync(s => s.SchoolId == dto.SchoolId.Value && s.DeletedAt == null);
                if (!schoolExists)
                    throw new ErrorException(StatusCodes.Status404NotFound, "SCHOOL_NOT_FOUND", $"No school with ID={dto.SchoolId.Value}");

                mentor.SchoolId = dto.SchoolId.Value;
            }

            if (dto.Phone != null)
                mentor.Phone = string.IsNullOrWhiteSpace(dto.Phone) ? null : dto.Phone.Trim();

            mentorRepository.Update(mentor);
            await _unitOfWork.SaveAsync();

            var updated = await mentorRepository.Entities
                .Include(m => m.User)
                .Include(m => m.School)
                .AsNoTracking()
                .FirstAsync(m => m.MentorId == id);

            return _mapper.Map<MentorDTO>(updated);
        }

        public async Task DeleteAsync(Guid id)
        {
            var mentorRepository = _unitOfWork.GetRepository<Mentor>();

            var mentor = await mentorRepository.Entities
                .Include(m => m.Teams)
                .FirstOrDefaultAsync(m => m.MentorId == id && m.DeletedAt == null);

            if (mentor == null)
                throw new ErrorException(StatusCodes.Status404NotFound, "MENTOR_NOT_FOUND", $"No mentor with ID={id}");

            if (mentor.Teams.Any())
                throw new ErrorException(StatusCodes.Status409Conflict, "MENTOR_IN_USE",
                    "Cannot delete a mentor who is assigned to teams.");

            mentor.DeletedAt = DateTime.UtcNow;
            mentorRepository.Update(mentor);
            await _unitOfWork.SaveAsync();
        }
    }
}
