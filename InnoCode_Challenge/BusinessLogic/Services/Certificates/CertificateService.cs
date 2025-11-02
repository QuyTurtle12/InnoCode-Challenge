using System.Security.Claims;
using AutoMapper;
using BusinessLogic.IServices.Certificates;
using CloudinaryDotNet;
using DataAccess.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Repository.DTOs.CertificateDTOs;
using Repository.IRepositories;
using Utility.Constant;
using Utility.ExceptionCustom;
using Utility.PaginatedList;

namespace BusinessLogic.Services.Certificates
{
    public class CertificateService : ICertificateService
    {
        private readonly IMapper _mapper;
        private readonly IUOW _unitOfWork;
        private readonly IHttpContextAccessor _httpContextAccessor;
        // Constructor
        public CertificateService(IMapper mapper, IUOW unitOfWork, IHttpContextAccessor httpContextAccessor)
        {
            _mapper = mapper;
            _unitOfWork = unitOfWork;
            _httpContextAccessor = httpContextAccessor;
        }

        public async Task AwardCertificateAsync(AwardCertificateDTO dto)
        {
            try
            {
                // Validate input
                if (dto == null)
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest,
                        ResponseCodeConstants.BADREQUEST,
                        "Award certificate data cannot be null.");
                }

                if (dto.templateId == Guid.Empty)
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest,
                        ResponseCodeConstants.BADREQUEST,
                        "Template ID is required.");
                }

                if (dto.teamIdList == null || !dto.teamIdList.Any())
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest,
                        ResponseCodeConstants.BADREQUEST,
                        "At least one team must be selected.");
                }

                // Start a transaction
                _unitOfWork.BeginTransaction();

                // Get repositories
                IGenericRepository<Certificate> certificateRepo = _unitOfWork.GetRepository<Certificate>();
                IGenericRepository<CertificateTemplate> templateRepo = _unitOfWork.GetRepository<CertificateTemplate>();
                IGenericRepository<Team> teamRepo = _unitOfWork.GetRepository<Team>();

                // Verify template exists
                CertificateTemplate? template = await templateRepo.GetByIdAsync(dto.templateId);
                if (template == null)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        $"Certificate template with ID {dto.templateId} not found.");
                }

                // Get all team members for the selected teams
                IList<Team> teams = await teamRepo.Entities
                    .Where(t => dto.teamIdList.Contains(t.TeamId))
                    .Include(t => t.TeamMembers)
                    .ToListAsync();

                // Verify all teams exist
                if (teams.Count != dto.teamIdList.Count)
                {
                    List<Guid> foundTeamIds = teams.Select(t => t.TeamId).ToList();
                    List<Guid> missingTeamIds = dto.teamIdList.Except(foundTeamIds).ToList();
                    throw new ErrorException(StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        $"Teams not found: {string.Join(", ", missingTeamIds)}");
                }

                // Prepare list to hold new certificates
                List<Certificate> certificates = new List<Certificate>();

                // Current timestamp for issuedAt
                DateTime issuedAt = DateTime.UtcNow;

                // Iterate through each team
                foreach (Team team in teams)
                {
                    // Create certificates for all team members
                    foreach (TeamMember member in team.TeamMembers)
                    {
                        Certificate certificate = new Certificate
                        {
                            CertificateId = Guid.NewGuid(),
                            TemplateId = dto.templateId,
                            TeamId = team.TeamId,
                            StudentId = member.StudentId,
                            FileUrl = template.FileUrl ?? "N/A",
                            IssuedAt = issuedAt
                        };

                        // Add to the list
                        certificates.Add(certificate);
                    }
                }

                // Insert all certificates at once
                await certificateRepo.InsertRangeAsync(certificates);

                // Save changes to the database
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
                    $"Error awarding certificates: {ex.Message}");
            }
        }

        public async Task CreateCertificateAsync(CreateCertificateDTO dto)
        {
            try
            {
                // Get the repository for Certificate entity
                IGenericRepository<Certificate> certificateRepo = _unitOfWork.GetRepository<Certificate>();

                // Map DTO to Entity
                Certificate certificate = _mapper.Map<Certificate>(dto);

                // Set additional properties if needed
                certificate.IssuedAt = DateTime.UtcNow;

                // Insert the new certificate
                await certificateRepo.InsertAsync(certificate);

                // Save changes to the database
                await _unitOfWork.SaveAsync();
            }
            catch (Exception ex)
            {
                if (ex is ErrorException)
                {
                    throw;
                }

                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error creating Certificate: {ex.Message}");
            }
        }

        public async Task DeleteCertificateAsync(Guid id)
        {
            try
            {
                // Start a transaction
                _unitOfWork.BeginTransaction();

                // Get the repository for Certificate entity
                IGenericRepository<Certificate> certificateRepo = _unitOfWork.GetRepository<Certificate>();

                // Find the certificate by id
                var certificate = await certificateRepo.GetByIdAsync(id);

                // If certificate not found, throw an exception
                if (certificate == null)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        $"Certificate with ID {id} not found.");
                }

                // Delete the certificate
                await certificateRepo.DeleteAsync(certificate);

                // Save changes to the database
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
                    $"Error deleting Certificate: {ex.Message}");
            }
        }

        public async Task<PaginatedList<GetAllTeamCertificateDTO>> GetPaginatedCertificateAsync(int pageNumber, int pageSize, Guid? idSearch, Guid? contestIdSearch, Guid? teamIdSearch, Guid? studentIdSearch, string? certificateNameSearch, string? teamName, string? studentNameSearch)
        {
            try
            {
                // Validate pageNumber and pageSize
                if (pageNumber < 1 || pageSize < 1)
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest,
                        ResponseCodeConstants.BADREQUEST,
                        "Page number and page size must be greater than or equal to 1.");
                }

                // Get the repository for Certificate entity
                IGenericRepository<Certificate> certificateRepo = _unitOfWork.GetRepository<Certificate>();

                // Build query with all necessary includes
                IQueryable<Certificate> query = certificateRepo
                    .Entities
                    .Include(c => c.Template)
                        .ThenInclude(t => t.Contest)
                    .Include(c => c.Team)
                        .ThenInclude(t => t!.TeamMembers)
                            .ThenInclude(tm => tm.Student)
                                .ThenInclude(s => s!.User)
                    .Include(c => c.Student)
                        .ThenInclude(s => s!.User);

                // Apply contest filter
                if (contestIdSearch.HasValue)
                {
                    query = query.Where(c => c.Template.ContestId == contestIdSearch.Value);
                }

                // Apply filters
                if (idSearch.HasValue)
                {
                    query = query.Where(c => c.CertificateId == idSearch.Value);
                }

                if (teamIdSearch.HasValue)
                {
                    query = query.Where(c => c.TeamId == teamIdSearch.Value);
                }

                if (studentIdSearch.HasValue)
                {
                    query = query.Where(c => c.StudentId == studentIdSearch.Value);
                }

                if (!string.IsNullOrWhiteSpace(teamName))
                {
                    query = query.Where(c => c.Team != null && c.Team.Name.Contains(teamName));
                }

                if (!string.IsNullOrWhiteSpace(studentNameSearch))
                {
                    query = query.Where(c => c.Student != null && c.Student.User.Fullname.Contains(studentNameSearch));
                }

                if (!string.IsNullOrWhiteSpace(certificateNameSearch))
                {
                    query = query.Where(c => c.Template.Name.Contains(certificateNameSearch));
                }

                // Order by issued date
                query = query.OrderByDescending(c => c.IssuedAt);

                // Execute query to get all matching certificates
                List<Certificate> allCertificates = await query.ToListAsync();

                // Group by TemplateId and TeamId
                var groupedCertificates = allCertificates
                    .GroupBy(c => new { c.TemplateId, c.TeamId })
                    .Select(group =>
                    {
                        // Get the first certificate to extract common information
                        Certificate firstCert = group.First();

                        // Create DTO with grouped data
                        GetAllTeamCertificateDTO dto = new GetAllTeamCertificateDTO
                        {
                            TemplateId = firstCert.TemplateId,
                            TemplateName = firstCert.Template.Name,
                            ContestId = firstCert.Template?.ContestId ?? Guid.Empty,
                            ContestName = firstCert.Template?.Contest?.Name ?? "N/A",
                            TeamId = firstCert.TeamId ?? Guid.Empty,
                            TeamName = firstCert.Team?.Name ?? "N/A",
                            FileUrl = firstCert.FileUrl,
                            IssuedAt = firstCert.IssuedAt,

                            // Collect all team members from this group
                            TeamDetails = group.Select(cert => new TeamDetailDTO
                            {
                                CertificateId = cert.CertificateId,
                                StudentId = cert.StudentId ?? Guid.Empty,
                                StudentName = cert.Student?.User?.Fullname ?? "N/A",
                            }).ToList()
                        };

                        return dto;
                    })
                    .ToList();

                // Apply pagination to grouped results
                int totalCount = groupedCertificates.Count;
                var paginatedGroups = groupedCertificates
                    .Skip((pageNumber - 1) * pageSize)
                    .Take(pageSize)
                    .ToList();

                // Create and return paginated list
                return new PaginatedList<GetAllTeamCertificateDTO>(
                    paginatedGroups,
                    totalCount,
                    pageNumber,
                    pageSize
                );
            }
            catch (Exception ex)
            {
                if (ex is ErrorException)
                {
                    throw;
                }

                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error fetching Certificates: {ex.Message}");
            }
        }

        public async Task<PaginatedList<GetMyCertificateDTO>> GetMyPaginatedCertificateAsync(int pageNumber, int pageSize, Guid? idSearch, Guid? contestIdSearch, string? contestNameSearch)
        {
            try
            {
                // Get the repository for Certificate entity
                IGenericRepository<Certificate> certificateRepo = _unitOfWork.GetRepository<Certificate>();

                IQueryable<Certificate> query;

                // Get user ID from JWT token
                string? userId = _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier)
                    ?? throw new ErrorException(StatusCodes.Status400BadRequest,
                        ResponseCodeConstants.BADREQUEST,
                        $"Null User Id");

                // Get student ID from user ID
                IGenericRepository<Student> studentRepo = _unitOfWork.GetRepository<Student>();
                Guid studentId = studentRepo.Entities.Where(s => s.UserId.ToString() == userId)
                    .Select(s => s.StudentId)
                    .FirstOrDefault();

                // Get certificates for the logged in student
                query = certificateRepo
                    .Entities
                    .Where(c => c.StudentId == studentId)
                    .Include(c => c.Template)
                        .ThenInclude(c => c.Contest)
                    .Include(c => c.Team);

                // Apply filters if provided
                if (idSearch.HasValue)
                {
                    query = query.Where(c => c.CertificateId == idSearch.Value);
                }

                if (contestIdSearch.HasValue)
                {
                    query = query.Where(c => c.Template.ContestId == contestIdSearch.Value);
                }

                if (!string.IsNullOrWhiteSpace(contestNameSearch))
                {
                    query = query.Where(c => c.Template.Contest.Name.Contains(contestNameSearch));
                }

                query = query.OrderByDescending(c => c.IssuedAt);

                PaginatedList<Certificate> resultQuery = await certificateRepo.GetPagingAsync(query, pageNumber, pageSize);

                // Project to DTO
                IReadOnlyCollection<GetMyCertificateDTO> result = resultQuery.Items.Select(items =>
                {
                    // Map basic properties
                    GetMyCertificateDTO certificateDTO = _mapper.Map<GetMyCertificateDTO>(items);

                    // Map additional properties
                    certificateDTO.ContestId = items.Template.ContestId;
                    certificateDTO.ContestName = items.Template.Contest != null ? items.Template.Contest.Name : "N/A";
                    certificateDTO.TemplateName = items.Template.Name;

                    return certificateDTO;
                }).ToList();

                // Create and return paginated list
                return new PaginatedList<GetMyCertificateDTO>(
                    result,
                    resultQuery.TotalCount,
                    resultQuery.PageNumber,
                    resultQuery.PageSize
                );
            }
            catch (Exception ex)
            {
                if (ex is ErrorException)
                {
                    throw;
                }

                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error fetching Certificates: {ex.Message}");
            }
        }
    }
}
