using FluentAssertions;
using Utility.Helpers;
using Xunit;

namespace Utility.UnitTests.Helpers
{
    public class PasswordHasherTests
    {
        [Fact]
        public void Hash_WhenPlainTextProvided_ShouldReturnNonEmptyHash_AndNotEqualPlainText()
        {
            // Arrange
            var plainText = "MyStrongPassword@123";

            // Act
            var hashed = PasswordHasher.Hash(plainText);

            // Assert
            hashed.Should().NotBeNullOrWhiteSpace();
            hashed.Should().NotBe(plainText);
        }

        [Fact]
        public void Verify_WhenPlainTextMatchesHash_ShouldReturnTrue()
        {
            // Arrange
            var plainText = "MyStrongPassword@123";
            var hashed = PasswordHasher.Hash(plainText);

            // Act
            var ok = PasswordHasher.Verify(plainText, hashed);

            // Assert
            ok.Should().BeTrue();
        }

        [Fact]
        public void Verify_WhenPlainTextDoesNotMatchHash_ShouldReturnFalse()
        {
            // Arrange
            var plainText = "MyStrongPassword@123";
            var hashed = PasswordHasher.Hash(plainText);

            // Act
            var ok = PasswordHasher.Verify("WrongPassword", hashed);

            // Assert
            ok.Should().BeFalse();
        }

        [Fact]
        public void Hash_WhenCalledTwiceWithSamePlainText_ShouldReturnDifferentHashes()
        {
            // Arrange
            var plainText = "SamePassword";

            // Act
            var hash1 = PasswordHasher.Hash(plainText);
            var hash2 = PasswordHasher.Hash(plainText);

            // Assert
            hash1.Should().NotBe(hash2); // do salt random
            PasswordHasher.Verify(plainText, hash1).Should().BeTrue();
            PasswordHasher.Verify(plainText, hash2).Should().BeTrue();
        }
    }
}
