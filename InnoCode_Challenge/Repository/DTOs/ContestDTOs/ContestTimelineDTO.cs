using System;
using System.Collections.Generic;
using Repository.DTOs.RoundDTOs;

namespace Repository.DTOs.ContestDTOs
{
    public class ContestTimelineDTO
    {
        public Guid ContestId { get; set; }
        public DateTime? RegistrationStart { get; set; }
        public DateTime? RegistrationEnd { get; set; }
        public DateTime? ContestStart { get; set; }
        public DateTime? ContestEnd { get; set; }
        public IReadOnlyList<RoundTimelineDTO> Rounds { get; set; } = new List<RoundTimelineDTO>();
    }
}
