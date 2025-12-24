using BusinessLogic.IServices.FileStorages;
using BusinessLogic.IServices.NotificationsAndLogs;
using BusinessLogic.IServices.Schools;
using DataAccess.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Repository.DTOs.SchoolDTOs;
using Repository.IRepositories;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Utility.Constant;
using Utility.ExceptionCustom;
using Utility.PaginatedList;

namespace BusinessLogic.Services.Schools
{
    public class SchoolCreationRequestService : ISchoolCreationRequestService
    {
        private readonly IUOW _uow;
        private readonly ICloudinaryService _cloudinary;
        private readonly ILogger<SchoolCreationRequestService> _logger;
        private readonly INotificationService _notificationService;
        private readonly IActivityLogWriter _logWriter;
        private static readonly HashSet<string> AllowedEvidenceExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".pdf", ".png", ".jpg", ".jpeg", ".doc", ".docx"
        };

        public SchoolCreationRequestService(
            IUOW uow,
            ICloudinaryService cloudinary,
            ILogger<SchoolCreationRequestService> logger,
            INotificationService notificationService,
            IActivityLogWriter logWriter)
        {
            _uow = uow;
            _cloudinary = cloudinary;
            _logger = logger;
            _notificationService = notificationService;
            _logWriter = logWriter;
        }


        private static string DetectTypeFromFileName(string fileName)
        {
            var ext = Path.GetExtension(fileName).ToLowerInvariant();
            if (ext == ".pdf") return "pdf";
            if (ext == ".png" || ext == ".jpg" || ext == ".jpeg") return "image";
            if (ext == ".doc" || ext == ".docx") return "doc";
            return "file";
        }

        private static void ValidateEvidenceFiles(IReadOnlyCollection<IFormFile>? files)
        {
            if (files == null || files.Count == 0)
                throw new ErrorException(StatusCodes.Status400BadRequest, "EVIDENCE_REQUIRED", "At least 1 evidence file is required.");

            foreach (var f in files)
            {
                var ext = Path.GetExtension(f.FileName);
                if (!AllowedEvidenceExtensions.Contains(ext))
                {
                    throw new ErrorException(
                        StatusCodes.Status400BadRequest,
                        "INVALID_EVIDENCE_TYPE",
                        "Evidence must be .pdf, .png, .jpg, .jpeg, .doc, or .docx.");
                }
            }
        }

