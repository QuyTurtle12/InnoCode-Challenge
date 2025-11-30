using Microsoft.AspNetCore.Http;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Utility.Enums;

namespace Repository.DTOs.ProblemDTOs
{
    public class UpdateProblemDTO : BaseProblemDTO
    {
        [Required]
        [EnumDataType(typeof(ProblemTypeEnum))]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public ProblemTypeEnum Type { get; set; } = ProblemTypeEnum.Manual;

        public IFormFile? TemplateFile { get; set; }
    }
}
