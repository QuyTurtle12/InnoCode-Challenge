using AutoMapper;
using Utility.Helpers;
using Utility.Constant;
using Repository.DTOs.UserDTOs;
using DataAccess.Entities;

namespace BusinessLogic.MappingProfiles
{
    public class UserProfile : Profile
    {
        public UserProfile()
        {
            CreateMap<User, UserDTO>().ReverseMap();

            // CreateUserDTO → User
            CreateMap<CreateUserDTO, User>()
              .ForMember(dest => dest.PasswordHash,
                         opt => opt.MapFrom(src => PasswordHasher.Hash(src.Password)))
              .ForMember(dest => dest.Role,
                         opt => opt.MapFrom(src => RoleConstants.ToRoleValue(src.Role)))
              .ForMember(dest => dest.Status,
                         opt => opt.MapFrom(src => src.Status));

            // UpdateUserDTO → User (only non-null props)
            var map = CreateMap<UpdateUserDTO, User>();

            // Configure ForAllMembers(...) to copy only non-null properties:
            map.ForAllMembers(opts => opts.Condition(
                (source, destination, sourceMember, destMember, context) => sourceMember != null
            ));

            // Now configure ForMember(...) to ignore PasswordHash:
            map.ForMember(dest => dest.PasswordHash, opt => opt.Ignore());
            map.ForMember(dest => dest.Role, opt => opt.Ignore());
        }
    }
}