using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

namespace DataAccess.Entities;

public partial class ContestDbContext : DbContext
{
    public ContestDbContext()
    {
    }

    public ContestDbContext(DbContextOptions<ContestDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<ActivityLog> ActivityLogs { get; set; }

    public virtual DbSet<Appeal> Appeals { get; set; }

    public virtual DbSet<AppealEvidence> AppealEvidences { get; set; }

    public virtual DbSet<Attachment> Attachments { get; set; }

    public virtual DbSet<Bank> Banks { get; set; }

    public virtual DbSet<Certificate> Certificates { get; set; }

    public virtual DbSet<CertificateTemplate> CertificateTemplates { get; set; }

    public virtual DbSet<Config> Configs { get; set; }

    public virtual DbSet<Contest> Contests { get; set; }

    public virtual DbSet<LeaderboardEntry> LeaderboardEntries { get; set; }

    public virtual DbSet<McqAttempt> McqAttempts { get; set; }

    public virtual DbSet<McqAttemptItem> McqAttemptItems { get; set; }

    public virtual DbSet<McqOption> McqOptions { get; set; }

    public virtual DbSet<McqQuestion> McqQuestions { get; set; }

    public virtual DbSet<McqTest> McqTests { get; set; }

    public virtual DbSet<McqTestQuestion> McqTestQuestions { get; set; }

    public virtual DbSet<Mentor> Mentors { get; set; }

    public virtual DbSet<MentorRegistration> MentorRegistrations { get; set; }

    public virtual DbSet<Notification> Notifications { get; set; }

    public virtual DbSet<Problem> Problems { get; set; }

    public virtual DbSet<Province> Provinces { get; set; }

    public virtual DbSet<Round> Rounds { get; set; }

    public virtual DbSet<School> Schools { get; set; }

    public virtual DbSet<Student> Students { get; set; }

    public virtual DbSet<Submission> Submissions { get; set; }

    public virtual DbSet<SubmissionArtifact> SubmissionArtifacts { get; set; }

    public virtual DbSet<SubmissionDetail> SubmissionDetails { get; set; }

    public virtual DbSet<Team> Teams { get; set; }

    public virtual DbSet<TeamInvite> TeamInvites { get; set; }

    public virtual DbSet<TeamMember> TeamMembers { get; set; }

    public virtual DbSet<TestCase> TestCases { get; set; }

    public virtual DbSet<User> Users { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ActivityLog>(entity =>
        {
            entity.HasKey(e => e.LogId).HasName("PK__activity__9E2397E06243B345");

            entity.ToTable("activity_logs");

            entity.HasIndex(e => new { e.UserId, e.At }, "IX_activity_user_at");

            entity.Property(e => e.LogId)
                .HasDefaultValueSql("(newid())")
                .HasColumnName("log_id");
            entity.Property(e => e.Action)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("action");
            entity.Property(e => e.At)
                .HasPrecision(0)
                .HasColumnName("at");
            entity.Property(e => e.DeletedAt).HasPrecision(0);
            entity.Property(e => e.TargetId)
                .HasMaxLength(36)
                .IsUnicode(false)
                .HasColumnName("target_id");
            entity.Property(e => e.TargetType)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasColumnName("target_type");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.User).WithMany(p => p.ActivityLogs)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_activity_logs_user");
        });

        modelBuilder.Entity<Appeal>(entity =>
        {
            entity.HasKey(e => e.AppealId).HasName("PK__appeals__DFAC766D35CFB80C");

            entity.ToTable("appeals");

            entity.HasIndex(e => new { e.TargetType, e.TargetId }, "IX_appeals_target").IsUnique();

            entity.Property(e => e.AppealId)
                .HasDefaultValueSql("(newid())")
                .HasColumnName("appeal_id");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasColumnName("created_at");
            entity.Property(e => e.Decision)
                .IsUnicode(false)
                .HasColumnName("decision");
            entity.Property(e => e.DeletedAt).HasPrecision(0);
            entity.Property(e => e.OwnerId).HasColumnName("owner_id");
            entity.Property(e => e.Reason)
                .IsUnicode(false)
                .HasColumnName("reason");
            entity.Property(e => e.State)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasDefaultValue("open")
                .HasColumnName("state");
            entity.Property(e => e.TargetId)
                .HasMaxLength(36)
                .IsUnicode(false)
                .HasColumnName("target_id");
            entity.Property(e => e.TargetType)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasColumnName("target_type");
            entity.Property(e => e.TeamId).HasColumnName("team_id");

            entity.HasOne(d => d.Owner).WithMany(p => p.Appeals)
                .HasForeignKey(d => d.OwnerId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_appeals_owner");

            entity.HasOne(d => d.Team).WithMany(p => p.Appeals)
                .HasForeignKey(d => d.TeamId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_appeals_team");
        });

        modelBuilder.Entity<AppealEvidence>(entity =>
        {
            entity.HasKey(e => e.EvidenceId).HasName("PK__appeal_e__C59A788E13033D22");

            entity.ToTable("appeal_evidence");

            entity.Property(e => e.EvidenceId)
                .HasDefaultValueSql("(newid())")
                .HasColumnName("evidence_id");
            entity.Property(e => e.AppealId).HasColumnName("appeal_id");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasColumnName("created_at");
            entity.Property(e => e.DeletedAt).HasPrecision(0);
            entity.Property(e => e.Note)
                .IsUnicode(false)
                .HasColumnName("note");
            entity.Property(e => e.Url)
                .IsUnicode(false)
                .HasColumnName("url");

            entity.HasOne(d => d.Appeal).WithMany(p => p.AppealEvidences)
                .HasForeignKey(d => d.AppealId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_appeal_evidence_appeal");
        });

        modelBuilder.Entity<Attachment>(entity =>
        {
            entity.HasKey(e => e.AttachmentId).HasName("PK__attachme__B74DF4E2F2C80D62");

            entity.ToTable("attachments");

            entity.Property(e => e.AttachmentId)
                .HasDefaultValueSql("(newid())")
                .HasColumnName("attachment_id");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasColumnName("created_at");
            entity.Property(e => e.DeletedAt).HasPrecision(0);
            entity.Property(e => e.Type)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("type");
            entity.Property(e => e.Url)
                .IsUnicode(false)
                .HasColumnName("url");
        });

        modelBuilder.Entity<Bank>(entity =>
        {
            entity.HasKey(e => e.BankId).HasName("PK__bank__4076F7039B27BF65");

            entity.ToTable("bank");

            entity.Property(e => e.BankId)
                .HasDefaultValueSql("(newid())")
                .HasColumnName("bank_id");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasColumnName("created_at");
            entity.Property(e => e.DeletedAt).HasPrecision(0);
            entity.Property(e => e.Name)
                .IsUnicode(false)
                .HasColumnName("name");
        });

        modelBuilder.Entity<Certificate>(entity =>
        {
            entity.HasKey(e => e.CertificateId).HasName("PK__certific__E2256D31FAD42339");

            entity.ToTable("certificates");

            entity.Property(e => e.CertificateId)
                .HasDefaultValueSql("(newid())")
                .HasColumnName("certificate_id");
            entity.Property(e => e.DeletedAt).HasPrecision(0);
            entity.Property(e => e.FileUrl)
                .IsUnicode(false)
                .HasColumnName("file_url");
            entity.Property(e => e.IssuedAt)
                .HasPrecision(0)
                .HasColumnName("issued_at");
            entity.Property(e => e.StudentId).HasColumnName("student_id");
            entity.Property(e => e.TeamId).HasColumnName("team_id");
            entity.Property(e => e.TemplateId).HasColumnName("template_id");

            entity.HasOne(d => d.Student).WithMany(p => p.Certificates)
                .HasForeignKey(d => d.StudentId)
                .HasConstraintName("FK_certificates_student");

            entity.HasOne(d => d.Team).WithMany(p => p.Certificates)
                .HasForeignKey(d => d.TeamId)
                .HasConstraintName("FK_certificates_team");

            entity.HasOne(d => d.Template).WithMany(p => p.Certificates)
                .HasForeignKey(d => d.TemplateId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_certificates_template");
        });

        modelBuilder.Entity<CertificateTemplate>(entity =>
        {
            entity.HasKey(e => e.TemplateId).HasName("PK__certific__BE44E0793543088C");

            entity.ToTable("certificate_templates");

            entity.Property(e => e.TemplateId)
                .HasDefaultValueSql("(newid())")
                .HasColumnName("template_id");
            entity.Property(e => e.ContestId).HasColumnName("contest_id");
            entity.Property(e => e.FileUrl)
                .IsUnicode(false)
                .HasColumnName("file_url");
            entity.Property(e => e.Name)
                .HasMaxLength(120)
                .IsUnicode(false)
                .HasColumnName("name");

            entity.HasOne(d => d.Contest).WithMany(p => p.CertificateTemplates)
                .HasForeignKey(d => d.ContestId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_cert_templates_contest");
        });

        modelBuilder.Entity<Config>(entity =>
        {
            entity.HasKey(e => e.Key).HasName("PK__config__DFD83CAE08B26399");

            entity.ToTable("config");

            entity.Property(e => e.Key)
                .HasMaxLength(120)
                .IsUnicode(false)
                .HasColumnName("key");
            entity.Property(e => e.DeletedAt).HasPrecision(0);
            entity.Property(e => e.Scope)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasColumnName("scope");
            entity.Property(e => e.UpdatedAt)
                .HasPrecision(0)
                .HasColumnName("updated_at");
            entity.Property(e => e.Value)
                .IsUnicode(false)
                .HasColumnName("value");
        });

        modelBuilder.Entity<Contest>(entity =>
        {
            entity.HasKey(e => e.ContestId).HasName("PK__contests__3148827E0E6AEC6A");

            entity.ToTable("contests");

            entity.HasIndex(e => new { e.Year, e.Name }, "UQ_contests_year_name").IsUnique();

            entity.Property(e => e.ContestId)
                .HasDefaultValueSql("(newid())")
                .HasColumnName("contest_id");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasColumnName("created_at");
            entity.Property(e => e.DeletedAt).HasPrecision(0);
            entity.Property(e => e.Description)
                .HasMaxLength(200)
                .IsUnicode(false)
                .HasColumnName("description");
            entity.Property(e => e.End)
                .HasPrecision(0)
                .HasColumnName("end");
            entity.Property(e => e.ImgUrl)
                .HasMaxLength(200)
                .IsUnicode(false)
                .HasColumnName("img_url");
            entity.Property(e => e.Name)
                .HasMaxLength(200)
                .IsUnicode(false)
                .HasColumnName("name");
            entity.Property(e => e.Start)
                .HasPrecision(0)
                .HasColumnName("start");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasDefaultValue("draft")
                .HasColumnName("status");
            entity.Property(e => e.Year).HasColumnName("year");
        });

        modelBuilder.Entity<LeaderboardEntry>(entity =>
        {
            entity.HasKey(e => new { e.EntryId, e.ContestId, e.TeamId }).HasName("PK__leaderbo__810FDCE1196EA113");

            entity.ToTable("leaderboard_entries");

            entity.HasIndex(e => new { e.ContestId, e.TeamId }, "UQ_leaderboard_contest_team").IsUnique();

            entity.Property(e => e.EntryId)
                .HasDefaultValueSql("(newid())")
                .HasColumnName("entry_id");
            entity.Property(e => e.ContestId).HasColumnName("contest_id");
            entity.Property(e => e.TeamId).HasColumnName("team_id");
            entity.Property(e => e.Rank).HasColumnName("rank");
            entity.Property(e => e.Score).HasColumnName("score");
            entity.Property(e => e.SnapshotAt)
                .HasPrecision(0)
                .HasColumnName("snapshot_at");

            entity.HasOne(d => d.Contest).WithMany(p => p.LeaderboardEntries)
                .HasForeignKey(d => d.ContestId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_leaderboard_contest");

            entity.HasOne(d => d.Team).WithMany(p => p.LeaderboardEntries)
                .HasForeignKey(d => d.TeamId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_leaderboard_team");
        });

        modelBuilder.Entity<McqAttempt>(entity =>
        {
            entity.HasKey(e => e.AttemptId).HasName("PK__mcq_atte__5621F94984B1617B");

            entity.ToTable("mcq_attempts");

            entity.Property(e => e.AttemptId)
                .HasDefaultValueSql("(newid())")
                .HasColumnName("attempt_id");
            entity.Property(e => e.End)
                .HasPrecision(0)
                .HasColumnName("end");
            entity.Property(e => e.RoundId).HasColumnName("round_id");
            entity.Property(e => e.Score).HasColumnName("score");
            entity.Property(e => e.Start)
                .HasPrecision(0)
                .HasColumnName("start");
            entity.Property(e => e.StudentId).HasColumnName("student_id");
            entity.Property(e => e.TestId).HasColumnName("test_id");

            entity.HasOne(d => d.Round).WithMany(p => p.McqAttempts)
                .HasForeignKey(d => d.RoundId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_mcq_attempts_round");

            entity.HasOne(d => d.Student).WithMany(p => p.McqAttempts)
                .HasForeignKey(d => d.StudentId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_mcq_attempts_student");

            entity.HasOne(d => d.Test).WithMany(p => p.McqAttempts)
                .HasForeignKey(d => d.TestId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_mcq_attempts_test");
        });

        modelBuilder.Entity<McqAttemptItem>(entity =>
        {
            entity.HasKey(e => e.ItemId).HasName("PK__mcq_atte__52020FDD02240646");

            entity.ToTable("mcq_attempt_items");

            entity.HasIndex(e => new { e.AttemptId, e.QuestionId }, "UQ_mcq_attempt_items").IsUnique();

            entity.Property(e => e.ItemId)
                .HasDefaultValueSql("(newid())")
                .HasColumnName("item_id");
            entity.Property(e => e.AttemptId).HasColumnName("attempt_id");
            entity.Property(e => e.Correct).HasColumnName("correct");
            entity.Property(e => e.QuestionId).HasColumnName("question_id");
            entity.Property(e => e.SelectedOptionId).HasColumnName("selected_option_id");
            entity.Property(e => e.TestId).HasColumnName("test_id");

            entity.HasOne(d => d.Attempt).WithMany(p => p.McqAttemptItems)
                .HasForeignKey(d => d.AttemptId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_mcq_ai_attempt");

            entity.HasOne(d => d.Question).WithMany(p => p.McqAttemptItems)
                .HasForeignKey(d => d.QuestionId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_mcq_ai_question");

            entity.HasOne(d => d.SelectedOption).WithMany(p => p.McqAttemptItems)
                .HasForeignKey(d => d.SelectedOptionId)
                .HasConstraintName("FK_mcq_ai_option");

            entity.HasOne(d => d.Test).WithMany(p => p.McqAttemptItems)
                .HasForeignKey(d => d.TestId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_mcq_ai_test");
        });

        modelBuilder.Entity<McqOption>(entity =>
        {
            entity.HasKey(e => e.OptionId).HasName("PK__mcq_opti__F4EACE1B12C06C83");

            entity.ToTable("mcq_options");

            entity.Property(e => e.OptionId)
                .HasDefaultValueSql("(newid())")
                .HasColumnName("option_id");
            entity.Property(e => e.IsCorrect).HasColumnName("is_correct");
            entity.Property(e => e.QuestionId).HasColumnName("question_id");
            entity.Property(e => e.Text)
                .IsUnicode(false)
                .HasColumnName("text");

            entity.HasOne(d => d.Question).WithMany(p => p.McqOptions)
                .HasForeignKey(d => d.QuestionId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_mcq_options_question");
        });

        modelBuilder.Entity<McqQuestion>(entity =>
        {
            entity.HasKey(e => e.QuestionId).HasName("PK__mcq_ques__2EC21549DFFA1BCF");

            entity.ToTable("mcq_questions");

            entity.Property(e => e.QuestionId)
                .HasDefaultValueSql("(newid())")
                .HasColumnName("question_id");
            entity.Property(e => e.BankId).HasColumnName("bank_id");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasColumnName("created_at");
            entity.Property(e => e.DeletedAt).HasPrecision(0);
            entity.Property(e => e.Text)
                .IsUnicode(false)
                .HasColumnName("text");

            entity.HasOne(d => d.Bank).WithMany(p => p.McqQuestions)
                .HasForeignKey(d => d.BankId)
                .HasConstraintName("FK_mcq_questions_bank");
        });

        modelBuilder.Entity<McqTest>(entity =>
        {
            entity.HasKey(e => e.TestId).HasName("PK__mcq_test__F3FF1C02DEC3D48D");

            entity.ToTable("mcq_tests");

            entity.Property(e => e.TestId)
                .HasDefaultValueSql("(newid())")
                .HasColumnName("test_id");
            entity.Property(e => e.Config)
                .IsUnicode(false)
                .HasColumnName("config");
            entity.Property(e => e.Name)
                .HasMaxLength(120)
                .IsUnicode(false)
                .HasColumnName("name");
            entity.Property(e => e.RoundId).HasColumnName("round_id");

            entity.HasOne(d => d.Round).WithMany(p => p.McqTests)
                .HasForeignKey(d => d.RoundId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_mcq_tests_round");
        });

        modelBuilder.Entity<McqTestQuestion>(entity =>
        {
            entity.HasKey(e => new { e.TestId, e.QuestionId });

            entity.ToTable("mcq_test_questions");

            entity.Property(e => e.TestId).HasColumnName("test_id");
            entity.Property(e => e.QuestionId).HasColumnName("question_id");
            entity.Property(e => e.OrderIndex).HasColumnName("order_index");
            entity.Property(e => e.Weight)
                .HasDefaultValue(1.0)
                .HasColumnName("weight");

            entity.HasOne(d => d.Question).WithMany(p => p.McqTestQuestions)
                .HasForeignKey(d => d.QuestionId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_mcq_tq_question");

            entity.HasOne(d => d.Test).WithMany(p => p.McqTestQuestions)
                .HasForeignKey(d => d.TestId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_mcq_tq_test");
        });

        modelBuilder.Entity<Mentor>(entity =>
        {
            entity.HasKey(e => e.MentorId).HasName("PK__mentors__E5D27EF382CA9BC1");

            entity.ToTable("mentors");

            entity.HasIndex(e => new { e.UserId, e.SchoolId }, "UQ_mentors_user_school").IsUnique();

            entity.Property(e => e.MentorId)
                .HasDefaultValueSql("(newid())")
                .HasColumnName("mentor_id");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasColumnName("created_at");
            entity.Property(e => e.DeletedAt).HasPrecision(0);
            entity.Property(e => e.Phone)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasColumnName("phone");
            entity.Property(e => e.SchoolId).HasColumnName("school_id");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.School).WithMany(p => p.Mentors)
                .HasForeignKey(d => d.SchoolId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_mentors_school");

            entity.HasOne(d => d.User).WithMany(p => p.Mentors)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_mentors_user");
        });

        modelBuilder.Entity<MentorRegistration>(entity =>
        {
            entity.HasKey(e => e.RegistrationId);

            entity.ToTable("mentor_registrations");

            entity.HasIndex(e => e.Email, "IX_mr_email");

            entity.HasIndex(e => e.SchoolId, "IX_mr_school");

            entity.HasIndex(e => new { e.Status, e.CreatedAt }, "IX_mr_status_created");

            entity.Property(e => e.RegistrationId)
                .HasDefaultValueSql("(newid())")
                .HasColumnName("registration_id");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasColumnName("created_at");
            entity.Property(e => e.DeletedAt).HasPrecision(0);
            entity.Property(e => e.DenyReason)
                .HasMaxLength(300)
                .IsUnicode(false)
                .HasColumnName("deny_reason");
            entity.Property(e => e.Email)
                .HasMaxLength(255)
                .IsUnicode(false)
                .HasColumnName("email");
            entity.Property(e => e.Fullname)
                .HasMaxLength(255)
                .IsUnicode(false)
                .HasColumnName("fullname");
            entity.Property(e => e.PasswordHash)
                .HasMaxLength(255)
                .IsUnicode(false)
                .HasColumnName("password_hash");
            entity.Property(e => e.Phone)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasColumnName("phone");
            entity.Property(e => e.ProposedSchoolAddress)
                .HasMaxLength(255)
                .IsUnicode(false)
                .HasColumnName("proposed_school_address");
            entity.Property(e => e.ProposedSchoolName)
                .HasMaxLength(200)
                .IsUnicode(false)
                .HasColumnName("proposed_school_name");
            entity.Property(e => e.ProvinceId).HasColumnName("province_id");
            entity.Property(e => e.ReviewedAt)
                .HasPrecision(0)
                .HasColumnName("reviewed_at");
            entity.Property(e => e.ReviewedByUserId).HasColumnName("reviewed_by_user_id");
            entity.Property(e => e.SchoolId).HasColumnName("school_id");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasDefaultValue("pending")
                .HasColumnName("status");

            entity.HasOne(d => d.Province).WithMany(p => p.MentorRegistrations)
                .HasForeignKey(d => d.ProvinceId)
                .HasConstraintName("FK_mr_province");

            entity.HasOne(d => d.ReviewedByUser).WithMany(p => p.MentorRegistrations)
                .HasForeignKey(d => d.ReviewedByUserId)
                .HasConstraintName("FK_mr_reviewer");

            entity.HasOne(d => d.School).WithMany(p => p.MentorRegistrations)
                .HasForeignKey(d => d.SchoolId)
                .HasConstraintName("FK_mr_school");
        });

        modelBuilder.Entity<Notification>(entity =>
        {
            entity.HasKey(e => new { e.NotificationId, e.UserId }).HasName("PK__notifica__E059842F639B2736");

            entity.ToTable("notifications");

            entity.Property(e => e.NotificationId)
                .HasDefaultValueSql("(newid())")
                .HasColumnName("notification_id");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.Channel)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasColumnName("channel");
            entity.Property(e => e.Payload)
                .IsUnicode(false)
                .HasColumnName("payload");
            entity.Property(e => e.SentAt)
                .HasPrecision(0)
                .HasColumnName("sent_at");
            entity.Property(e => e.Type)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasColumnName("type");

            entity.HasOne(d => d.User).WithMany(p => p.Notifications)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_notifications_user");
        });

        modelBuilder.Entity<Problem>(entity =>
        {
            entity.HasKey(e => e.ProblemId).HasName("PK__problems__69B87CEC105D1832");

            entity.ToTable("problems");

            entity.HasIndex(e => e.RoundId, "UQ_problems_round").IsUnique();

            entity.Property(e => e.ProblemId)
                .HasDefaultValueSql("(newid())")
                .HasColumnName("problem_id");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasColumnName("created_at");
            entity.Property(e => e.DeletedAt).HasPrecision(0);
            entity.Property(e => e.Language)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasDefaultValue("python3")
                .HasColumnName("language");
            entity.Property(e => e.PenaltyRate).HasColumnName("penalty_rate");
            entity.Property(e => e.RoundId).HasColumnName("round_id");
            entity.Property(e => e.Type)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasColumnName("type");

            entity.HasOne(d => d.Round).WithOne(p => p.Problem)
                .HasForeignKey<Problem>(d => d.RoundId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_problems_round");
        });

        modelBuilder.Entity<Province>(entity =>
        {
            entity.HasKey(e => e.ProvinceId).HasName("PK__province__08DCB60F3AA84E0F");

            entity.ToTable("provinces");

            entity.Property(e => e.ProvinceId)
                .HasDefaultValueSql("(newid())")
                .HasColumnName("province_id");
            entity.Property(e => e.Address)
                .HasMaxLength(255)
                .IsUnicode(false)
                .HasColumnName("address");
            entity.Property(e => e.Name)
                .HasMaxLength(120)
                .IsUnicode(false)
                .HasColumnName("name");
        });

        modelBuilder.Entity<Round>(entity =>
        {
            entity.HasKey(e => e.RoundId).HasName("PK__rounds__295E52E380DFEDC3");

            entity.ToTable("rounds");

            entity.Property(e => e.RoundId)
                .HasDefaultValueSql("(newid())")
                .HasColumnName("round_id");
            entity.Property(e => e.ContestId).HasColumnName("contest_id");
            entity.Property(e => e.End)
                .HasPrecision(0)
                .HasColumnName("end");
            entity.Property(e => e.Name)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("name");
            entity.Property(e => e.Start)
                .HasPrecision(0)
                .HasColumnName("start");

            entity.HasOne(d => d.Contest).WithMany(p => p.Rounds)
                .HasForeignKey(d => d.ContestId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_rounds_contest");
        });

        modelBuilder.Entity<School>(entity =>
        {
            entity.HasKey(e => e.SchoolId).HasName("PK__schools__27CA6CF4B49EE151");

            entity.ToTable("schools");

            entity.Property(e => e.SchoolId)
                .HasDefaultValueSql("(newid())")
                .HasColumnName("school_id");
            entity.Property(e => e.Contact)
                .HasMaxLength(255)
                .IsUnicode(false)
                .HasColumnName("contact");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasColumnName("created_at");
            entity.Property(e => e.DeletedAt).HasPrecision(0);
            entity.Property(e => e.Name)
                .HasMaxLength(200)
                .IsUnicode(false)
                .HasColumnName("name");
            entity.Property(e => e.ProvinceId).HasColumnName("province_id");

            entity.HasOne(d => d.Province).WithMany(p => p.Schools)
                .HasForeignKey(d => d.ProvinceId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_schools_province");
        });

        modelBuilder.Entity<Student>(entity =>
        {
            entity.HasKey(e => e.StudentId).HasName("PK__students__2A33069A5789151B");

            entity.ToTable("students");

            entity.HasIndex(e => new { e.UserId, e.SchoolId }, "UQ_students_user_school").IsUnique();

            entity.Property(e => e.StudentId)
                .HasDefaultValueSql("(newid())")
                .HasColumnName("student_id");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasColumnName("created_at");
            entity.Property(e => e.DeletedAt).HasPrecision(0);
            entity.Property(e => e.Grade)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasColumnName("grade");
            entity.Property(e => e.SchoolId).HasColumnName("school_id");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.School).WithMany(p => p.Students)
                .HasForeignKey(d => d.SchoolId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_students_school");

            entity.HasOne(d => d.User).WithMany(p => p.Students)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_students_user");
        });

        modelBuilder.Entity<Submission>(entity =>
        {
            entity.HasKey(e => e.SubmissionId).HasName("PK__submissi__9B535595830BF8CC");

            entity.ToTable("submissions");

            entity.Property(e => e.SubmissionId)
                .HasDefaultValueSql("(newid())")
                .HasColumnName("submission_id");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasColumnName("created_at");
            entity.Property(e => e.DeletedAt).HasPrecision(0);
            entity.Property(e => e.JudgedBy)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasColumnName("judged_by");
            entity.Property(e => e.ProblemId).HasColumnName("problem_id");
            entity.Property(e => e.Score).HasColumnName("score");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasColumnName("status");
            entity.Property(e => e.SubmittedByStudentId).HasColumnName("submitted_by_student_id");
            entity.Property(e => e.TeamId).HasColumnName("team_id");

            entity.HasOne(d => d.Problem).WithMany(p => p.Submissions)
                .HasForeignKey(d => d.ProblemId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_submissions_problem");

            entity.HasOne(d => d.SubmittedByStudent).WithMany(p => p.Submissions)
                .HasForeignKey(d => d.SubmittedByStudentId)
                .HasConstraintName("FK_submissions_submitter");

            entity.HasOne(d => d.Team).WithMany(p => p.Submissions)
                .HasForeignKey(d => d.TeamId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_submissions_team");
        });

        modelBuilder.Entity<SubmissionArtifact>(entity =>
        {
            entity.HasKey(e => e.ArtifactId).HasName("PK__submissi__A074A76F877BA6F4");

            entity.ToTable("submission_artifacts");

            entity.Property(e => e.ArtifactId)
                .HasDefaultValueSql("(newid())")
                .HasColumnName("artifact_id");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasColumnName("created_at");
            entity.Property(e => e.DeletedAt).HasPrecision(0);
            entity.Property(e => e.SubmissionId).HasColumnName("submission_id");
            entity.Property(e => e.Type)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasColumnName("type");
            entity.Property(e => e.Url)
                .IsUnicode(false)
                .HasColumnName("url");

            entity.HasOne(d => d.Submission).WithMany(p => p.SubmissionArtifacts)
                .HasForeignKey(d => d.SubmissionId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_submission_artifacts_submission");
        });

        modelBuilder.Entity<SubmissionDetail>(entity =>
        {
            entity.HasKey(e => e.DetailsId).HasName("PK__submissi__C3E443F48E2132E2");

            entity.ToTable("submission_details");

            entity.Property(e => e.DetailsId)
                .HasDefaultValueSql("(newid())")
                .HasColumnName("details_id");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasColumnName("created_at");
            entity.Property(e => e.DeletedAt).HasPrecision(0);
            entity.Property(e => e.MemoryKb).HasColumnName("memory_kb");
            entity.Property(e => e.Note)
                .HasMaxLength(255)
                .IsUnicode(false)
                .HasColumnName("note");
            entity.Property(e => e.RuntimeMs).HasColumnName("runtime_ms");
            entity.Property(e => e.SubmissionId).HasColumnName("submission_id");
            entity.Property(e => e.TestcaseId).HasColumnName("testcase_id");
            entity.Property(e => e.Weight).HasColumnName("weight");

            entity.HasOne(d => d.Submission).WithMany(p => p.SubmissionDetails)
                .HasForeignKey(d => d.SubmissionId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_submission_details_submission");

            entity.HasOne(d => d.Testcase).WithMany(p => p.SubmissionDetails)
                .HasForeignKey(d => d.TestcaseId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_submission_details_testcase");
        });

        modelBuilder.Entity<Team>(entity =>
        {
            entity.HasKey(e => e.TeamId).HasName("PK__teams__F82DEDBC65635C26");

            entity.ToTable("teams");

            entity.Property(e => e.TeamId)
                .HasDefaultValueSql("(newid())")
                .HasColumnName("team_id");
            entity.Property(e => e.ContestId).HasColumnName("contest_id");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasColumnName("created_at");
            entity.Property(e => e.DeletedAt).HasPrecision(0);
            entity.Property(e => e.MentorId).HasColumnName("mentor_id");
            entity.Property(e => e.Name)
                .HasMaxLength(200)
                .IsUnicode(false)
                .HasColumnName("name");
            entity.Property(e => e.SchoolId).HasColumnName("school_id");

            entity.HasOne(d => d.Contest).WithMany(p => p.Teams)
                .HasForeignKey(d => d.ContestId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_teams_contest");

            entity.HasOne(d => d.Mentor).WithMany(p => p.Teams)
                .HasForeignKey(d => d.MentorId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_teams_mentor");

            entity.HasOne(d => d.School).WithMany(p => p.Teams)
                .HasForeignKey(d => d.SchoolId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_teams_school");
        });

        modelBuilder.Entity<TeamInvite>(entity =>
        {
            entity.HasKey(e => e.InviteId);

            entity.ToTable("team_invites");

            entity.HasIndex(e => new { e.InviteeEmail, e.Status, e.CreatedAt }, "IX_team_invites_email");

            entity.HasIndex(e => e.ExpiresAt, "IX_team_invites_expiry");

            entity.HasIndex(e => new { e.StudentId, e.Status, e.CreatedAt }, "IX_team_invites_student");

            entity.HasIndex(e => new { e.TeamId, e.Status, e.CreatedAt }, "IX_team_invites_team");

            entity.HasIndex(e => e.Token, "UQ_team_invites_token").IsUnique();

            entity.Property(e => e.InviteId)
                .HasDefaultValueSql("(newid())")
                .HasColumnName("invite_id");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasColumnName("created_at");
            entity.Property(e => e.ExpiresAt)
                .HasPrecision(0)
                .HasColumnName("expires_at");
            entity.Property(e => e.InvitedByUserId).HasColumnName("invited_by_user_id");
            entity.Property(e => e.InviteeEmail)
                .HasMaxLength(255)
                .IsUnicode(false)
                .HasColumnName("invitee_email");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasDefaultValue("pending")
                .HasColumnName("status");
            entity.Property(e => e.StudentId).HasColumnName("student_id");
            entity.Property(e => e.TeamId).HasColumnName("team_id");
            entity.Property(e => e.Token)
                .HasMaxLength(64)
                .IsUnicode(false)
                .HasColumnName("token");

            entity.HasOne(d => d.InvitedByUser).WithMany(p => p.TeamInvites)
                .HasForeignKey(d => d.InvitedByUserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_team_invites_inviter");

            entity.HasOne(d => d.Student).WithMany(p => p.TeamInvites)
                .HasForeignKey(d => d.StudentId)
                .HasConstraintName("FK_team_invites_student");

            entity.HasOne(d => d.Team).WithMany(p => p.TeamInvites)
                .HasForeignKey(d => d.TeamId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_team_invites_team");
        });

        modelBuilder.Entity<TeamMember>(entity =>
        {
            entity.HasKey(e => new { e.TeamId, e.StudentId });

            entity.ToTable("team_members");

            entity.Property(e => e.TeamId).HasColumnName("team_id");
            entity.Property(e => e.StudentId).HasColumnName("student_id");
            entity.Property(e => e.JoinedAt)
                .HasPrecision(0)
                .HasColumnName("joined_at");
            entity.Property(e => e.MemberRole)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasColumnName("member_role");

            entity.HasOne(d => d.Student).WithMany(p => p.TeamMembers)
                .HasForeignKey(d => d.StudentId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_team_members_student");

            entity.HasOne(d => d.Team).WithMany(p => p.TeamMembers)
                .HasForeignKey(d => d.TeamId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_team_members_team");
        });

        modelBuilder.Entity<TestCase>(entity =>
        {
            entity.HasKey(e => e.TestCaseId).HasName("PK__test_cas__F33C4A1786D85A58");

            entity.ToTable("test_cases");

            entity.Property(e => e.TestCaseId)
                .HasDefaultValueSql("(newid())")
                .HasColumnName("test_case_id");
            entity.Property(e => e.Description)
                .HasMaxLength(255)
                .IsUnicode(false)
                .HasColumnName("description");
            entity.Property(e => e.MemoryKb).HasColumnName("memory_kb");
            entity.Property(e => e.ProblemId).HasColumnName("problem_id");
            entity.Property(e => e.TimeLimitMs).HasColumnName("time_limit_ms");
            entity.Property(e => e.Type)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasColumnName("type");
            entity.Property(e => e.Weight)
                .HasDefaultValue(1.0)
                .HasColumnName("weight");

            entity.HasOne(d => d.Problem).WithMany(p => p.TestCases)
                .HasForeignKey(d => d.ProblemId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_test_cases_problem");
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.UserId).HasName("PK__users__B9BE370F63AA0581");

            entity.ToTable("users");

            entity.HasIndex(e => e.Email, "UQ__users__AB6E61646B3DD2A5").IsUnique();

            entity.HasIndex(e => e.Fullname, "UQ__users__D316D6C188FAE9C9").IsUnique();

            entity.Property(e => e.UserId)
                .HasDefaultValueSql("(newid())")
                .HasColumnName("user_id");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasColumnName("created_at");
            entity.Property(e => e.DeletedAt).HasPrecision(0);
            entity.Property(e => e.Email)
                .HasMaxLength(255)
                .IsUnicode(false)
                .HasColumnName("email");
            entity.Property(e => e.Fullname)
                .HasMaxLength(255)
                .IsUnicode(false)
                .HasColumnName("fullname");
            entity.Property(e => e.PasswordHash)
                .HasMaxLength(255)
                .IsUnicode(false)
                .HasColumnName("password_hash");
            entity.Property(e => e.Role)
                .HasMaxLength(255)
                .IsUnicode(false)
                .HasColumnName("role");
            entity.Property(e => e.Status)
                .HasMaxLength(30)
                .IsUnicode(false)
                .HasDefaultValue("active")
                .HasColumnName("status");
            entity.Property(e => e.UpdatedAt)
                .HasPrecision(0)
                .HasColumnName("updated_at");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
