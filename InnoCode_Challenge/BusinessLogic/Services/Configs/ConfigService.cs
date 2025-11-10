using AutoMapper;
using BusinessLogic.IServices;
using DataAccess.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Repository.DTOs.ConfigDTOs;
using Repository.IRepositories;
using Utility.Constant;
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
            var list = repo.Entities.Where(c => c.DeletedAt == null).AsNoTracking();

            if (!string.IsNullOrWhiteSpace(query.KeyPrefix))
            {
                var prefix = query.KeyPrefix.Trim();
                list = list.Where(c => c.Key.StartsWith(prefix));
            }

            if (!string.IsNullOrWhiteSpace(query.Scope))
            {
                var scope = query.Scope.Trim();
                list = list.Where(c => c.Scope == scope);
            }

            if (!string.IsNullOrWhiteSpace(query.Search))
            {
                var k = query.Search.Trim().ToLower();
                list = list.Where(c => c.Key.ToLower().Contains(k) || (c.Value != null && c.Value.ToLower().Contains(k)));
            }

            list = (query.SortBy?.ToLowerInvariant()) switch
            {
                "key" => query.Desc ? list.OrderByDescending(c => c.Key) : list.OrderBy(c => c.Key),
                "updatedat" => query.Desc ? list.OrderByDescending(c => c.UpdatedAt) : list.OrderBy(c => c.UpdatedAt),
                _ => query.Desc ? list.OrderByDescending(c => c.UpdatedAt) : list.OrderBy(c => c.UpdatedAt),
            };

            var page = await repo.GetPagingAsync(list, query.Page, query.PageSize);
            var items = page.Items.Select(_mapper.Map<ConfigDTO>).ToList();
            return new PaginatedList<ConfigDTO>(items, page.TotalCount, page.PageNumber, page.PageSize);
        }

        public async Task<ConfigDTO> GetByKeyAsync(string key)
        {
            var repo = _uow.GetRepository<Config>();
            var config = await repo.Entities.AsNoTracking().FirstOrDefaultAsync(c => c.Key == key && c.DeletedAt == null);
            if (config == null)
                throw new ErrorException(StatusCodes.Status404NotFound, "CONFIG_NOT_FOUND", $"No config with key '{key}'.");
            return _mapper.Map<ConfigDTO>(config);
        }

        public async Task<ConfigDTO> CreateAsync(CreateConfigDTO dto, string performedByRole)
        {
            EnsureStaffOrAdmin(performedByRole);

            ValidateKey(dto.Key);

            var repo = _uow.GetRepository<Config>();
            var exists = await repo.Entities.AnyAsync(c => c.Key == dto.Key && c.DeletedAt == null);
            if (exists)
                throw new ErrorException(StatusCodes.Status409Conflict, "CONFIG_EXISTS", $"Config '{dto.Key}' already exists.");

            var resurrect = await repo.Entities.FirstOrDefaultAsync(c => c.Key == dto.Key && c.DeletedAt != null);
            if (resurrect != null)
            {
                resurrect.Value = dto.Value;
                resurrect.Scope = dto.Scope;
                resurrect.DeletedAt = null;
                resurrect.UpdatedAt = DateTime.UtcNow;
                repo.Update(resurrect);
                await _uow.SaveAsync();
                return _mapper.Map<ConfigDTO>(resurrect);
            }

            var entity = _mapper.Map<Config>(dto);
            await repo.InsertAsync(entity);
            await _uow.SaveAsync();
            return _mapper.Map<ConfigDTO>(entity);
        }

        public async Task<ConfigDTO> UpdateAsync(string key, UpdateConfigDTO dto, string performedByRole)
        {
            EnsureStaffOrAdmin(performedByRole);

            var repo = _uow.GetRepository<Config>();
            var entity = await repo.Entities.FirstOrDefaultAsync(c => c.Key == key && c.DeletedAt == null);
            if (entity == null)
                throw new ErrorException(StatusCodes.Status404NotFound, "CONFIG_NOT_FOUND", $"No config with key '{key}'.");

            _mapper.Map(dto, entity);
            entity.UpdatedAt = DateTime.UtcNow;

            repo.Update(entity);
            await _uow.SaveAsync();

            return _mapper.Map<ConfigDTO>(entity);
        }

        public async Task DeleteAsync(string key, string performedByRole)
        {
            EnsureStaffOrAdmin(performedByRole);

            var repo = _uow.GetRepository<Config>();
            var entity = await repo.Entities.FirstOrDefaultAsync(c => c.Key == key && c.DeletedAt == null);
            if (entity == null)
                throw new ErrorException(StatusCodes.Status404NotFound, "CONFIG_NOT_FOUND", $"No config with key '{key}'.");

            entity.DeletedAt = DateTime.UtcNow;
            repo.Update(entity);
            await _uow.SaveAsync();
        }

        // ---------- Contest helpers ----------

        public async Task SetRegistrationWindowAsync(Guid contestId, SetRegistrationWindowDTO dto, string performedByRole)
        {
            EnsureStaffOrAdmin(performedByRole);

            if (dto.RegistrationEndUtc <= dto.RegistrationStartUtc)
                throw new ErrorException(StatusCodes.Status400BadRequest, "INVALID_WINDOW", "End must be after Start.");

            var repo = _uow.GetRepository<Config>();
            var startKey = $"contest:{contestId}:registration_start";
            var endKey = $"contest:{contestId}:registration_end";

            await UpsertAsync(repo, startKey, dto.RegistrationStartUtc.ToString("o"), "contest");
            await UpsertAsync(repo, endKey, dto.RegistrationEndUtc.ToString("o"), "contest");
            await _uow.SaveAsync();
        }

        public async Task SetContestPolicyAsync(Guid contestId, SetContestPolicyDTO dto, string performedByRole)
        {
            EnsureStaffOrAdmin(performedByRole);

            var repo = _uow.GetRepository<Config>();

            if (dto.TeamMembersMax.HasValue)
                await UpsertAsync(repo, $"contest:{contestId}:team_members_max", dto.TeamMembersMax.Value.ToString(), "contest");

            if (dto.TeamInviteTtlDays.HasValue)
                await UpsertAsync(repo, "team_invite_ttl_days", dto.TeamInviteTtlDays.Value.ToString(), "global");

            await _uow.SaveAsync();
        }

        // ---------- helpers ----------

        private static void ValidateKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ErrorException(StatusCodes.Status400BadRequest, "INVALID_KEY", "Key is required.");

            if (!System.Text.RegularExpressions.Regex.IsMatch(key, @"^[a-z0-9:_\.\-]+$"))
                throw new ErrorException(StatusCodes.Status400BadRequest, "INVALID_KEY",
                    "Key must be lowercase and may contain a-z, 0-9, :, ., - and _.");
        }

        private static void EnsureStaffOrAdmin(string role)
        {
            if (role != RoleConstants.Admin && role != RoleConstants.Staff)
                throw new ErrorException(StatusCodes.Status403Forbidden, "FORBIDDEN", "Staff or Admin required.");
        }

        private static async Task UpsertAsync(IGenericRepository<Config> repo, string key, string? value, string? scope)
        {
            var item = await repo.Entities.FirstOrDefaultAsync(c => c.Key == key);
            if (item == null)
            {
                await repo.InsertAsync(new Config
                {
                    Key = key,
                    Value = value,
                    Scope = scope,
                    UpdatedAt = DateTime.UtcNow,
                    DeletedAt = null
                });
            }
            else
            {
                item.Value = value;
                item.Scope = scope;
                item.UpdatedAt = DateTime.UtcNow;
                item.DeletedAt = null;
                repo.Update(item);
            }
        }

        public async Task<bool> AreSubmissionsDistributedAsync(Guid roundId)
        {
            string key = ConfigKeys.RoundSubmissionsDistributed(roundId);

            var config = await _uow.GetRepository<Config>()
                .Entities
                .FirstOrDefaultAsync(c => c.Key == key && !c.DeletedAt.HasValue);

            return config != null && config.Value?.ToLower() == "true";
        }

        public async Task MarkSubmissionsAsDistributedAsync(Guid roundId)
        {
            string key = ConfigKeys.RoundSubmissionsDistributed(roundId);
            await SetConfigValueAsync(key, "true", "round");
        }

        public async Task ResetDistributionStatusAsync(Guid roundId)
        {
            string key = ConfigKeys.RoundSubmissionsDistributed(roundId);
            await SetConfigValueAsync(key, "false", "round");
        }

        private async Task SetConfigValueAsync(string key, string value, string? scope = null)
        {
            var configRepo = _uow.GetRepository<Config>();

            var existingConfig = await configRepo.Entities
                .FirstOrDefaultAsync(c => c.Key == key && !c.DeletedAt.HasValue);

            if (existingConfig != null)
            {
                existingConfig.Value = value;
                existingConfig.UpdatedAt = DateTime.UtcNow;
                if (!string.IsNullOrEmpty(scope))
                {
                    existingConfig.Scope = scope;
                }
                await configRepo.UpdateAsync(existingConfig);
            }
            else
            {
                var newConfig = new Config
                {
                    Key = key,
                    Value = value,
                    Scope = scope,
                    UpdatedAt = DateTime.UtcNow
                };
                await configRepo.InsertAsync(newConfig);
            }
        }

    }
}
