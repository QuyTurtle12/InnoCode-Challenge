using AutoMapper;
using BusinessLogic.IServices.Certificates;
using BusinessLogic.IServices.Contests;
using DataAccess.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Repository.DTOs.CertificateDTOs;
using Repository.DTOs.ContestDTOs;
using Repository.IRepositories;
using Utility.Constant;
using Utility.Enums;
using Utility.ExceptionCustom;
using Utility.PaginatedList;

namespace BusinessLogic.Services.Contests
{
    public class ContestService : IContestService
    {
        private readonly IMapper _mapper;
        private readonly IUOW _unitOfWork;
        private readonly IServiceProvider _serviceProvider;
        private const int MIN_YEAR = 10;

        // Constructor
        public ContestService(IMapper mapper, IUOW uow, IServiceProvider serviceProvider)
        {
            _mapper = mapper;
            _unitOfWork = uow;
            _serviceProvider = serviceProvider;
        }

        public async Task CreateContestAsync(CreateContestDTO contestDTO)
        {
            try
            {
                // Start a transaction
                _unitOfWork.BeginTransaction();

                // Validate input data
                if (contestDTO == null)
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Contest data cannot be null.");
                }

                // Validate name
                if (string.IsNullOrWhiteSpace(contestDTO.Name))
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Contest name is required.");
                }

