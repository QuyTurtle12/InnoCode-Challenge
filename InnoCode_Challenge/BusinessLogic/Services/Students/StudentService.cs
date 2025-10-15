using AutoMapper;
using BusinessLogic.IServices.Students;
using DataAccess.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Repository.DTOs.StudentDTOs;
using Repository.IRepositories;
using Utility.Constant;
using Utility.ExceptionCustom;
using Utility.PaginatedList;

namespace BusinessLogic.Services.Students
{
    public class StudentService : IStudentService
    {
        private readonly IUOW _unitOfWork;
        private readonly IMapper _mapper;

        public StudentService(IUOW unitOfWork, IMapper mapper)
        {
            _unitOfWork = unitOfWork;
            _mapper = mapper;
        }

        public async Task<PaginatedList<StudentDTO>> GetAsync(StudentQueryParams queryParams)
        {
            var studentRepository = _unitOfWork.GetRepository<Student>();

            IQueryable<Student> studentsQuery = studentRepository.Entities
                .Where(s => s.DeletedAt == null)
                .Include(s => s.User)
                .Include(s => s.School)
                .AsNoTracking();

            if (queryParams.SchoolId.HasValue)
                studentsQuery = studentsQuery.Where(s => s.SchoolId == queryParams.SchoolId.Value);

            if (queryParams.UserId.HasValue)
                studentsQuery = studentsQuery.Where(s => s.UserId == queryParams.UserId.Value);

            if (!string.IsNullOrWhiteSpace(queryParams.Grade))
                studentsQuery = studentsQuery.Where(s => s.Grade == queryParams.Grade);

            if (!string.IsNullOrWhiteSpace(queryParams.Search))
            {
                string keyword = queryParams.Search.Trim().ToLower();
                studentsQuery = studentsQuery.Where(s =>
                    s.User.Fullname.ToLower().Contains(keyword) ||
                    s.User.Email.ToLower().Contains(keyword));
            }

            studentsQuery = (queryParams.SortBy?.ToLowerInvariant()) switch
            {
                "username" => queryParams.Desc ? studentsQuery.OrderByDescending(s => s.User.Fullname)
                                                 : studentsQuery.OrderBy(s => s.User.Fullname),
                "schoolname" => queryParams.Desc ? studentsQuery.OrderByDescending(s => s.School.Name)
                                                 : studentsQuery.OrderBy(s => s.School.Name),
                "grade" => queryParams.Desc ? studentsQuery.OrderByDescending(s => s.Grade)
                                                 : studentsQuery.OrderBy(s => s.Grade),
                _ => queryParams.Desc ? studentsQuery.OrderByDescending(s => s.CreatedAt)
                                                 : studentsQuery.OrderBy(s => s.CreatedAt),
            };

            var paged = await studentRepository.GetPagingAsync(studentsQuery, queryParams.Page, queryParams.PageSize);
            var items = paged.Items.Select(_mapper.Map<StudentDTO>).ToList();

            return new PaginatedList<StudentDTO>(items, paged.TotalCount, paged.PageNumber, paged.PageSize);
        }

        public async Task<StudentDTO> GetByIdAsync(Guid id)
        {
            var studentRepository = _unitOfWork.GetRepository<Student>();
            var student = await studentRepository.Entities
                .Include(s => s.User)
                .Include(s => s.School)
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.StudentId == id && s.DeletedAt == null);

            if (student == null)
                throw new ErrorException(StatusCodes.Status404NotFound, "STUDENT_NOT_FOUND", $"No student with ID={id}");

            return _mapper.Map<StudentDTO>(student);
        }

