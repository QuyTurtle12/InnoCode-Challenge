using AutoMapper;
using DataAccess.Entities;
using Repository.DTOs.ProvinceDTOs;

namespace BusinessLogic.MappingProfiles.Schools
{
    public class ProvinceProfile : Profile
    {
        public ProvinceProfile()
        {
            CreateMap<Province, ProvinceDTO>().ReverseMap();

            CreateMap<CreateProvinceDTO, Province>()
                .ForMember(dest => dest.Name,
                    opt => opt.MapFrom(src => src.Name.Trim()))
                .ForMember(dest => dest.Address,
                    opt => opt.MapFrom(src =>
                        string.IsNullOrWhiteSpace(src.Address) ? null : src.Address!.Trim()));

            var updateMap = CreateMap<UpdateProvinceDTO, Province>();
            updateMap.ForAllMembers(opt =>
                opt.Condition((src, dest, srcMember) => srcMember != null));
            updateMap.ForMember(dest => dest.Name, opt => opt.Ignore()); 
            updateMap.ForMember(dest => dest.Address, opt => opt.Ignore());  
        }
    }

}
