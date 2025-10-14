using AutoMapper;
using DataAccess.Entities;
using Repository.DTOs.AppealEvidenceDTOs;

namespace BusinessLogic.MappingProfiles
{
    public class AppealEvidenceProfile : Profile
    {
        public AppealEvidenceProfile()
        {
            CreateMap<CreateAppealEvidenceDTO, AppealEvidence>().ReverseMap();
            CreateMap<AppealEvidence, GetAppealEvidenceDTO>().ReverseMap();
        }
    }
}