        public async Task<StudentDTO> CreateAsync(CreateStudentDTO dto)
        {
            var studentRepository = _unitOfWork.GetRepository<Student>();
            var userRepository = _unitOfWork.GetRepository<User>();
            var schoolRepository = _unitOfWork.GetRepository<School>();

            var user = await userRepository.Entities
                .FirstOrDefaultAsync(u => u.UserId == dto.UserId && u.DeletedAt == null);

            if (user == null)
                throw new ErrorException(StatusCodes.Status404NotFound, "USER_NOT_FOUND", $"No user with ID={dto.UserId}");

            if (!string.Equals(user.Role, RoleConstants.Student, StringComparison.Ordinal))
                throw new ErrorException(StatusCodes.Status400BadRequest, "USER_NOT_STUDENT", "User role must be Student.");

            if (!string.Equals(user.Status, "Active", StringComparison.Ordinal))
                throw new ErrorException(StatusCodes.Status400BadRequest, "USER_INACTIVE", "User must be Active.");

            var schoolExists = await schoolRepository.Entities
                .AnyAsync(s => s.SchoolId == dto.SchoolId && s.DeletedAt == null);
            if (!schoolExists)
                throw new ErrorException(StatusCodes.Status404NotFound, "SCHOOL_NOT_FOUND", $"No school with ID={dto.SchoolId}");

            bool userAlreadyLinked = await studentRepository.Entities
                .AnyAsync(s => s.UserId == dto.UserId && s.DeletedAt == null);
            if (userAlreadyLinked)
                throw new ErrorException(StatusCodes.Status400BadRequest, "USER_ALREADY_STUDENT", "This user is already a student.");

            var now = DateTime.UtcNow;

            var student = new Student
            {
                StudentId = Guid.NewGuid(),
                UserId = dto.UserId,
                SchoolId = dto.SchoolId,
                Grade = string.IsNullOrWhiteSpace(dto.Grade) ? null : dto.Grade.Trim(),
                CreatedAt = now,
                DeletedAt = null
            };

            await studentRepository.InsertAsync(student);
            await _unitOfWork.SaveAsync();

            var created = await studentRepository.Entities
                .Include(s => s.User)
                .Include(s => s.School)
                .AsNoTracking()
                .FirstAsync(s => s.StudentId == student.StudentId);

            return _mapper.Map<StudentDTO>(created);
        }

        public async Task<StudentDTO> UpdateAsync(Guid id, UpdateStudentDTO dto)
        {
            var studentRepository = _unitOfWork.GetRepository<Student>();
            var schoolRepository = _unitOfWork.GetRepository<School>();

            var student = await studentRepository.Entities
                .FirstOrDefaultAsync(s => s.StudentId == id && s.DeletedAt == null);

            if (student == null)
                throw new ErrorException(StatusCodes.Status404NotFound, "STUDENT_NOT_FOUND", $"No student with ID={id}");

            if (dto.SchoolId.HasValue && dto.SchoolId.Value != student.SchoolId)
            {
                bool schoolExists = await schoolRepository.Entities
                    .AnyAsync(s => s.SchoolId == dto.SchoolId.Value && s.DeletedAt == null);
                if (!schoolExists)
                    throw new ErrorException(StatusCodes.Status404NotFound, "SCHOOL_NOT_FOUND",
                        $"No school with ID={dto.SchoolId.Value}");
                student.SchoolId = dto.SchoolId.Value;
            }

            if (dto.Grade != null)
                student.Grade = string.IsNullOrWhiteSpace(dto.Grade) ? null : dto.Grade.Trim();

            studentRepository.Update(student);
            await _unitOfWork.SaveAsync();

            var updated = await studentRepository.Entities
                .Include(s => s.User)
                .Include(s => s.School)
                .AsNoTracking()
                .FirstAsync(s => s.StudentId == id);

            return _mapper.Map<StudentDTO>(updated);
        }

        public async Task DeleteAsync(Guid id)
        {
            var studentRepository = _unitOfWork.GetRepository<Student>();

            var student = await studentRepository.Entities
                .Include(s => s.TeamMembers)
                .Include(s => s.Submissions)
                .Include(s => s.Certificates)
                .Include(s => s.McqAttempts)
                .FirstOrDefaultAsync(s => s.StudentId == id && s.DeletedAt == null);

            if (student == null)
                throw new ErrorException(StatusCodes.Status404NotFound, "STUDENT_NOT_FOUND", $"No student with ID={id}");

            // Guard deletes if there are dependent records
            bool hasRelations = student.TeamMembers.Any() ||
                                student.Submissions.Any() ||
                                student.Certificates.Any() ||
                                student.McqAttempts.Any();

            if (hasRelations)
                throw new ErrorException(StatusCodes.Status409Conflict, "STUDENT_IN_USE",
                    "Cannot delete a student with related records (teams, submissions, certificates, or attempts).");

            student.DeletedAt = DateTime.UtcNow;
            studentRepository.Update(student);
            await _unitOfWork.SaveAsync();
        }
    }
}
