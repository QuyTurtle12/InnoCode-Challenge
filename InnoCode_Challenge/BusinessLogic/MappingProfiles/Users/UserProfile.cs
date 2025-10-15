using AutoMapper;
using Repository.DTOs.UserDTOs;
using DataAccess.Entities;

namespace BusinessLogic.MappingProfiles.Users
{
    public class UserProfile : Profile
    {
        public UserProfile()
        {
            CreateMap<User, UserDTO>().ReverseMap();

            CreateMap<CreateUserDTO, User>()
                .ForMember(d => d.PasswordHash, opt => opt.Ignore())
                .ForMember(d => d.Role, opt => opt.MapFrom(s => s.Role))
                .ForMember(d => d.Fullname, opt => opt.MapFrom(s => s.Fullname))
                .ForMember(d => d.Status, opt => opt.MapFrom(s => s.Status));
            
            // UpdateUserDTO → User (only non-null props)
            var map = CreateMap<UpdateUserDTO, User>();
            map.ForAllMembers(opts => opts.Condition(
                (source, destination, sourceMember, destMember, context) => sourceMember != null
            ));

            // Ignore Password & Role
            map.ForMember(dest => dest.PasswordHash, opt => opt.Ignore());
            map.ForMember(dest => dest.Role, opt => opt.Ignore());
        }
    }
}