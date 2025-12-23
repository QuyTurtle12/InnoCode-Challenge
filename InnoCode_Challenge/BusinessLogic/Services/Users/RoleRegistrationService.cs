using BusinessLogic.IServices.FileStorages;
using BusinessLogic.IServices.Users;
using DataAccess.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Repository.DTOs.RoleRegistrationDTOs;
using Repository.IRepositories;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Utility.Constant;
using Utility.ExceptionCustom;
using Utility.Helpers;
using Utility.PaginatedList;

namespace BusinessLogic.Services.Users
{
    public class RoleRegistrationService : IRoleRegistrationService
    {
        private readonly IUOW _uow;
        private readonly ICloudinaryService _cloudinary;
        private readonly ILogger<RoleRegistrationService> _logger;
        private static readonly Dictionary<string, string> RoleAliases = new(StringComparer.OrdinalIgnoreCase)
        {
            { "staff", RoleConstants.Staff },
            { "organizer", RoleConstants.ContestOrganizer },
            { "contestorganizer", RoleConstants.ContestOrganizer },
            { "judge", RoleConstants.Judge },
            { "schoolmanager", RoleConstants.SchoolManager },
            { "admin", RoleConstants.Admin }
        };

        public RoleRegistrationService(IUOW uow, ICloudinaryService cloudinary, ILogger<RoleRegistrationService> logger)
        {
            _uow = uow;
            _cloudinary = cloudinary;
            _logger = logger;
        }

        public async Task<RoleRegistrationSubmittedDTO> SubmitAsync(CreateRoleRegistrationDTO dto)
        {
            if (dto == null)
                throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Payload cannot be null.");

            if (!string.Equals(dto.Password, dto.ConfirmPassword, StringComparison.Ordinal))
                throw new ErrorException(StatusCodes.Status400BadRequest, "CONFIRM_PASSWORD_MISMATCH", "Passwords do not match.");

            var requestedRole = NormalizeRequestedRole(dto.RequestedRole);
            var email = NormalizeEmail(dto.Email);

            if (dto.EvidenceFiles == null || dto.EvidenceFiles.Count == 0)
                throw new ErrorException(StatusCodes.Status400BadRequest, "EVIDENCE_REQUIRED", "At least 1 evidence file is required.");

            var userRepo = _uow.GetRepository<User>();
            var regRepo = _uow.GetRepository<RoleRegistration>();
            var evRepo = _uow.GetRepository<RoleRegistrationEvidence>();

            var userExists = await userRepo.Entities.AnyAsync(u => u.DeletedAt == null && u.Email.ToLower() == email);
            if (userExists)
                throw new ErrorException(StatusCodes.Status409Conflict, "EMAIL_EXISTS", "Email is already registered.");

            var pendingExists = await regRepo.Entities.AnyAsync(r =>
                r.DeletedAt == null && r.Email.ToLower() == email && r.Status == RoleRegistrationStatusConstants.Pending);

            if (pendingExists)
                throw new ErrorException(StatusCodes.Status409Conflict, "REGISTRATION_EXISTS", "A pending role registration already exists for this email.");

            var now = DateTime.UtcNow;

            var reg = new RoleRegistration
            {
                RegistrationId = Guid.NewGuid(),
                RequestedRole = requestedRole,
                Fullname = dto.FullName.Trim(),
                Email = email,
                Phone = string.IsNullOrWhiteSpace(dto.Phone) ? null : dto.Phone.Trim(),
                PasswordHash = PasswordHasher.Hash(dto.Password),
                Payload = string.IsNullOrWhiteSpace(dto.Payload) ? null : dto.Payload.Trim(),
                Status = RoleRegistrationStatusConstants.Pending,
                CreatedAt = now
            };

            _uow.BeginTransaction();
            try
            {
                await regRepo.InsertAsync(reg);
                await _uow.SaveAsync();

                var folder = $"role_registrations/{reg.RegistrationId}";

                foreach (var f in dto.EvidenceFiles)
                {
                    var url = await _cloudinary.UploadEvidenceAsync(f, folder);

                    var ext = Path.GetExtension(f.FileName).ToLowerInvariant();
                    var evidence = new RoleRegistrationEvidence
                    {
                        EvidenceId = Guid.NewGuid(),
                        RegistrationId = reg.RegistrationId,
                        Url = url,
                        Type = ext, // ".pdf" ".png" ...
                        Note = null,
                        CreatedAt = now
                    };

                    await evRepo.InsertAsync(evidence);
                }

                await _uow.SaveAsync();
                _uow.CommitTransaction();
            }
            catch (Exception ex)
            {
                _uow.RollBack();
                _logger.LogError(ex, "Submit role registration failed. Email={Email}, Role={Role}", email, requestedRole);
                throw;
            }

            return new RoleRegistrationSubmittedDTO
            {
                RegistrationId = reg.RegistrationId,
                Status = reg.Status,
                CreatedAt = reg.CreatedAt
            };
        }

