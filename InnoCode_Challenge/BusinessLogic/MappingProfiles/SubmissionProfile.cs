using AutoMapper;
using DataAccess.Entities;
using Repository.DTOs.SubmissionDTOs;

namespace BusinessLogic.MappingProfiles
{
    public class SubmissionProfile : Profile
    {
        public SubmissionProfile() 
        {
            CreateMap<Submission, GetSubmissionDTO>()
                .ForMember(dest => dest.TeamName, opt => opt.MapFrom(src => src.Team.Name));

            CreateMap<CreateSubmissionDTO, Submission>().ReverseMap();
            CreateMap<UpdateSubmissionDTO, Submission>().ReverseMap();
        }
    }
}
