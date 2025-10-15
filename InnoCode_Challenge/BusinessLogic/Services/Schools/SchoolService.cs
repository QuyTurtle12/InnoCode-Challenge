using AutoMapper;
using BusinessLogic.IServices.Schools;
using DataAccess.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Repository.DTOs.SchoolDTOs;
using Repository.IRepositories;
using Utility.ExceptionCustom;
using Utility.PaginatedList;

namespace BusinessLogic.Services.Schools
{
    public class SchoolService : ISchoolService
    {
        private readonly IUOW _unitOfWork;
        private readonly IMapper _mapper;

        public SchoolService(IUOW unitOfWork, IMapper mapper)
        {
            _unitOfWork = unitOfWork;
            _mapper = mapper;
        }

        public async Task<PaginatedList<SchoolDTO>> GetAsync(SchoolQueryParams queryParams)
        {
            var schoolRepository = _unitOfWork.GetRepository<School>();

            IQueryable<School> schoolsQuery = schoolRepository.Entities
                .Where(s => s.DeletedAt == null)
                .Include(s => s.Province)
                .AsNoTracking();

            if (queryParams.ProvinceId.HasValue)
            {
                schoolsQuery = schoolsQuery.Where(s => s.ProvinceId == queryParams.ProvinceId.Value);
            }

            if (!string.IsNullOrWhiteSpace(queryParams.Search))
            {
                string keyword = queryParams.Search.Trim().ToLower();
                schoolsQuery = schoolsQuery.Where(s =>
                    s.Name.ToLower().Contains(keyword) ||
                    s.Contact != null && s.Contact.ToLower().Contains(keyword));
            }

            schoolsQuery = (queryParams.SortBy?.ToLowerInvariant()) switch
            {
                "createdat" => queryParams.Desc ? schoolsQuery.OrderByDescending(s => s.CreatedAt)
                                                   : schoolsQuery.OrderBy(s => s.CreatedAt),
                "provincename" => queryParams.Desc ? schoolsQuery.OrderByDescending(s => s.Province.Name)
                                                   : schoolsQuery.OrderBy(s => s.Province.Name),
                _ => queryParams.Desc ? schoolsQuery.OrderByDescending(s => s.Name)
                                                    : schoolsQuery.OrderBy(s => s.Name),
            };

            var paged = await schoolRepository.GetPagingAsync(schoolsQuery, queryParams.Page, queryParams.PageSize);
            var items = paged.Items.Select(_mapper.Map<SchoolDTO>).ToList();

            return new PaginatedList<SchoolDTO>(items, paged.TotalCount, paged.PageNumber, paged.PageSize);
        }

        public async Task<SchoolDTO> GetByIdAsync(Guid id)
        {
            var schoolRepository = _unitOfWork.GetRepository<School>();
            var school = await schoolRepository.Entities
                .Include(s => s.Province)
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.SchoolId == id && s.DeletedAt == null);

            if (school == null)
                throw new ErrorException(StatusCodes.Status404NotFound, "SCHOOL_NOT_FOUND", $"No school with ID={id}");

            return _mapper.Map<SchoolDTO>(school);
        }

        public async Task<SchoolDTO> CreateAsync(CreateSchoolDTO dto)
        {
            var schoolRepository = _unitOfWork.GetRepository<School>();
            var provinceRepository = _unitOfWork.GetRepository<Province>();

            bool provinceExists = await provinceRepository.Entities
                .AnyAsync(p => p.ProvinceId == dto.ProvinceId);
            if (!provinceExists)
                throw new ErrorException(StatusCodes.Status404NotFound, "PROVINCE_NOT_FOUND", $"No province with ID={dto.ProvinceId}");

            string trimmedName = dto.Name.Trim();

            bool nameExistsInProvince = await schoolRepository.Entities
                .AnyAsync(s => s.ProvinceId == dto.ProvinceId
                               && s.DeletedAt == null
                               && s.Name.ToLower() == trimmedName.ToLower());
            if (nameExistsInProvince)
                throw new ErrorException(StatusCodes.Status400BadRequest, "NAME_EXISTS",
                    "School name already exists in this province.");

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
                throw new ErrorException(StatusCodes.Status404NotFound, "SCHOOL_NOT_FOUND", $"No school with ID={id}");

            Guid newProvinceId = dto.ProvinceId ?? school.ProvinceId;
            if (dto.ProvinceId.HasValue)
            {
                bool newProvinceExists = await provinceRepository.Entities
                    .AnyAsync(p => p.ProvinceId == newProvinceId);
                if (!newProvinceExists)
                    throw new ErrorException(StatusCodes.Status404NotFound, "PROVINCE_NOT_FOUND",
                        $"No province with ID={newProvinceId}");
                school.ProvinceId = newProvinceId;
            }

            if (!string.IsNullOrWhiteSpace(dto.Name))
            {
                string newName = dto.Name.Trim();
                bool duplicateInProvince = await schoolRepository.Entities
                    .AnyAsync(s => s.SchoolId != id
                                   && s.ProvinceId == newProvinceId
                                   && s.DeletedAt == null
                                   && s.Name.ToLower() == newName.ToLower());
                if (duplicateInProvince)
                    throw new ErrorException(StatusCodes.Status400BadRequest, "NAME_EXISTS",
                        "School name already exists in this province.");

                school.Name = newName;
            }

            if (dto.Contact != null)
                school.Contact = string.IsNullOrWhiteSpace(dto.Contact) ? null : dto.Contact.Trim();

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
                .FirstOrDefaultAsync(s => s.SchoolId == id && s.DeletedAt == null);

            if (school == null)
                throw new ErrorException(StatusCodes.Status404NotFound, "SCHOOL_NOT_FOUND", $"No school with ID={id}");

            bool hasRelations = school.Students.Any() || school.Mentors.Any() || school.Teams.Any();
            if (hasRelations)
                throw new ErrorException(StatusCodes.Status409Conflict, "SCHOOL_IN_USE",
                    "Cannot delete a school that has students, mentors, or teams.");

            school.DeletedAt = DateTime.UtcNow;
            schoolRepository.Update(school);
            await _unitOfWork.SaveAsync();
        }
    }
}
