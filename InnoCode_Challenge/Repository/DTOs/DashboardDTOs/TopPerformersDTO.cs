using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Repository.DTOs.DashboardDTOs
{
    public class TopPerformersDTO
    {
        public List<TopOrganizerDTO> TopOrganizers { get; set; } = new();
        public List<TopMentorDTO> TopMentors { get; set; } = new();
        public List<TopStudentDTO> TopStudents { get; set; } = new();
    }

    public class TopOrganizerDTO
    {
        public Guid UserId { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public int CompletedContestsCount { get; set; }
        public int TotalTeamsInContests { get; set; }
    }

    public class TopMentorDTO
    {
        public Guid MentorId { get; set; }
        public Guid UserId { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string SchoolName { get; set; } = string.Empty;
        public int TeamCertificatesCount { get; set; }
        public int TeamsManaged { get; set; }
    }

    public class TopStudentDTO
    {
        public Guid StudentId { get; set; }
        public Guid UserId { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string SchoolName { get; set; } = string.Empty;
        public int TeamCertificatesCount { get; set; }
        public int IndividualCertificatesCount { get; set; }
        public int TotalCertificates { get; set; }
    }
}
