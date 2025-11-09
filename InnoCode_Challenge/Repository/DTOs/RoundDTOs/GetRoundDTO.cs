using Repository.DTOs.McqTestDTOs;
using Repository.DTOs.ProblemDTOs;

namespace Repository.DTOs.RoundDTOs
{
    public class GetRoundDTO : BaseRoundDTO
    {
        public Guid RoundId { get; set; }

        public Guid ContestId { get; set; }

        public string RoundName { get; set; } = string.Empty;

        public string ContestName { get; set; } = string.Empty;

        public string Status { get; set; } = string.Empty;

        public string? ProblemType { get; set; }

        public GetProblemDTO? Problem { get; set; }

        public GetMcqTestDTO? McqTest { get; set; }
    }
}
