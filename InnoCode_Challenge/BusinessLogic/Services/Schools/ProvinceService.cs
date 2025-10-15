using AutoMapper;
using BusinessLogic.IServices.Schools;
using DataAccess.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Repository.DTOs.ProvinceDTOs;
using Repository.IRepositories;
using Utility.ExceptionCustom;
using Utility.PaginatedList;

namespace BusinessLogic.Services.Schools
{
    public class ProvinceService : IProvinceService
    {
        private readonly IUOW _unitOfWork;
        private readonly IMapper _mapper;

        public ProvinceService(IUOW unitOfWork, IMapper mapper)
        {
            _unitOfWork = unitOfWork;
            _mapper = mapper;
        }

        public async Task<PaginatedList<ProvinceDTO>> GetAsync(ProvinceQueryParams queryParams)
        {
            var provinceRepository = _unitOfWork.GetRepository<Province>();

            IQueryable<Province> provincesQuery = provinceRepository.Entities.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(queryParams.Search))
            {
                string searchKeyword = queryParams.Search.Trim().ToLower();
                provincesQuery = provincesQuery.Where(province =>
                    province.Name.ToLower().Contains(searchKeyword) ||
                    province.Address != null && province.Address.ToLower().Contains(searchKeyword));
            }

            provincesQuery = (queryParams.SortBy?.ToLowerInvariant()) switch
            {
                "address" => queryParams.Desc
                    ? provincesQuery.OrderByDescending(province => province.Address)
                    : provincesQuery.OrderBy(province => province.Address),

                _ => queryParams.Desc
                    ? provincesQuery.OrderByDescending(province => province.Name)
                    : provincesQuery.OrderBy(province => province.Name),
            };

            var pagedResult = await provinceRepository.GetPagingAsync(
                provincesQuery,
                queryParams.Page,
                queryParams.PageSize
            );

            var provinceDtos = pagedResult.Items.Select(_mapper.Map<ProvinceDTO>).ToList();

            return new PaginatedList<ProvinceDTO>(
                provinceDtos,
                pagedResult.TotalCount,
                pagedResult.PageNumber,
                pagedResult.PageSize
            );
        }

        public async Task<ProvinceDTO> GetByIdAsync(Guid id)
        {
            var provinceRepository = _unitOfWork.GetRepository<Province>();
            var province = await provinceRepository.GetByIdAsync(id);

            if (province == null)
                throw new ErrorException(
                    StatusCodes.Status404NotFound,
                    "PROVINCE_NOT_FOUND",
                    $"No province with ID={id}"
                );

            return _mapper.Map<ProvinceDTO>(province);
        }

        public async Task<ProvinceDTO> CreateAsync(CreateProvinceDTO dto)
        {
            var provinceRepository = _unitOfWork.GetRepository<Province>();
            string trimmedName = dto.Name.Trim();

            bool nameExists = await provinceRepository.Entities
                .AnyAsync(province => province.Name.ToLower() == trimmedName.ToLower());

            if (nameExists)
                throw new ErrorException(
                    StatusCodes.Status400BadRequest,
                    "NAME_EXISTS",
                    "Province name already exists."
                );

            var province = _mapper.Map<Province>(dto);
            province.Name = trimmedName;

            await provinceRepository.InsertAsync(province);
            await _unitOfWork.SaveAsync();

            return _mapper.Map<ProvinceDTO>(province);
        }

        public async Task<ProvinceDTO> UpdateAsync(Guid id, UpdateProvinceDTO dto)
        {
            var provinceRepository = _unitOfWork.GetRepository<Province>();
            var province = await provinceRepository.GetByIdAsync(id);

            if (province == null)
                throw new ErrorException(
                    StatusCodes.Status404NotFound,
                    "PROVINCE_NOT_FOUND",
                    $"No province with ID={id}"
                );

            if (!string.IsNullOrWhiteSpace(dto.Name))
            {
                string newName = dto.Name.Trim();
                bool nameTaken = await provinceRepository.Entities
                    .AnyAsync(other =>
                        other.ProvinceId != id &&
                        other.Name.ToLower() == newName.ToLower());

                if (nameTaken)
                    throw new ErrorException(
                        StatusCodes.Status400BadRequest,
                        "NAME_EXISTS",
                        "Province name already exists."
                    );

                province.Name = newName;
            }

            if (dto.Address != null)
                province.Address = string.IsNullOrWhiteSpace(dto.Address) ? null : dto.Address.Trim();

            provinceRepository.Update(province);
            await _unitOfWork.SaveAsync();

            return _mapper.Map<ProvinceDTO>(province);
        }

        public async Task DeleteAsync(Guid id)
        {
            var provinceRepository = _unitOfWork.GetRepository<Province>();

            var province = await provinceRepository.Entities
                .Include(p => p.Schools)
                .FirstOrDefaultAsync(p => p.ProvinceId == id);

            if (province == null)
                throw new ErrorException(
                    StatusCodes.Status404NotFound,
                    "PROVINCE_NOT_FOUND",
                    $"No province with ID={id}"
                );

            if (province.Schools.Any())
                throw new ErrorException(
                    StatusCodes.Status409Conflict,
                    "PROVINCE_IN_USE",
                    "Cannot delete a province that has schools."
                );

            provinceRepository.Delete(province);
            await _unitOfWork.SaveAsync();
        }
    }
}
