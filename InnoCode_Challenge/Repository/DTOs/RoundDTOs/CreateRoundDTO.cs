﻿using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Repository.DTOs.McqTestDTOs;
using Repository.DTOs.ProblemDTOs;
using Utility.Enums;

namespace Repository.DTOs.RoundDTOs
{
    public class CreateRoundDTO : BaseRoundDTO
    {
        public Guid ContestId { get; set; }

        [Required]
        [EnumDataType(typeof(ProblemTypeEnum))]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public ProblemTypeEnum ProblemType { get; set; }

        public CreateMcqTestDTO? McqTestConfig { get; set; }

        public CreateProblemDTO? ProblemConfig { get; set; }
    }
}
