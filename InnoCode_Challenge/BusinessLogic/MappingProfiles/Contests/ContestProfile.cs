using AutoMapper;
using DataAccess.Entities;
using Repository.DTOs.ContestDTOs;

namespace BusinessLogic.MappingProfiles.Contests
{
    public class ContestProfile : Profile
    {
        public ContestProfile()
        {
            CreateMap<CreateContestDTO, Contest>().ReverseMap();
            CreateMap<UpdateContestDTO, Contest>().ReverseMap();
            CreateMap<Contest, GetContestDTO>().ReverseMap();

            CreateMap<CreateContestAdvancedDTO, Contest>()
                .ForMember(d => d.Status, opt => opt.Ignore())
                .ForMember(d => d.CreatedAt, opt => opt.Ignore())
                .ForMember(d => d.DeletedAt, opt => opt.Ignore());

            CreateMap<Contest, ContestCreatedDTO>()
                .ForMember(d => d.Status, opt => opt.MapFrom(s => s.Status));

        }
    }
}
