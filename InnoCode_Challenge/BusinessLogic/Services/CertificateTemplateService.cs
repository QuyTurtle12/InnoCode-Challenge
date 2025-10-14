using AutoMapper;
using BusinessLogic.IServices;
using DataAccess.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Repository.DTOs.CertificateTemplateDTOs;
using Repository.IRepositories;
using Utility.Constant;
using Utility.ExceptionCustom;
using Utility.PaginatedList;

namespace BusinessLogic.Services
{
    public class CertificateTemplateService : ICertificateTemplateService
    {
        private readonly IMapper _mapper;
        private readonly IUOW _unitOfWork;

        // Constructor
        public CertificateTemplateService(IMapper mapper, IUOW unitOfWork)
        {
            _mapper = mapper;
            _unitOfWork = unitOfWork;
        }

        public async Task CreateCertificateTemplateAsync(CreateCertificateTemplateDTO templateDTO)
        {
            try
            {
                // Begin transaction
                _unitOfWork.BeginTransaction();

                // Get repository for CertificateTemplate entity
                IGenericRepository<CertificateTemplate> certificateTemplateRepo = _unitOfWork.GetRepository<CertificateTemplate>();

                // Get repository for Contest entity to check if contest exists
                IGenericRepository<Contest> contestRepo = _unitOfWork.GetRepository<Contest>();

                // Check if contest exists
                var contestExists = await contestRepo.GetByIdAsync(templateDTO.ContestId);
                if (contestExists == null)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound, ResponseCodeConstants.NOT_FOUND, $"Contest with ID {templateDTO.ContestId} not found.");
                }

                // Create new template entity and map from DTO
                CertificateTemplate template = _mapper.Map<CertificateTemplate>(templateDTO);

                // Generate new ID for the template
                template.TemplateId = Guid.NewGuid();

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

        public async Task UpdateCertificateTemplateAsync(Guid id, UpdateCertificateTemplateDTO templateDTO)
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

                // Check if the contest ID has changed
                if (template.ContestId != templateDTO.ContestId)
                {
                    // Get repository for Contest entity to check if the new contest exists
                    IGenericRepository<Contest> contestRepo = _unitOfWork.GetRepository<Contest>();

                    // Check if the new contest exists
                    var contestExists = await contestRepo.GetByIdAsync(templateDTO.ContestId);
                    if (contestExists == null)
                    {
                        throw new ErrorException(StatusCodes.Status404NotFound, ResponseCodeConstants.NOT_FOUND, $"Contest with ID {templateDTO.ContestId} not found.");
                    }
                }

                // Update properties
                template.ContestId = templateDTO.ContestId;
                template.Name = templateDTO.Name;
                template.FileUrl = templateDTO.FileUrl;

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
                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error updating Certificate Template: {ex.Message}");
            }
        }
    }
}
