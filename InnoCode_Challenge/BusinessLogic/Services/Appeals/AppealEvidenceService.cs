using AutoMapper;
using BusinessLogic.IServices.Appeals;
using DataAccess.Entities;
using Microsoft.AspNetCore.Http;
using Repository.DTOs.AppealEvidenceDTOs;
using Repository.IRepositories;
using Utility.Constant;

namespace BusinessLogic.Services.Appeals
{
    public class AppealEvidenceService : IAppealEvidenceService
    {
        private readonly IMapper _mapper;
        private readonly IUOW _unitOfWork;

        // Constructor
        public AppealEvidenceService(IMapper mapper, IUOW unitOfWork)
        {
            _mapper = mapper;
            _unitOfWork = unitOfWork;
        }

        public async Task CreateAppealEvidenceAsync(CreateAppealEvidenceDTO appealEvidenceDTO)
        {
            try
            {
                // Start a transaction
                _unitOfWork.BeginTransaction();

                // Map DTO to Entity
                IGenericRepository<AppealEvidence> appealEvidenceRepo = _unitOfWork.GetRepository<AppealEvidence>();

                // Map the DTO to the Entity
                AppealEvidence appealEvidence = _mapper.Map<AppealEvidence>(appealEvidenceDTO);

                // Insert the new entity
                await appealEvidenceRepo.InsertAsync(appealEvidence);

                // Save changes to the database
                await _unitOfWork.SaveAsync();

                // Commit the transaction
                _unitOfWork.CommitTransaction();
            }
            catch (Exception ex)
            {
                // If something fails, roll back the transaction
                _unitOfWork.RollBack();
                throw new Utility.ExceptionCustom.ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error creating Appeal Evidence: {ex.Message}");
            }
        }

        public async Task DeleteAppealEvidenceAsync(Guid id)
        {
            try
            {
                // Start a transaction
                _unitOfWork.BeginTransaction();

                // Retrieve the existing entity
                IGenericRepository<AppealEvidence> appealEvidenceRepo = _unitOfWork.GetRepository<AppealEvidence>();
                AppealEvidence? existingAppealEvidence = await appealEvidenceRepo.GetByIdAsync(id);

                // If the entity does not exist, throw a custom exception
                if (existingAppealEvidence == null)
                {
                    throw new Utility.ExceptionCustom.ErrorException(StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        $"Appeal Evidence with ID {id} not found.");
                }

                // Delete the entity (soft delete)
                existingAppealEvidence.DeletedAt = DateTime.UtcNow;

                // Save changes to the database
                await _unitOfWork.SaveAsync();
                
                // Commit the transaction
                _unitOfWork.CommitTransaction();
            }
            catch (Exception ex)
            {
                // If something fails, roll back the transaction
                _unitOfWork.RollBack();
                throw new Utility.ExceptionCustom.ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error deleting Appeal Evidence: {ex.Message}");
            }
        }
    }
}
