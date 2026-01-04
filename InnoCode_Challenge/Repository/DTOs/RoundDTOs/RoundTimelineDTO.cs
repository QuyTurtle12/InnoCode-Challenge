using System;

namespace Repository.DTOs.RoundDTOs
{
    public class RoundTimelineDTO
    {
        public Guid RoundId { get; set; }
        public DateTime Start { get; set; }
        public DateTime End { get; set; }
        public DateTime? AppealSubmitDeadline { get; set; }
        public DateTime? AppealReviewDeadline { get; set; }
        public DateTime? JudgeDeadline { get; set; }
        public DateTime? JudgeRescoreDeadline { get; set; }
    }
}
