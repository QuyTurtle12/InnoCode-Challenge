using AutoMapper;
using DataAccess.Entities;
using Repository.DTOs.SchoolDTOs;

namespace BusinessLogic.MappingProfiles.Schools
{
    public class SchoolProfile : Profile
    {
        public SchoolProfile()
        {
            CreateMap<School, SchoolDTO>()
                .ForMember(dest => dest.ProvinceName, opt => opt.MapFrom(src => src.Province.Name));

            CreateMap<CreateSchoolDTO, School>()
                .ForMember(dest => dest.Name, opt => opt.MapFrom(src => src.Name.Trim()))
                .ForMember(dest => dest.Contact, opt => opt.MapFrom(src =>
                    string.IsNullOrWhiteSpace(src.Contact) ? null : src.Contact!.Trim()));

            var updateMap = CreateMap<UpdateSchoolDTO, School>();
            updateMap.ForAllMembers(opt => opt.Condition((src, dest, srcMember) => srcMember != null));
            updateMap.ForMember(dest => dest.Name, opt => opt.Ignore()); 
            updateMap.ForMember(dest => dest.Contact, opt => opt.Ignore()); 
            updateMap.ForMember(dest => dest.ProvinceId, opt => opt.Ignore()); 
        }
    }

}
