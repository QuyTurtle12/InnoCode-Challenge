using AutoMapper;
using DataAccess.Entities;
using Repository.DTOs.SubmissionArtifactDTOs;

namespace BusinessLogic.MappingProfiles
{
    public class SubmissionArtifactProfile : Profile
    {
        public SubmissionArtifactProfile()
        {
            CreateMap<SubmissionArtifact, GetSubmissionArtifactDTO>();
            CreateMap<CreateSubmissionArtifactDTO, SubmissionArtifact>();
            CreateMap<UpdateSubmissionArtifactDTO, SubmissionArtifact>();
        }
    }
}
