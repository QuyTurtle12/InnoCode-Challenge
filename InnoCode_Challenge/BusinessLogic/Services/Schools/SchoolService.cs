using AutoMapper;
using BusinessLogic.IServices.Schools;
using DataAccess.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Repository.DTOs.SchoolDTOs;
using Repository.IRepositories;
using System.Security.Claims;
using Utility.Constant;
using Utility.ExceptionCustom;
using Utility.PaginatedList;

namespace BusinessLogic.Services.Schools
{
    public class SchoolService : ISchoolService
    {
        private readonly IUOW _unitOfWork;
        private readonly IMapper _mapper;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public SchoolService(IUOW unitOfWork, IMapper mapper, IHttpContextAccessor httpContextAccessor)
        {
            _unitOfWork = unitOfWork;
            _mapper = mapper;
            _httpContextAccessor = httpContextAccessor;
        }

        public async Task<PaginatedList<SchoolDTO>> GetAsync(SchoolQueryParams queryParams)
        {
            var schoolRepository = _unitOfWork.GetRepository<School>();

            IQueryable<School> schoolsQuery = BuildSchoolQuery(schoolRepository, queryParams, null);

            var paged = await schoolRepository.GetPagingAsync(schoolsQuery, queryParams.Page, queryParams.PageSize);
            var items = paged.Items.Select(_mapper.Map<SchoolDTO>).ToList();

            return new PaginatedList<SchoolDTO>(items, paged.TotalCount, paged.PageNumber, paged.PageSize);
        }

        public async Task<SchoolDTO> GetByIdAsync(Guid id)
        {
            var schoolRepository = _unitOfWork.GetRepository<School>();
            var school = await schoolRepository.Entities
                .Include(s => s.Province)
                .Include(s => s.ManagerUser)
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.SchoolId == id && s.DeletedAt == null);

            if (school == null)
                throw new ErrorException(StatusCodes.Status404NotFound, SchoolErrorCodeConstants.NotFound, $"No school with ID={id}");

            return _mapper.Map<SchoolDTO>(school);
        }

        public async Task<SchoolDTO> CreateAsync(CreateSchoolDTO dto)
        {
            var schoolRepository = _unitOfWork.GetRepository<School>();
            var provinceRepository = _unitOfWork.GetRepository<Province>();

            await EnsureProvinceExistsAsync(provinceRepository, dto.ProvinceId);

            string trimmedName = NormalizeName(dto.Name);
            await EnsureSchoolNameUniqueAsync(schoolRepository, dto.ProvinceId, trimmedName, null);

            var now = DateTime.UtcNow;
            var school = _mapper.Map<School>(dto);
            school.Name = trimmedName;
            school.Contact = string.IsNullOrWhiteSpace(dto.Contact) ? null : dto.Contact!.Trim();
            school.CreatedAt = now;
            school.DeletedAt = null;

            await schoolRepository.InsertAsync(school);
            await _unitOfWork.SaveAsync();

            school = await schoolRepository.Entities.Include(s => s.Province)
                .FirstAsync(s => s.SchoolId == school.SchoolId);

            return _mapper.Map<SchoolDTO>(school);
        }

        public async Task<SchoolDTO> UpdateAsync(Guid id, UpdateSchoolDTO dto)
        {
            var schoolRepository = _unitOfWork.GetRepository<School>();
            var provinceRepository = _unitOfWork.GetRepository<Province>();

            var school = await schoolRepository.Entities
                .FirstOrDefaultAsync(s => s.SchoolId == id && s.DeletedAt == null);

            if (school == null)
                throw new ErrorException(StatusCodes.Status404NotFound, SchoolErrorCodeConstants.NotFound, $"No school with ID={id}");

            Guid newProvinceId = dto.ProvinceId ?? school.ProvinceId;
            if (dto.ProvinceId.HasValue)
            {
                await EnsureProvinceExistsAsync(provinceRepository, newProvinceId);
                school.ProvinceId = newProvinceId;
            }

            if (!string.IsNullOrWhiteSpace(dto.Name))
            {
                string newName = NormalizeName(dto.Name);
                await EnsureSchoolNameUniqueAsync(schoolRepository, newProvinceId, newName, id);

                school.Name = newName;
            }

            if (dto.Contact != null)
                school.Contact = string.IsNullOrWhiteSpace(dto.Contact) ? null : dto.Contact.Trim();

            if (dto.Address != null)
                school.Address = string.IsNullOrWhiteSpace(dto.Address) ? null : dto.Address.Trim();

            schoolRepository.Update(school);
            await _unitOfWork.SaveAsync();

            var updated = await schoolRepository.Entities
                .Include(s => s.Province)
                .AsNoTracking()
                .FirstAsync(s => s.SchoolId == id);

            return _mapper.Map<SchoolDTO>(updated);
        }

