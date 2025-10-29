using AutoMapper;
using BusinessLogic.IServices.Certificates;
using BusinessLogic.IServices.FileStorages;
using DataAccess.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Repository.DTOs.CertificateTemplateDTOs;
using Repository.IRepositories;
using Utility.Constant;
using Utility.ExceptionCustom;
using Utility.PaginatedList;

namespace BusinessLogic.Services.Certificates
{
    public class CertificateTemplateService : ICertificateTemplateService
    {
        private readonly IMapper _mapper;
        private readonly IUOW _unitOfWork;
        private readonly ICloudinaryService _cloudinaryService;

        private const long MAX_FILE_SIZE = 10 * 1024 * 1024; // 10MB

        // Constructor
        public CertificateTemplateService(IMapper mapper, IUOW unitOfWork, ICloudinaryService cloudinaryService)
        {
            _mapper = mapper;
            _unitOfWork = unitOfWork;
            _cloudinaryService = cloudinaryService;
        }

        public async Task CreateCertificateTemplateAsync(IFormFile file, CreateCertificateTemplateDTO templateDTO)
        {
            try
            {
                // Validate file
                if (file == null || file.Length == 0)
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest,
                        ResponseCodeConstants.BADREQUEST,
                        "Certificate template file is required.");
                }

                // Validate file type
                List<string> allowedExtensions = new List<string>{ ".pdf", ".png", ".jpg", ".jpeg" };
                string fileExtension = Path.GetExtension(file.FileName).ToLower();
                if (!allowedExtensions.Contains(fileExtension))
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest,
                        ResponseCodeConstants.BADREQUEST,
                        $"File type {fileExtension} is not supported. Allowed types: {string.Join(", ", allowedExtensions)}");
                }

                // Validate file size
                const long maxFileSize = 10 * 1024 * 1024; // 10MB
                if (file.Length > MAX_FILE_SIZE)
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest,
                        ResponseCodeConstants.BADREQUEST,
                        $"File size exceeds the maximum allowed size of {maxFileSize / (1024 * 1024)}MB.");
                }

                // Begin transaction
                _unitOfWork.BeginTransaction();

                // Get repository for CertificateTemplate entity
                IGenericRepository<CertificateTemplate> certificateTemplateRepo = _unitOfWork.GetRepository<CertificateTemplate>();

                // Get repository for Contest entity to check if contest exists
                IGenericRepository<Contest> contestRepo = _unitOfWork.GetRepository<Contest>();

                // Check if contest exists
                Contest? contestExists = await contestRepo.GetByIdAsync(templateDTO.ContestId);
                if (contestExists == null)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound, ResponseCodeConstants.NOT_FOUND, $"Contest with ID {templateDTO.ContestId} not found.");
                }

                // Upload file to Cloudinary
                string fileUrl = await _cloudinaryService.UploadFileAsync(file, "certificate templates");

                // Create new template entity and map from DTO
                CertificateTemplate template = _mapper.Map<CertificateTemplate>(templateDTO);

                // Generate new ID for the template
                template.TemplateId = Guid.NewGuid();

                // Set the Cloudinary file URL
                template.FileUrl = fileUrl;

                // Insert the new template
                await certificateTemplateRepo.InsertAsync(template);

                // Save changes
                await _unitOfWork.SaveAsync();

                // Commit the transaction
                _unitOfWork.CommitTransaction();
            }
            catch (Exception ex)
            {
                // If something fails, roll back the transaction
                _unitOfWork.RollBack();
                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error creating Certificate Template: {ex.Message}");
            }
        }

        public async Task DeleteCertificateTemplateAsync(Guid id)
        {
            try
            {
                // Begin transaction
                _unitOfWork.BeginTransaction();

                // Get repository for CertificateTemplate entity
                IGenericRepository<CertificateTemplate> certificateTemplateRepo = _unitOfWork.GetRepository<CertificateTemplate>();

                // Find the template
                CertificateTemplate? template = await certificateTemplateRepo.GetByIdAsync(id);

                if (template == null)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound, ResponseCodeConstants.NOT_FOUND, $"Certificate template with ID {id} not found.");
                }

                // Get repository for Certificate entity
                IGenericRepository<Certificate> certificateRepo = _unitOfWork.GetRepository<Certificate>();

                // Check if any certificates reference this template
                bool certificatesExist = certificateRepo.Entities.Any(c => c.TemplateId == id);

                // If there are certificates using this template, prevent deletion
                if (certificatesExist)
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Cannot delete this template because there are certificates using it.");
                }

                // Delete the template
                await certificateTemplateRepo.DeleteAsync(template);

                // Save changes
                await _unitOfWork.SaveAsync();

                // Commit the transaction
                _unitOfWork.CommitTransaction();
            }
            catch (Exception ex)
            {
                // If something fails, roll back the transaction
                _unitOfWork.RollBack();
                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error deleting Certificate Template: {ex.Message}");
            }
        }

        public async Task<PaginatedList<GetCertificateTemplateDTO>> GetPaginatedCertificateTemplateAsync(int pageNumber, int pageSize, Guid? idSearch, Guid? contestIdSearch, string? templateNameSearch, string? contestNameSearch)
        {
            try
            {
                // Get repository for CertificateTemplate entity
                IGenericRepository<CertificateTemplate> certificateTemplateRepo = _unitOfWork.GetRepository<CertificateTemplate>();

                // Build query with filters
                IQueryable<CertificateTemplate> query = certificateTemplateRepo.Entities.Include(t => t.Contest);

                // Apply filters if provided
                if (idSearch.HasValue)
                {
                    query = query.Where(t => t.TemplateId == idSearch.Value);
                }

                if (contestIdSearch.HasValue)
                {
                    query = query.Where(t => t.ContestId == contestIdSearch.Value);
                }

                if (!string.IsNullOrWhiteSpace(templateNameSearch))
                {
                    query = query.Where(t => t.Name.Contains(templateNameSearch));
                }

                if (!string.IsNullOrWhiteSpace(contestNameSearch))
                {
                    query = query.Where(t => t.Contest.Name.Contains(contestNameSearch));
                }

                // Get paginated data
                PaginatedList<CertificateTemplate> resultQuery = await certificateTemplateRepo.GetPagingAsync(query, pageNumber, pageSize);

                // Map to DTOs
                IReadOnlyCollection<GetCertificateTemplateDTO> result = resultQuery.Items.Select(t => _mapper.Map<GetCertificateTemplateDTO>(t)).ToList();

                // Create paginated list of DTOs
                return new PaginatedList<GetCertificateTemplateDTO>(
                    result,
                    resultQuery.TotalCount,
                    resultQuery.PageNumber,
                    resultQuery.PageSize);
            }
            catch (Exception ex)
            {
                // If something fails, roll back the transaction
                _unitOfWork.RollBack();
                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error retrieving Certificate Template: {ex.Message}");
            }
        }

        public async Task UpdateCertificateTemplateAsync(Guid id, IFormFile? file, UpdateCertificateTemplateDTO templateDTO)
        {
            try
            {
                // Begin transaction
                _unitOfWork.BeginTransaction();

                // Get repository for CertificateTemplate entity
                IGenericRepository<CertificateTemplate> certificateTemplateRepo = _unitOfWork.GetRepository<CertificateTemplate>();

                // Find the template
                CertificateTemplate? template = await certificateTemplateRepo.GetByIdAsync(id);

                //  If template not found, throw error
                if (template == null)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        $"Certificate template with ID {id} not found.");
                }

                // Upload new file if provided
                if (file != null && file.Length > 0)
                {
                    // Validate file extension
                    List<string> allowedExtensions = new List<string>{ ".pdf", ".png", ".jpg", ".jpeg", ".docx" };
                    string? fileExtension = Path.GetExtension(file.FileName).ToLower();
                    if (!allowedExtensions.Contains(fileExtension))
                    {
                        throw new ErrorException(StatusCodes.Status400BadRequest,
                            ResponseCodeConstants.BADREQUEST,
                            $"File type {fileExtension} is not supported.");
                    }

                    // Validate file size
                    if (file.Length > MAX_FILE_SIZE)
                    {
                        throw new ErrorException(StatusCodes.Status400BadRequest,
                            ResponseCodeConstants.BADREQUEST,
                            "File size exceeds the maximum allowed size.");
                    }

                    // Upload new file to Cloudinary
                    string fileUrl = await _cloudinaryService.UploadFileAsync(file, "certificates");
                    template.FileUrl = fileUrl;
                }

                // Update properties
                if (!string.IsNullOrWhiteSpace(templateDTO.Name))
                {
                    template.Name = templateDTO.Name;
                }

                // Update the template
                await certificateTemplateRepo.UpdateAsync(template);

                // Save changes
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
                    $"Error updating Certificate Template: {ex.Message}");
            }
        }
    }
}