                // Validate year
                int currentYear = DateTime.UtcNow.Year;
                if (contestDTO.Year < currentYear)
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, $"Contest year must be greater than {currentYear}.");
                }

                // Map DTO to entity
                Contest newContest = _mapper.Map<Contest>(contestDTO);

                // Set initial status to Draft
                newContest.Status = ContestStatusEnum.Draft.ToString();

                // Set creation timestamp
                newContest.CreatedAt = DateTime.UtcNow;

                // Get repository and insert new contest
                IGenericRepository<Contest> contestRepo = _unitOfWork.GetRepository<Contest>();
                await contestRepo.InsertAsync(newContest);

                // Save changes to database
                await _unitOfWork.SaveAsync();

                // Commit the transaction
                _unitOfWork.CommitTransaction();
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
                    $"Error creating Contests: {ex.Message}");
            }
        }

        public async Task DeleteContestAsync(Guid id)
        {
            try
            {
                // Start a transaction
                _unitOfWork.BeginTransaction();

                // Validate contest ID
                if (id == Guid.Empty)
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Invalid contest ID.");
                }

                // Get repository and fetch the contest by ID
                IGenericRepository<Contest> contestRepo = _unitOfWork.GetRepository<Contest>();
                Contest? existingContest = await contestRepo.GetByIdAsync(id);

                // Check if the contest exists
                if (existingContest == null)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound, ResponseCodeConstants.NOT_FOUND, "Contest not found.");
                }

                // Check if the contest is already deleted
                if (existingContest.DeletedAt.HasValue)
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Contest already deleted.");
                }

                // Soft delete by setting the DeletedAt property
                existingContest.DeletedAt = DateTime.UtcNow;

                // Set status to Cancelled
                existingContest.Status = ContestStatusEnum.Cancelled.ToString();

                // Update the contest
                await contestRepo.UpdateAsync(existingContest);

                // Save changes to database
                await _unitOfWork.SaveAsync();

                // Commit the transaction
                _unitOfWork.CommitTransaction();
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
                    $"Error deleting Contests: {ex.Message}");
            }
        }

        public async Task<PaginatedList<GetContestDTO>> GetPaginatedContestAsync(int pageNumber, int pageSize, Guid? idSearch, string? nameSearch, int? yearSearch, DateTime? startDate, DateTime? endDate)
        {
            try
            {
                // Validate pageNumber and pageSize
                if (pageNumber < 1 || pageSize < 1)
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Page number and page size must be greater than or equal to 1.");
                }

                // Validate year range
                if (yearSearch.HasValue)
                {
                    int currentYear = DateTime.UtcNow.Year;
                    if (yearSearch < MIN_YEAR)
                    {
                        throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, $"Year must be greater than 1900");
                    }
                }

                // Validate date range
                if (startDate.HasValue && endDate.HasValue && startDate.Value > endDate.Value)
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Start date cannot be later than end date.");
                }

                // Get contest repository
                var contestRepo = _unitOfWork.GetRepository<Contest>();

                // Get all available contests
                IQueryable<Contest> query = contestRepo.Entities.Where(c => !c.DeletedAt.HasValue);

                // Apply filters if provided
                if (idSearch.HasValue)
                {
                    query = query.Where(c => c.ContestId == idSearch.Value);
                }

                if (!string.IsNullOrWhiteSpace(nameSearch))
                {
                    query = query.Where(c => c.Name.Contains(nameSearch));
                }

                if (yearSearch.HasValue)
                {
                    query = query.Where(c => c.Year == yearSearch.Value);
                }

                if (startDate.HasValue)
                {
                    query = query.Where(c => c.Start >= startDate.Value);
                }

                if (endDate.HasValue)
                {
                    query = query.Where(c => c.End <= endDate.Value);
                }

                // Order by creation date (newest first)
                query = query.OrderByDescending(c => c.CreatedAt);

                // Change to paginated list to facilitate mapping process
                PaginatedList<Contest> resultQuery = await contestRepo.GetPagingAsync(query, pageNumber, pageSize);

                // Map the result to DTO
                IReadOnlyCollection<GetContestDTO> result = resultQuery.Items.Select(item =>
                {
                    GetContestDTO contestDTO = _mapper.Map<GetContestDTO>(item);

                    return contestDTO;
                }).ToList();

                // Create a new paginated list with the mapped DTOs
                PaginatedList<GetContestDTO> paginatedList = new PaginatedList<GetContestDTO>(result, resultQuery.TotalCount, resultQuery.PageNumber, resultQuery.PageSize);

                // Return the paginated list of DTOs
                return paginatedList;
            }
            catch (Exception ex)
            {
                if (ex is ErrorException)
                {
                    throw;
                }
                
                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error retrieving Contests: {ex.Message}");
            }
        }

        public async Task PublishContestAsync(Guid id)
        {
            try
            {
                // Start a transaction
                _unitOfWork.BeginTransaction();

                // Validate contest ID
                if (id == Guid.Empty)
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Invalid contest ID.");
                }

                // Get repository and fetch the contest by ID
                IGenericRepository<Contest> contestRepo = _unitOfWork.GetRepository<Contest>();
                Contest? existingContest = await contestRepo.GetByIdAsync(id);

                // Check if the contest exists
                if (existingContest == null || existingContest.DeletedAt.HasValue)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound, ResponseCodeConstants.NOT_FOUND, "Contest not found.");
                }

                // Set status to Published
                existingContest.Status = ContestStatusEnum.Published.ToString();

                // Update the contest
                await contestRepo.UpdateAsync(existingContest);

                // Save changes to database
                await _unitOfWork.SaveAsync();

                // Commit the transaction
                _unitOfWork.CommitTransaction();
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
                    $"Error publishing Contests: {ex.Message}");
            }
        }

        public async Task UpdateContestAsync(Guid id, UpdateContestDTO contestDTO)
        {
            try
            {
                // Start a transaction
                _unitOfWork.BeginTransaction();

                // Validate input data
                if (contestDTO == null)
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Contest data cannot be null.");
                }

                if (id == Guid.Empty)
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Invalid contest ID.");
                }

                // Validate name
                if (string.IsNullOrWhiteSpace(contestDTO.Name))
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Contest name is required.");
                }

                // Get repository and fetch the contest by ID
                IGenericRepository<Contest> contestRepo = _unitOfWork.GetRepository<Contest>();
                Contest? existingContest = await contestRepo.GetByIdAsync(id);

                // Check if the contest exists
                if (existingContest == null || existingContest.DeletedAt.HasValue)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound, ResponseCodeConstants.NOT_FOUND, "Contest not found.");
                }

                // Update properties from DTO
                _mapper.Map(contestDTO, existingContest);
                existingContest.Status = contestDTO.Status.ToString();

                // Update the contest
                await contestRepo.UpdateAsync(existingContest);

                // Save changes to database
                await _unitOfWork.SaveAsync();

                // Commit the transaction
                _unitOfWork.CommitTransaction();
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
                    $"Error updating Contests: {ex.Message}");
            }
        }

        public async Task<ContestCreatedDTO> CreateContestWithPolicyAsync(CreateContestAdvancedDTO dto)
        {
            if (dto == null)
                throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Payload cannot be null.");

            if (string.IsNullOrWhiteSpace(dto.Name))
                throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Name is required.");

            var currentYear = DateTime.UtcNow.Year;
            if (dto.Year < currentYear)
                throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, $"Year must be ≥ {currentYear}.");

            var contestRepo = _unitOfWork.GetRepository<Contest>();
            var configRepo = _unitOfWork.GetRepository<Config>();

            var nameTrim = dto.Name.Trim();

            var exists = await contestRepo.Entities
                .AnyAsync(c => c.Year == dto.Year && c.Name == nameTrim && c.DeletedAt == null);

            if (exists)
            {
                var suggestion = await SuggestAlternateNameAsync(nameTrim, dto.Year, contestRepo);
                var ex = new CoreException("CONTEST_DUPLICATE", "Contest name already exists for this year.", StatusCodes.Status409Conflict)
                {
                    AdditionalData = new Dictionary<string, object>
                    {
                        ["suggestion"] = suggestion
                    }
                };
                throw ex;
            }

            var entity = _mapper.Map<Contest>(dto);
            entity.ContestId = Guid.NewGuid();
            entity.Name = nameTrim;
            entity.Status = ContestStatusEnum.Draft.ToString(); // draft
            entity.CreatedAt = DateTime.UtcNow;

            _unitOfWork.BeginTransaction();
            try
            {
                await contestRepo.InsertAsync(entity);
                await _unitOfWork.SaveAsync();

                var teamMembersMax = dto.TeamMembersMax
                                     ?? await GetGlobalIntOrDefaultAsync(configRepo, ConfigKeys.Defaults_TeamMembersMax, 4); 
                int? teamLimitMax = dto.TeamLimitMax
                                     ?? await GetGlobalNullableIntAsync(configRepo, ConfigKeys.Defaults_TeamLimitMax);    
                await UpsertConfigAsync(configRepo, ConfigKeys.ContestTeamMembersMax(entity.ContestId), teamMembersMax.ToString());
                if (teamLimitMax.HasValue)
                    await UpsertConfigAsync(configRepo, ConfigKeys.ContestTeamLimitMax(entity.ContestId), teamLimitMax.Value.ToString());

                if (dto.RegistrationStart.HasValue)
                    await UpsertConfigAsync(configRepo, $"contest:{entity.ContestId}:registration_start", dto.RegistrationStart.Value.ToString("o"));
                if (dto.RegistrationEnd.HasValue)
                    await UpsertConfigAsync(configRepo, $"contest:{entity.ContestId}:registration_end", dto.RegistrationEnd.Value.ToString("o"));

                if (!string.IsNullOrWhiteSpace(dto.RewardsText))
                    await UpsertConfigAsync(configRepo, $"contest:{entity.ContestId}:rewards_text", dto.RewardsText!.Trim());

                await _unitOfWork.SaveAsync();
                _unitOfWork.CommitTransaction();

                var created = _mapper.Map<ContestCreatedDTO>(entity);
                created.TeamMembersMax = teamMembersMax;
                created.TeamLimitMax = teamLimitMax;
                created.RewardsText = dto.RewardsText;
                created.RegistrationStart = dto.RegistrationStart;
                created.RegistrationEnd = dto.RegistrationEnd;
                return created;
            }
            catch
            {
                _unitOfWork.RollBack();
                throw;
            }
        }

        public async Task<PublishReadinessDTO> CheckPublishReadinessAsync(Guid contestId)
        {
            var contestRepo = _unitOfWork.GetRepository<Contest>();
            var roundRepo = _unitOfWork.GetRepository<Round>();
            var problemRepo = _unitOfWork.GetRepository<Problem>();
            var configRepo = _unitOfWork.GetRepository<Config>();

            var contest = await contestRepo.Entities
                .Include(c => c.Rounds)
                .FirstOrDefaultAsync(c => c.ContestId == contestId && c.DeletedAt == null);

            if (contest == null)
                throw new ErrorException(StatusCodes.Status404NotFound, ResponseCodeConstants.NOT_FOUND, "Contest not found.");

            var result = new PublishReadinessDTO { ContestId = contestId };

            if (contest.Rounds == null || !contest.Rounds.Any())
                result.Missing.Add("No rounds created.");

            var roundIds = contest.Rounds.Select(r => r.RoundId).ToList();
            if (roundIds.Any())
            {
                var problemCount = await problemRepo.Entities.CountAsync(p => roundIds.Contains(p.RoundId) && p.DeletedAt == null);
                if (problemCount < roundIds.Count)
                    result.Missing.Add("One or more rounds missing a problem.");
            }

            var regStart = await configRepo.Entities
                .Where(c => c.Key == $"contest:{contestId}:registration_start" && c.DeletedAt == null)
                .Select(c => c.Value).FirstOrDefaultAsync();
            var regEnd = await configRepo.Entities
                .Where(c => c.Key == $"contest:{contestId}:registration_end" && c.DeletedAt == null)
                .Select(c => c.Value).FirstOrDefaultAsync();

            if (string.IsNullOrEmpty(regStart) || string.IsNullOrEmpty(regEnd))
                result.Missing.Add("Registration window not configured.");

            var membersMaxContest = await configRepo.Entities
                .Where(c => c.Key == ConfigKeys.ContestTeamMembersMax(contestId) && c.DeletedAt == null)
                .Select(c => c.Value).FirstOrDefaultAsync();

            var membersMaxDefault = await configRepo.Entities
                .Where(c => c.Key == ConfigKeys.Defaults_TeamMembersMax && c.DeletedAt == null)
                .Select(c => c.Value).FirstOrDefaultAsync();

            if (string.IsNullOrEmpty(membersMaxContest) && string.IsNullOrEmpty(membersMaxDefault))
                result.Missing.Add("Team members max not configured (contest or global).");

            result.IsReady = result.Missing.Count == 0;
            return result;
        }

        public async Task PublishIfReadyAsync(Guid contestId)
        {
            var check = await CheckPublishReadinessAsync(contestId);
            if (!check.IsReady)
            {
                var ex = new CoreException("PUBLISH_BLOCKED", "Contest is not ready to publish.", StatusCodes.Status409Conflict)
                {
                    AdditionalData = new Dictionary<string, object> { ["missing"] = check.Missing }
                };
                throw ex;
            }

            var contestRepo = _unitOfWork.GetRepository<Contest>();
            var contest = await contestRepo.GetByIdAsync(contestId);
            if (contest == null || contest.DeletedAt != null)
                throw new ErrorException(StatusCodes.Status404NotFound, ResponseCodeConstants.NOT_FOUND, "Contest not found.");

            contest.Status = ContestStatusEnum.Published.ToString();
            await contestRepo.UpdateAsync(contest);
            await _unitOfWork.SaveAsync();
        }


        private static async Task UpsertConfigAsync(IGenericRepository<Config> repo, string key, string value)
        {
            var existing = await repo.Entities.FirstOrDefaultAsync(c => c.Key == key);
            if (existing == null)
            {
                await repo.InsertAsync(new Config
                {
                    Key = key,
                    Value = value,
                    Scope = "contest",
                    UpdatedAt = DateTime.UtcNow,
                    DeletedAt = null
                });
            }
            else
            {
                existing.Value = value;
                existing.UpdatedAt = DateTime.UtcNow;
                existing.DeletedAt = null;
                await repo.UpdateAsync(existing);
            }
        }

        private static async Task<string> SuggestAlternateNameAsync(string baseName, int year, IGenericRepository<Contest> repo)
        {
            var suffix = 2;
            string candidate;
            do
            {
                candidate = $"{baseName} ({suffix})";
                suffix++;
            }
            while (await repo.Entities.AnyAsync(c => c.Year == year && c.Name == candidate && c.DeletedAt == null));

            return candidate;
        }
        private static async Task<int> GetGlobalIntOrDefaultAsync(IGenericRepository<Config> repo, string key, int @default)
        {
            var value = await repo.Entities.Where(c => c.Key == key && c.DeletedAt == null)
                                           .Select(c => c.Value).FirstOrDefaultAsync();
            return int.TryParse(value, out var n) ? n : @default;
        }

        private static async Task<int?> GetGlobalNullableIntAsync(IGenericRepository<Config> repo, string key)
        {
            var value = await repo.Entities.Where(c => c.Key == key && c.DeletedAt == null)
                                           .Select(c => c.Value).FirstOrDefaultAsync();
            return int.TryParse(value, out var n) ? n : null;
        }

        //public async Task CloseContestAsync(Guid id)
        //{
        //    try
        //    {
        //        // Start a transaction
        //        _unitOfWork.BeginTransaction();

        //        //Get repository for Contest
        //        IGenericRepository<Contest> contestRepo = _unitOfWork.GetRepository<Contest>();

        //        // Fetch the contest by ID
        //        Contest? existingContest = await contestRepo
        //            .Entities
        //            .Where(c => c.ContestId == id)
        //            .Include(c => c.CertificateTemplates)
        //            .FirstOrDefaultAsync();

        //        // Validate if contest exists
        //        if (existingContest == null || existingContest.DeletedAt.HasValue)
        //        {
        //            throw new ErrorException(StatusCodes.Status404NotFound, ResponseCodeConstants.NOT_FOUND, "Contest not found.");
        //        }

        //        // Get repository for LeaderboardEntry
        //        IGenericRepository<LeaderboardEntry> leaderboardRepo = _unitOfWork.GetRepository<LeaderboardEntry>();

        //        // Fetch the leaderboard entry for the given contest ID and rank 1 team
        //        LeaderboardEntry? leaderboardEntry = await leaderboardRepo
        //            .Entities
        //            .Where(le => le.ContestId == id && le.Rank == 1)
        //            .Include(le => le.Team)
        //                .ThenInclude(t => t.TeamMembers)
        //                    .ThenInclude(tm => tm.Student)
        //            .FirstOrDefaultAsync();

        //        // Validate if leaderboard entry exists
        //        if (leaderboardEntry == null)
        //        {
        //            throw new ErrorException(StatusCodes.Status400BadRequest,
        //                ResponseCodeConstants.BADREQUEST,
        //                "Cannot found leaderboard in this contest.");
        //        }

        //        // Get template ID once
        //        Guid templateId = existingContest.CertificateTemplates.First().TemplateId;

        //        // Award certificates to each team member concurrently using separate scopes
        //        IEnumerable<Task> certificateTasks = leaderboardEntry.Team.TeamMembers.Select(async member =>
        //        {
        //            using IServiceScope scope = _serviceProvider.CreateScope();
        //            ICertificateService scopedCertificateService = scope.ServiceProvider.GetRequiredService<ICertificateService>();

        //            await scopedCertificateService.CreateCertificateAsync(new CreateCertificateDTO
        //            {
        //                StudentId = member.StudentId,
        //                TeamId = member.TeamId,
        //                TemplateId = templateId,
        //                FileUrl = string.Empty
        //            });
        //        });

        //        // Wait for all certificate creation tasks to complete
        //        await Task.WhenAll(certificateTasks);

        //        // Set contest status to Closed
        //        existingContest.Status = ContestStatusEnum.Closed.ToString();

        //        // Update the contest
        //        await contestRepo.UpdateAsync(existingContest);

        //        // Save changes to database
        //        await _unitOfWork.SaveAsync();

        //        // Commit the transaction
        //        _unitOfWork.CommitTransaction();
        //    }
        //    catch (Exception ex)
        //    {
        //        // If something fails, roll back the transaction
        //        _unitOfWork.RollBack();
        //        throw new ErrorException(StatusCodes.Status500InternalServerError,
        //            ResponseCodeConstants.INTERNAL_SERVER_ERROR,
        //            $"Error closing Contest: {ex.Message}");
        //    }
        //}
    }
}
