using System.ComponentModel.DataAnnotations;

namespace Repository.DTOs.JudgeInviteDTOs
{
    public class CreateJudgeInviteDTO
    {
        [Required]
        public Guid JudgeUserId { get; set; }

        [Range(1, 60)]
        public int? TtlDays { get; set; }
    }
}
