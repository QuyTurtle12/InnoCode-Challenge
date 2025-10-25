using AutoMapper;
using DataAccess.Entities;
using Repository.DTOs.ConfigDTOs;

namespace BusinessLogic.MappingProfiles
{
    public class ConfigProfile : Profile
    {
        public ConfigProfile()
        {
            CreateMap<Config, ConfigDTO>().ReverseMap();

            CreateMap<CreateConfigDTO, Config>()
                .ForMember(d => d.UpdatedAt, o => o.MapFrom(_ => DateTime.UtcNow))
                .ForMember(d => d.DeletedAt, o => o.Ignore());

            var update = CreateMap<UpdateConfigDTO, Config>();
            update.ForAllMembers(opt =>
                opt.Condition((src, dest, srcMember, destMember, ctx) => srcMember != null));
            update.ForMember(d => d.UpdatedAt, o => o.MapFrom(_ => DateTime.UtcNow));
        }
    }
}