        private static void ValidatePagination(SchoolCreationRequestQueryParams query)
        {
            if (query.PageNumber < 1 || query.PageSize < 1)
                throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "PageNumber and PageSize must be >= 1.");
        }

        public async Task<SchoolCreationRequestDetailDTO> CreateAsync(CreateSchoolCreationRequestFormDTO dto, Guid requestedByUserId)
        {
            if (dto == null)
                throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Payload cannot be null.");

            var provinceRepo = _uow.GetRepository<Province>();
            var reqRepo = _uow.GetRepository<SchoolCreationRequest>();
            var evRepo = _uow.GetRepository<SchoolCreationRequestEvidence>();

            var provinceOk = await provinceRepo.Entities.AnyAsync(p => p.ProvinceId == dto.ProvinceId);
            if (!provinceOk)
                throw new ErrorException(StatusCodes.Status404NotFound, "PROVINCE_NOT_FOUND", "Province not found.");

            ValidateEvidenceFiles(dto.Evidences);

            var now = DateTime.UtcNow;

            var req = new SchoolCreationRequest
            {
                RequestId = Guid.NewGuid(),
                RequestedByUserId = requestedByUserId,
                Name = dto.Name.Trim(),
                Address = string.IsNullOrWhiteSpace(dto.Address) ? null : dto.Address.Trim(),
                ProvinceId = dto.ProvinceId,
                Contact = string.IsNullOrWhiteSpace(dto.Contact) ? null : dto.Contact.Trim(),
                Status = SchoolCreationRequestStatus.Pending,
                CreatedAt = now
            };

            _uow.BeginTransaction();
            try
            {
                await reqRepo.InsertAsync(req);
                await _uow.SaveAsync();

                foreach (var f in dto.Evidences!)
                {
                    var url = await _cloudinary.UploadFileAsync(f, folder: $"school-requests/{req.RequestId}");
                    await evRepo.InsertAsync(new SchoolCreationRequestEvidence
                    {
                        EvidenceId = Guid.NewGuid(),
                        RequestId = req.RequestId,
                        Url = url,
                        Type = DetectTypeFromFileName(f.FileName),
                        CreatedAt = now
                    });
                }

                await _uow.SaveAsync();
                _uow.CommitTransaction();
            }
            catch
            {
                _uow.RollBack();
                throw;
            }

            await _logWriter.TryWriteAsync(
                requestedByUserId,
                ActivityActions.SchoolRequestCreate,
                TargetTypes.SchoolCreationRequest,
                req.RequestId.ToString());

            try
            {
                var staffAdminIds = await GetStaffAdminIdsAsync();

                await _notificationService.CreateInAppToUsersAsync(
                    staffAdminIds,
                    NotificationTypes.SchoolCreationRequestSubmitted,
                    new
                    {
                        requestId = req.RequestId,
                        requestedByUserId = req.RequestedByUserId,
                        name = req.Name,
                        provinceId = req.ProvinceId,
                        status = req.Status,
                        targetType = TargetTypes.SchoolCreationRequest,
                        targetId = req.RequestId.ToString(),
                        message = $"New school creation request: {req.Name}"
                    });

            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send notifications for school request create. RequestId={RequestId}", req.RequestId);
            }


            return await GetByIdAsync(req.RequestId, requestedByUserId, RoleConstants.SchoolManager);
        }

        public async Task<PaginatedList<SchoolCreationRequestListDTO>> GetListAsync(SchoolCreationRequestQueryParams query)
        {
            var repo = _uow.GetRepository<SchoolCreationRequest>();
            ValidatePagination(query);

            IQueryable<SchoolCreationRequest> q = repo.Entities
                .AsNoTracking()
                .Where(x => x.DeletedAt == null);

            if (!string.IsNullOrWhiteSpace(query.Status))
                q = q.Where(x => x.Status == query.Status.Trim().ToLowerInvariant());

            if (!string.IsNullOrWhiteSpace(query.Search))
            {
                var k = query.Search.Trim().ToLowerInvariant();
                q = q.Where(x => x.Name.ToLower().Contains(k) || (x.Contact != null && x.Contact.ToLower().Contains(k)));
            }

            q = q.OrderByDescending(x => x.CreatedAt);

            var page = await repo.GetPagingAsync(q, query.PageNumber, query.PageSize);

            var requestedByIds = page.Items.Select(x => x.RequestedByUserId).Distinct().ToList();
            var reviewerIds = page.Items.Where(x => x.ReviewedBy.HasValue)
                                        .Select(x => x.ReviewedBy!.Value)
                                        .Distinct()
                                        .ToList();
            var allUserIds = requestedByIds.Concat(reviewerIds).Distinct().ToList();

            var provinceIds = page.Items.Select(x => x.ProvinceId).Distinct().ToList();

            var userRepo = _uow.GetRepository<User>();
            var users = await userRepo.Entities.AsNoTracking()
                .Where(u => u.DeletedAt == null && allUserIds.Contains(u.UserId))
                .Select(u => new { u.UserId, u.Fullname, u.Email })
                .ToDictionaryAsync(x => x.UserId);

            var provinceRepo = _uow.GetRepository<Province>();
            var provinces = await provinceRepo.Entities.AsNoTracking()
                .Where(p => provinceIds.Contains(p.ProvinceId))
                .Select(p => new { p.ProvinceId, Name = p.Name })
                .ToDictionaryAsync(x => x.ProvinceId, x => x.Name);

            var items = page.Items.Select(x =>
            {
                users.TryGetValue(x.RequestedByUserId, out var reqUser);

                users.TryGetValue(x.ReviewedBy ?? Guid.Empty, out var reviewedUser);

                provinces.TryGetValue(x.ProvinceId, out var provinceName);

                return new SchoolCreationRequestListDTO
                {
                    RequestId = x.RequestId,

                    RequestedByUserId = x.RequestedByUserId,
                    RequestedByName = reqUser?.Fullname,
                    RequestedByEmail = reqUser?.Email,

                    Name = x.Name,

                    ProvinceId = x.ProvinceId,
                    ProvinceName = provinceName,

                    Status = x.Status,

                    ReviewedBy = x.ReviewedBy,
                    ReviewedByName = reviewedUser?.Fullname,
                    ReviewedByEmail = reviewedUser?.Email,

                    ReviewedAt = x.ReviewedAt,
                    DenyReason = x.DenyReason,
                    CreatedSchoolId = x.CreatedSchoolId,
                    CreatedAt = x.CreatedAt
                };
            }).ToList();

            return new PaginatedList<SchoolCreationRequestListDTO>(items, page.TotalCount, page.PageNumber, page.PageSize);
        }
        public async Task<PaginatedList<SchoolCreationRequestListDTO>> GetMyAsync(Guid requestedByUserId, SchoolCreationRequestQueryParams query)
        {
            var repo = _uow.GetRepository<SchoolCreationRequest>();
            ValidatePagination(query);

            IQueryable<SchoolCreationRequest> q = repo.Entities
                .AsNoTracking()
                .Where(x => x.DeletedAt == null && x.RequestedByUserId == requestedByUserId);

            if (!string.IsNullOrWhiteSpace(query.Status))
                q = q.Where(x => x.Status == query.Status.Trim().ToLowerInvariant());

            q = q.OrderByDescending(x => x.CreatedAt);

            var page = await repo.GetPagingAsync(q, query.PageNumber, query.PageSize);

            var requestedByIds = page.Items.Select(x => x.RequestedByUserId).Distinct().ToList();
            var reviewerIds = page.Items.Where(x => x.ReviewedBy.HasValue)
                                        .Select(x => x.ReviewedBy!.Value)
                                        .Distinct()
                                        .ToList();
            var allUserIds = requestedByIds.Concat(reviewerIds).Distinct().ToList();

            var provinceIds = page.Items.Select(x => x.ProvinceId).Distinct().ToList();

            var userRepo = _uow.GetRepository<User>();
            var users = await userRepo.Entities.AsNoTracking()
                .Where(u => u.DeletedAt == null && allUserIds.Contains(u.UserId))
                .Select(u => new { u.UserId, u.Fullname, u.Email })
                .ToDictionaryAsync(x => x.UserId);

            var provinceRepo = _uow.GetRepository<Province>();
            var provinces = await provinceRepo.Entities.AsNoTracking()
                .Where(p => provinceIds.Contains(p.ProvinceId))
                .Select(p => new { p.ProvinceId, Name = p.Name })
                .ToDictionaryAsync(x => x.ProvinceId, x => x.Name);

            var items = page.Items.Select(x =>
            {
                users.TryGetValue(x.RequestedByUserId, out var reqUser);

                users.TryGetValue(x.ReviewedBy ?? Guid.Empty, out var reviewedUser);

                provinces.TryGetValue(x.ProvinceId, out var provinceName);

                return new SchoolCreationRequestListDTO
                {
                    RequestId = x.RequestId,

                    RequestedByUserId = x.RequestedByUserId,
                    RequestedByName = reqUser?.Fullname,
                    RequestedByEmail = reqUser?.Email,

                    Name = x.Name,

                    ProvinceId = x.ProvinceId,
                    ProvinceName = provinceName,

                    Status = x.Status,

                    ReviewedBy = x.ReviewedBy,
                    ReviewedByName = reviewedUser?.Fullname,
                    ReviewedByEmail = reviewedUser?.Email,

                    ReviewedAt = x.ReviewedAt,
                    DenyReason = x.DenyReason,
                    CreatedSchoolId = x.CreatedSchoolId,
                    CreatedAt = x.CreatedAt
                };
            }).ToList();


            return new PaginatedList<SchoolCreationRequestListDTO>(items, page.TotalCount, page.PageNumber, page.PageSize);
        }

        public async Task<SchoolCreationRequestDetailDTO> GetByIdAsync(Guid requestId, Guid requesterUserId, string requesterRole)
        {
            var repo = _uow.GetRepository<SchoolCreationRequest>();

            var req = await repo.Entities
                .AsNoTracking()
                .Include(x => x.SchoolCreationRequestEvidences)
                .FirstOrDefaultAsync(x => x.RequestId == requestId && x.DeletedAt == null)
                ?? throw new ErrorException(StatusCodes.Status404NotFound, "REQ_NOT_FOUND", "School creation request not found.");

            var role = (requesterRole ?? "").ToLowerInvariant();
            var isStaffOrAdmin = role == RoleConstants.Admin.ToLowerInvariant() || role == RoleConstants.Staff.ToLowerInvariant();
            if (!isStaffOrAdmin && req.RequestedByUserId != requesterUserId)
                throw new ErrorException(StatusCodes.Status403Forbidden, "FORBIDDEN", "You are not allowed to view this request.");
            var userRepo = _uow.GetRepository<User>();
            var ids = new List<Guid> { req.RequestedByUserId };
            if (req.ReviewedBy.HasValue) ids.Add(req.ReviewedBy.Value);

            var users = await userRepo.Entities.AsNoTracking()
                .Where(u => u.DeletedAt == null && ids.Contains(u.UserId))
                .Select(u => new { u.UserId, u.Fullname, u.Email })
                .ToDictionaryAsync(x => x.UserId);

            users.TryGetValue(req.RequestedByUserId, out var reqUser);
            users.TryGetValue(req.ReviewedBy ?? Guid.Empty, out var revUser);

            var provinceRepo = _uow.GetRepository<Province>();
            var provinceName = await provinceRepo.Entities.AsNoTracking()
                .Where(p => p.ProvinceId == req.ProvinceId)
                .Select(p => p.Name)
                .FirstOrDefaultAsync();

            return new SchoolCreationRequestDetailDTO
            {
                RequestId = req.RequestId,

                RequestedByUserId = req.RequestedByUserId,
                RequestedByName = reqUser?.Fullname,
                RequestedByEmail = reqUser?.Email,

                Name = req.Name,
                Address = req.Address,

                ProvinceId = req.ProvinceId,
                ProvinceName = provinceName,

                Contact = req.Contact,
                Status = req.Status,

                ReviewedBy = req.ReviewedBy,
                ReviewedByName = revUser?.Fullname,
                ReviewedByEmail = revUser?.Email,

                ReviewedAt = req.ReviewedAt,
                DenyReason = req.DenyReason,
                CreatedSchoolId = req.CreatedSchoolId,
                CreatedAt = req.CreatedAt,

                Evidences = req.SchoolCreationRequestEvidences
                    .Where(e => e.DeletedAt == null)
                    .OrderByDescending(e => e.CreatedAt)
                    .Select(e => new SchoolCreationRequestEvidenceDTO
                    {
                        EvidenceId = e.EvidenceId,
                        Url = e.Url,
                        Type = e.Type,
                        CreatedAt = e.CreatedAt
                    }).ToList()
            };

        }

        public async Task ApproveAsync(Guid requestId, Guid reviewerUserId)
        {
            var reqRepo = _uow.GetRepository<SchoolCreationRequest>();
            var schoolRepo = _uow.GetRepository<School>();

            var req = await reqRepo.Entities.FirstOrDefaultAsync(x => x.RequestId == requestId && x.DeletedAt == null)
                ?? throw new ErrorException(StatusCodes.Status404NotFound, "REQ_NOT_FOUND", "Request not found.");

            if (req.Status != SchoolCreationRequestStatus.Pending)
                throw new ErrorException(StatusCodes.Status409Conflict, "NOT_PENDING", "Only pending requests can be approved.");

            Guid createdSchoolId;
            var requesterId = req.RequestedByUserId;
            var schoolName = req.Name;

            _uow.BeginTransaction();
            try
            {
                var now = DateTime.UtcNow;

                var school = new School
                {
                    SchoolId = Guid.NewGuid(),
                    Name = req.Name.Trim(),
                    ProvinceId = req.ProvinceId,
                    Contact = req.Contact,
                    Address = req.Address,
                    ManagerUserId = req.RequestedByUserId,
                    CreatedAt = now
                };

                await schoolRepo.InsertAsync(school);
                await _uow.SaveAsync();

                req.Status = SchoolCreationRequestStatus.Approved;
                req.ReviewedBy = reviewerUserId;
                req.ReviewedAt = now;
                req.DenyReason = null;
                req.CreatedSchoolId = school.SchoolId;

                reqRepo.Update(req);
                await _uow.SaveAsync();

                _uow.CommitTransaction();

                createdSchoolId = school.SchoolId;
            }
            catch
            {
                _uow.RollBack();
                throw;
            }

            await _logWriter.TryWriteAsync(
                reviewerUserId,
                ActivityActions.SchoolRequestApprove,
                TargetTypes.SchoolCreationRequest,
                requestId.ToString());

            try
            {
                await _notificationService.CreateInAppToUserAsync(
                    requesterId,
                    NotificationTypes.SchoolCreationRequestApproved,
                    new
                    {
                        requestId,
                        createdSchoolId,
                        name = schoolName,
                        targetType = TargetTypes.School,
                        targetId = createdSchoolId.ToString(),
                        message = $"Your school '{schoolName}' has been approved."
                    });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to notify requester after approve. RequestId={RequestId}", requestId);
            }
        }


        public async Task DenyAsync(Guid requestId, Guid reviewerUserId, string denyReason)
        {
            if (string.IsNullOrWhiteSpace(denyReason))
                throw new ErrorException(StatusCodes.Status400BadRequest, "DENY_REASON_REQUIRED", "DenyReason is required.");

            var reqRepo = _uow.GetRepository<SchoolCreationRequest>();
            var req = await reqRepo.Entities.FirstOrDefaultAsync(x => x.RequestId == requestId && x.DeletedAt == null)
                ?? throw new ErrorException(StatusCodes.Status404NotFound, "REQ_NOT_FOUND", "Request not found.");

            if (req.Status != SchoolCreationRequestStatus.Pending)
                throw new ErrorException(StatusCodes.Status409Conflict, "NOT_PENDING", "Only pending requests can be denied.");

            var requesterId = req.RequestedByUserId;
            var schoolName = req.Name;
            var reason = denyReason.Trim();

            req.Status = SchoolCreationRequestStatus.Denied;
            req.ReviewedBy = reviewerUserId;
            req.ReviewedAt = DateTime.UtcNow;
            req.DenyReason = reason;

            reqRepo.Update(req);
            await _uow.SaveAsync();

            await _logWriter.TryWriteAsync(
                reviewerUserId,
                ActivityActions.SchoolRequestDeny,
                TargetTypes.SchoolCreationRequest,
                requestId.ToString());

            try
            {
                await _notificationService.CreateInAppToUserAsync(
                    requesterId,
                    NotificationTypes.SchoolCreationRequestDenied,
                    new
                    {
                        requestId,
                        denyReason = reason,
                        name = schoolName,
                        targetType = TargetTypes.SchoolCreationRequest,
                        targetId = requestId.ToString(),
                        message = $"Your school creation request '{schoolName}' was rejected."
                    });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to notify requester after deny. RequestId={RequestId}", requestId);
            }
        }

        private async Task<List<Guid>> GetStaffAdminIdsAsync()
        {
            var userRepo = _uow.GetRepository<User>();
            return await userRepo.Entities
                .AsNoTracking()
                .Where(u => u.DeletedAt == null
                    && (u.Role == RoleConstants.Admin || u.Role == RoleConstants.Staff))
                .Select(u => u.UserId)
                .ToListAsync();
        }

    }
}
