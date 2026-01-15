using AutoMapper;
using DataAccess.Entities;
using Repository.DTOs.SubmissionDetailDTOs;

namespace BusinessLogic.MappingProfiles.Submissions
{
    public class SubmissionDetailProfile : Profile
    {
        public SubmissionDetailProfile()
        {
            CreateMap<CreateSubmissionDetailDTO, SubmissionDetail>();
            CreateMap<UpdateSubmissionDetailDTO, SubmissionDetail>();
            CreateMap<SubmissionDetail, GetSubmissionDetailDTO>();
        }
    }
}
