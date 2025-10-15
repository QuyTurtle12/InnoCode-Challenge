using AutoMapper;
using BusinessLogic.IServices;
using DataAccess.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Repository.DTOs.ConfigDTOs;
using Repository.IRepositories;
using Utility.ExceptionCustom;
using Utility.PaginatedList;

namespace BusinessLogic.Services
{
    public class ConfigService : IConfigService
    {
        private readonly IUOW _uow;
        private readonly IMapper _mapper;

        public ConfigService(IUOW uow, IMapper mapper)
        {
            _uow = uow;
            _mapper = mapper;
        }

        public async Task<PaginatedList<ConfigDTO>> GetAsync(ConfigQueryParams query)
        {
            var repo = _uow.GetRepository<Config>();
            var q = repo.Entities.Where(c => c.DeletedAt == null).AsNoTracking();

            if (!string.IsNullOrWhiteSpace(query.Search))
            {
                var k = query.Search.Trim().ToLower();
                q = q.Where(c =>
                    c.Key.ToLower().Contains(k) ||
                    (c.Value != null && c.Value.ToLower().Contains(k)));
            }

            if (!string.IsNullOrWhiteSpace(query.Scope))
                q = q.Where(c => c.Scope == query.Scope);

            q = (query.SortBy?.ToLowerInvariant()) switch
            {
                "key" => query.Desc ? q.OrderByDescending(c => c.Key) : q.OrderBy(c => c.Key),
                "updatedat" => query.Desc ? q.OrderByDescending(c => c.UpdatedAt) : q.OrderBy(c => c.UpdatedAt),
                _ => query.Desc ? q.OrderByDescending(c => c.Key) : q.OrderBy(c => c.Key),
            };

            var page = await repo.GetPagingAsync(q, query.Page, query.PageSize);
            var items = page.Items.Select(_mapper.Map<ConfigDTO>).ToList();
            return new PaginatedList<ConfigDTO>(items, page.TotalCount, page.PageNumber, page.PageSize);
        }

        public async Task<ConfigDTO> GetByKeyAsync(string key)
        {
            var repo = _uow.GetRepository<Config>();
            var norm = NormalizeKey(key);
            var entity = await repo.Entities.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Key == norm && c.DeletedAt == null);

            if (entity == null)
                throw new ErrorException(StatusCodes.Status404NotFound, "CONFIG_NOT_FOUND", $"No config with key='{key}'");

            return _mapper.Map<ConfigDTO>(entity);
        }

        public async Task<ConfigDTO> CreateAsync(CreateConfigDTO dto)
        {
            var repo = _uow.GetRepository<Config>();
            var norm = NormalizeKey(dto.Key);

            var existing = await repo.Entities.FirstOrDefaultAsync(c => c.Key == norm);
            if (existing != null && existing.DeletedAt == null)
                throw new ErrorException(StatusCodes.Status409Conflict, "CONFIG_EXISTS", $"Key '{dto.Key}' already exists.");

            if (existing != null && existing.DeletedAt != null)
            {
                existing.Value = dto.Value;
                existing.Scope = dto.Scope;
                existing.UpdatedAt = DateTime.UtcNow;
                existing.DeletedAt = null;
                repo.Update(existing);
                await _uow.SaveAsync();
                return _mapper.Map<ConfigDTO>(existing);
            }

            var entity = _mapper.Map<Config>(dto);
            entity.Key = norm;
            await repo.InsertAsync(entity);
            await _uow.SaveAsync();
            return _mapper.Map<ConfigDTO>(entity);
        }

        public async Task<ConfigDTO> UpdateAsync(string key, UpdateConfigDTO dto)
        {
            var repo = _uow.GetRepository<Config>();
            var norm = NormalizeKey(key);
            var entity = await repo.Entities.FirstOrDefaultAsync(c => c.Key == norm && c.DeletedAt == null);
            if (entity == null)
                throw new ErrorException(StatusCodes.Status404NotFound, "CONFIG_NOT_FOUND", $"No config with key='{key}'");

            _mapper.Map(dto, entity);
            repo.Update(entity);
            await _uow.SaveAsync();
            return _mapper.Map<ConfigDTO>(entity);
        }

        public async Task DeleteAsync(string key)
        {
            var repo = _uow.GetRepository<Config>();
            var norm = NormalizeKey(key);
            var entity = await repo.Entities.FirstOrDefaultAsync(c => c.Key == norm && c.DeletedAt == null);
            if (entity == null)
                throw new ErrorException(StatusCodes.Status404NotFound, "CONFIG_NOT_FOUND", $"No config with key='{key}'");

            entity.DeletedAt = DateTime.UtcNow;
            repo.Update(entity);
            await _uow.SaveAsync();
        }

        private static string NormalizeKey(string key) => key.Trim().ToLowerInvariant();
    }
}