        public async Task DeleteAsync(Guid id)
        {
            var schoolRepository = _unitOfWork.GetRepository<School>();

            var school = await schoolRepository.Entities
                .Include(s => s.Students)
                .Include(s => s.Mentors)
                .Include(s => s.Teams)
                .Include(s => s.MentorRegistrations)
                .Include(s => s.SchoolCreationRequests)
                .FirstOrDefaultAsync(s => s.SchoolId == id && s.DeletedAt == null);

            if (school == null)
                throw new ErrorException(StatusCodes.Status404NotFound, SchoolErrorCodeConstants.NotFound, $"No school with ID={id}");

            bool hasRelations = school.Students.Any()
                || school.Mentors.Any()
                || school.Teams.Any()
                || school.MentorRegistrations.Any()
                || school.SchoolCreationRequests.Any();
            if (hasRelations)
                throw new ErrorException(StatusCodes.Status409Conflict, SchoolErrorCodeConstants.InUse,
                    "Cannot delete a school that has students, mentors, or teams.");

            school.DeletedAt = DateTime.UtcNow;
            schoolRepository.Update(school);
            await _unitOfWork.SaveAsync();
        }

        public async Task<PaginatedList<SchoolDTO>> GetMyManagedSchoolsAsync(SchoolQueryParams queryParams)
        {
            var schoolRepository = _unitOfWork.GetRepository<School>();

            // Get current user ID 
            string? userId = _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userId))
                throw new ErrorException(StatusCodes.Status401Unauthorized, "UNAUTHORIZED", "User ID not found.");

            if (!Guid.TryParse(userId, out Guid userGuid))
                throw new ErrorException(StatusCodes.Status401Unauthorized, "UNAUTHORIZED", "Invalid user ID.");

            IQueryable<School> q = BuildSchoolQuery(schoolRepository, queryParams, userGuid);

            var paged = await schoolRepository.GetPagingAsync(q, queryParams.Page, queryParams.PageSize);
            var items = paged.Items.Select(_mapper.Map<SchoolDTO>).ToList();

            return new PaginatedList<SchoolDTO>(items, paged.TotalCount, paged.PageNumber, paged.PageSize);
        }

        private static string NormalizeName(string name)
        {
            return name.Trim();
        }

        private static async Task EnsureSchoolNameUniqueAsync(
            IGenericRepository<School> schoolRepository,
            Guid provinceId,
            string name,
            Guid? excludeId)
        {
            var lowered = name.ToLowerInvariant();
            bool exists = await schoolRepository.Entities
                .AnyAsync(s => s.DeletedAt == null
                               && s.ProvinceId == provinceId
                               && s.SchoolId != excludeId
                               && s.Name.ToLower() == lowered);

            if (exists)
                throw new ErrorException(StatusCodes.Status400BadRequest, SchoolErrorCodeConstants.NameExists,
                    "School name already exists in this province.");
        }

        private static async Task EnsureProvinceExistsAsync(IGenericRepository<Province> provinceRepository, Guid provinceId)
        {
            bool exists = await provinceRepository.Entities.AnyAsync(p => p.ProvinceId == provinceId);
            if (!exists)
                throw new ErrorException(StatusCodes.Status404NotFound, ProvinceErrorCodeConstants.NotFound,
                    $"No province with ID={provinceId}");
        }

        private static IQueryable<School> BuildSchoolQuery(
            IGenericRepository<School> schoolRepository,
            SchoolQueryParams queryParams,
            Guid? managerUserId)
        {
            IQueryable<School> q = schoolRepository.Entities
                .Where(s => s.DeletedAt == null)
                .Include(s => s.Province)
                .Include(s => s.ManagerUser)
                .AsNoTracking();

            if (managerUserId.HasValue)
                q = q.Where(s => s.ManagerUserId == managerUserId.Value);

            if (queryParams.ProvinceId.HasValue)
                q = q.Where(s => s.ProvinceId == queryParams.ProvinceId.Value);

            if (!string.IsNullOrWhiteSpace(queryParams.Search))
            {
                string keyword = queryParams.Search.Trim().ToLower();
                q = q.Where(s =>
                    s.Name.ToLower().Contains(keyword) ||
                    (s.Contact != null && s.Contact.ToLower().Contains(keyword)));
            }

            q = (queryParams.SortBy?.ToLowerInvariant()) switch
            {
                "createdat" => queryParams.Desc ? q.OrderByDescending(s => s.CreatedAt)
                                                : q.OrderBy(s => s.CreatedAt),
                "provincename" => queryParams.Desc ? q.OrderByDescending(s => s.Province.Name)
                                                   : q.OrderBy(s => s.Province.Name),
                _ => queryParams.Desc ? q.OrderByDescending(s => s.Name)
                                      : q.OrderBy(s => s.Name),
            };

            return q;
        }
    }
}
