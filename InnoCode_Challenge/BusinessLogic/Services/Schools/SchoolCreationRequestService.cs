using BusinessLogic.IServices.FileStorages;
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

        public SchoolCreationRequestService(IUOW uow, ICloudinaryService cloudinary, ILogger<SchoolCreationRequestService> logger)
        {
            _uow = uow;
            _cloudinary = cloudinary;
            _logger = logger;
        }

        private static string DetectTypeFromFileName(string fileName)
        {
            var ext = Path.GetExtension(fileName).ToLowerInvariant();
            if (ext == ".pdf") return "pdf";
            if (ext == ".png" || ext == ".jpg" || ext == ".jpeg") return "image";
            return "file";
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

                var files = dto.Evidences ?? new List<IFormFile>();
                foreach (var f in files)
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

            return await GetByIdAsync(req.RequestId, requestedByUserId, RoleConstants.SchoolManager);
        }

        public async Task<PaginatedList<SchoolCreationRequestListDTO>> GetListAsync(SchoolCreationRequestQueryParams query)
        {
            var repo = _uow.GetRepository<SchoolCreationRequest>();

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

            var page = await repo.GetPagingAsync(q, query.Page, query.PageSize);

            var items = page.Items.Select(x => new SchoolCreationRequestListDTO
            {
                RequestId = x.RequestId,
                RequestedByUserId = x.RequestedByUserId,
                Name = x.Name,
                ProvinceId = x.ProvinceId,
                Status = x.Status,
                ReviewedBy = x.ReviewedBy,
                ReviewedAt = x.ReviewedAt,
                DenyReason = x.DenyReason,
                CreatedSchoolId = x.CreatedSchoolId,
                CreatedAt = x.CreatedAt
            }).ToList();

            return new PaginatedList<SchoolCreationRequestListDTO>(items, page.TotalCount, page.PageNumber, page.PageSize);
        }

        public async Task<PaginatedList<SchoolCreationRequestListDTO>> GetMyAsync(Guid requestedByUserId, SchoolCreationRequestQueryParams query)
        {
            var repo = _uow.GetRepository<SchoolCreationRequest>();

            IQueryable<SchoolCreationRequest> q = repo.Entities
                .AsNoTracking()
                .Where(x => x.DeletedAt == null && x.RequestedByUserId == requestedByUserId);

            if (!string.IsNullOrWhiteSpace(query.Status))
                q = q.Where(x => x.Status == query.Status.Trim().ToLowerInvariant());

            q = q.OrderByDescending(x => x.CreatedAt);

            var page = await repo.GetPagingAsync(q, query.Page, query.PageSize);

            var items = page.Items.Select(x => new SchoolCreationRequestListDTO
            {
                RequestId = x.RequestId,
                RequestedByUserId = x.RequestedByUserId,
                Name = x.Name,
                ProvinceId = x.ProvinceId,
                Status = x.Status,
                ReviewedBy = x.ReviewedBy,
                ReviewedAt = x.ReviewedAt,
                DenyReason = x.DenyReason,
                CreatedSchoolId = x.CreatedSchoolId,
                CreatedAt = x.CreatedAt
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

            // access rule: staff/admin see all; school_manager sees own
            var role = (requesterRole ?? "").ToLowerInvariant();
            var isStaffOrAdmin = role == RoleConstants.Admin || role == RoleConstants.Staff;
            if (!isStaffOrAdmin && req.RequestedByUserId != requesterUserId)
                throw new ErrorException(StatusCodes.Status403Forbidden, "FORBIDDEN", "You are not allowed to view this request.");

            return new SchoolCreationRequestDetailDTO
            {
                RequestId = req.RequestId,
                RequestedByUserId = req.RequestedByUserId,
                Name = req.Name,
                Address = req.Address,
                ProvinceId = req.ProvinceId,
                Contact = req.Contact,
                Status = req.Status,
                ReviewedBy = req.ReviewedBy,
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
            }
            catch
            {
                _uow.RollBack();
                throw;
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

            req.Status = SchoolCreationRequestStatus.Denied;
            req.ReviewedBy = reviewerUserId;
            req.ReviewedAt = DateTime.UtcNow;
            req.DenyReason = denyReason.Trim();

            reqRepo.Update(req);
            await _uow.SaveAsync();
        }
    }
}
