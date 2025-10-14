using System;
using System.ComponentModel.DataAnnotations;

namespace Repository.DTOs.MentorRegistrationDTOs
{
    public class ApproveMentorRegistrationDTO
    {
        public Guid? SchoolId { get; set; }

        public bool UseProposedSchool { get; set; } = true;
    }
}
