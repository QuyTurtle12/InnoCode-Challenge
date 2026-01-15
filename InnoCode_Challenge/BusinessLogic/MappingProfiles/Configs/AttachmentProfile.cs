using AutoMapper;
using DataAccess.Entities;
using Repository.DTOs.AttachmentDTOs;

namespace BusinessLogic.MappingProfiles
{
    public class AttachmentProfile : Profile
    {
        public AttachmentProfile()
        {
            CreateMap<Attachment, AttachmentDTO>().ReverseMap();

            CreateMap<CreateAttachmentDTO, Attachment>()
                .ForMember(d => d.CreatedAt, o => o.MapFrom(_ => DateTime.UtcNow))
                .ForMember(d => d.DeletedAt, o => o.Ignore());

            var update = CreateMap<UpdateAttachmentDTO, Attachment>();
            update.ForAllMembers(opt =>
                opt.Condition((src, dest, srcMember, destMember, ctx) => srcMember != null));
        }
    }
}