        public async Task<PaginatedList<RoleRegistrationDTO>> GetAsync(RoleRegistrationQueryParams query)
        {
            if (query.Page < 1 || query.PageSize < 1)
                throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Page and PageSize must be >= 1.");

            var repo = _uow.GetRepository<RoleRegistration>();

            IQueryable<RoleRegistration> q = repo.Entities
                .Where(x => x.DeletedAt == null)
                .AsNoTracking()
                .Include(x => x.RoleRegistrationEvidences);

            if (!string.IsNullOrWhiteSpace(query.Status))
                q = q.Where(x => x.Status == query.Status.Trim().ToLowerInvariant());

            if (!string.IsNullOrWhiteSpace(query.RequestedRole))
                q = q.Where(x => x.RequestedRole == NormalizeRequestedRole(query.RequestedRole));

            if (!string.IsNullOrWhiteSpace(query.EmailContains))
            {
                var k = query.EmailContains.Trim().ToLowerInvariant();
                q = q.Where(x => x.Email.ToLower().Contains(k));
            }

            q = (query.SortBy?.ToLowerInvariant()) switch
            {
                "email" => query.Desc ? q.OrderByDescending(x => x.Email) : q.OrderBy(x => x.Email),
                "status" => query.Desc ? q.OrderByDescending(x => x.Status) : q.OrderBy(x => x.Status),
                "role" => query.Desc ? q.OrderByDescending(x => x.RequestedRole) : q.OrderBy(x => x.RequestedRole),
                _ => query.Desc ? q.OrderByDescending(x => x.CreatedAt) : q.OrderBy(x => x.CreatedAt),
            };

            var page = await repo.GetPagingAsync(q, query.Page, query.PageSize);

            var reviewerIds = page.Items
                .Where(x => x.ReviewedBy.HasValue)
                .Select(x => x.ReviewedBy!.Value)
                .Distinct()
                .ToList();

            var userRepo = _uow.GetRepository<User>();
            var reviewers = await userRepo.Entities.AsNoTracking()
                .Where(u => u.DeletedAt == null && reviewerIds.Contains(u.UserId))
                .Select(u => new { u.UserId, u.Fullname, u.Email })
                .ToDictionaryAsync(x => x.UserId);

            var items = page.Items.Select(x =>
            {
                reviewers.TryGetValue(x.ReviewedBy ?? Guid.Empty, out var rv);

                return new RoleRegistrationDTO
                {
                    RegistrationId = x.RegistrationId,
                    RequestedRole = x.RequestedRole,
                    Fullname = x.Fullname,
                    Email = x.Email,
                    Phone = x.Phone,
                    Status = x.Status,
                    DenyReason = x.DenyReason,
                    ReviewedBy = x.ReviewedBy,
                    ReviewedAt = x.ReviewedAt,
                    CreatedAt = x.CreatedAt,
                    EvidenceCount = x.RoleRegistrationEvidences.Count(e => e.DeletedAt == null),

                    // NEW:
                    ReviewedByName = rv?.Fullname,
                    ReviewedByEmail = rv?.Email
                };
            }).ToList();

            return new PaginatedList<RoleRegistrationDTO>(items, page.TotalCount, page.PageNumber, page.PageSize);
        }

        public async Task<RoleRegistrationDetailDTO> GetByIdAsync(Guid id)
        {
            var repo = _uow.GetRepository<RoleRegistration>();

            var entity = await repo.Entities
                .Include(x => x.RoleRegistrationEvidences)
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.RegistrationId == id && x.DeletedAt == null);

            if (entity == null)
                throw new ErrorException(StatusCodes.Status404NotFound, "ROLE_REG_NOT_FOUND", $"No registration with ID={id}");

            string? reviewerName = null;
            string? reviewerEmail = null;

            if (entity.ReviewedBy.HasValue)
            {
                var userRepo = _uow.GetRepository<User>();
                var reviewer = await userRepo.Entities.AsNoTracking()
                    .Where(u => u.DeletedAt == null && u.UserId == entity.ReviewedBy.Value)
                    .Select(u => new { u.Fullname, u.Email })
                    .FirstOrDefaultAsync();

                reviewerName = reviewer?.Fullname;
                reviewerEmail = reviewer?.Email;
            }
            return new RoleRegistrationDetailDTO
            {
                RegistrationId = entity.RegistrationId,
                RequestedRole = entity.RequestedRole,
                Fullname = entity.Fullname,
                Email = entity.Email,
                Phone = entity.Phone,
                Payload = entity.Payload,
                Status = entity.Status,
                DenyReason = entity.DenyReason,
                ReviewedBy = entity.ReviewedBy,
                ReviewedAt = entity.ReviewedAt,
                CreatedAt = entity.CreatedAt,

                ReviewedByName = reviewerName,
                ReviewedByEmail = reviewerEmail,

                EvidenceCount = entity.RoleRegistrationEvidences.Count(e => e.DeletedAt == null),

                Evidences = entity.RoleRegistrationEvidences
                    .Where(e => e.DeletedAt == null)
                    .OrderByDescending(e => e.CreatedAt)
                    .Select(e => new RoleRegistrationEvidenceDTO
                    {
                        EvidenceId = e.EvidenceId,
                        Url = e.Url,
                        Type = e.Type,
                        Note = e.Note,
                        CreatedAt = e.CreatedAt
                    }).ToList()
            };
        }

        public async Task ApproveAsync(Guid id, Guid reviewerUserId)
        {
            var regRepo = _uow.GetRepository<RoleRegistration>();
            var userRepo = _uow.GetRepository<User>();

            var reg = await regRepo.Entities.FirstOrDefaultAsync(x => x.RegistrationId == id && x.DeletedAt == null);
            if (reg == null)
                throw new ErrorException(StatusCodes.Status404NotFound, "ROLE_REG_NOT_FOUND", $"No registration with ID={id}");

            if (reg.Status != RoleRegistrationStatusConstants.Pending)
                throw new ErrorException(StatusCodes.Status409Conflict, "NOT_PENDING", "Only pending registrations can be approved.");

            var email = reg.Email.Trim().ToLowerInvariant();
            var userExists = await userRepo.Entities.AnyAsync(u => u.DeletedAt == null && u.Email.ToLower() == email);
            if (userExists)
                throw new ErrorException(StatusCodes.Status409Conflict, "EMAIL_EXISTS", "Email is already registered.");

            if (string.IsNullOrWhiteSpace(reg.PasswordHash))
                throw new ErrorException(StatusCodes.Status400BadRequest, "MISSING_PASSWORD", "Registration has no password hash.");

            var now = DateTime.UtcNow;

            _uow.BeginTransaction();
            try
            {
                var user = new User
                {
                    UserId = Guid.NewGuid(),
                    Email = email,
                    Fullname = reg.Fullname.Trim(),
                    PasswordHash = reg.PasswordHash,
                    Role = reg.RequestedRole,
                    Status = UserStatusConstants.Active,
                    CreatedAt = now,
                    UpdatedAt = now
                };

                await userRepo.InsertAsync(user);

                reg.Status = RoleRegistrationStatusConstants.Approved;
                reg.DenyReason = null;
                reg.ReviewedBy = reviewerUserId;
                reg.ReviewedAt = now;

                regRepo.Update(reg);

                await _uow.SaveAsync();
                _uow.CommitTransaction();
            }
            catch (Exception ex)
            {
                _uow.RollBack();
                _logger.LogError(ex, "Approve role registration failed. RegId={RegId}, Reviewer={Reviewer}", id, reviewerUserId);
                throw;
            }
        }

        public async Task DenyAsync(Guid id, string reason, Guid reviewerUserId)
        {
            if (string.IsNullOrWhiteSpace(reason))
                throw new ErrorException(StatusCodes.Status400BadRequest, "REASON_REQUIRED", "Deny reason is required.");

            var regRepo = _uow.GetRepository<RoleRegistration>();

            var reg = await regRepo.Entities.FirstOrDefaultAsync(x => x.RegistrationId == id && x.DeletedAt == null);
            if (reg == null)
                throw new ErrorException(StatusCodes.Status404NotFound, "ROLE_REG_NOT_FOUND", $"No registration with ID={id}");

            if (reg.Status != RoleRegistrationStatusConstants.Pending)
                throw new ErrorException(StatusCodes.Status409Conflict, "NOT_PENDING", "Only pending registrations can be denied.");

            reg.Status = RoleRegistrationStatusConstants.Denied;
            reg.DenyReason = reason.Trim();
            reg.ReviewedBy = reviewerUserId;
            reg.ReviewedAt = DateTime.UtcNow;

            await _uow.SaveAsync();
        }

        // -------- helpers --------

        private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

        private static string NormalizeRequestedRole(string input)
        {
            var r = input?.Trim();

            if (string.IsNullOrWhiteSpace(r))
                throw new ErrorException(StatusCodes.Status400BadRequest, "ROLE_REQUIRED", "RequestedRole is required.");

            if (RoleAliases.TryGetValue(r, out var normalized))
                return normalized;

            throw new ErrorException(
                StatusCodes.Status400BadRequest,
                "INVALID_ROLE",
                "RequestedRole must be staff, organizer, judge, schoolmanager, or admin.");
        }
    }
}
