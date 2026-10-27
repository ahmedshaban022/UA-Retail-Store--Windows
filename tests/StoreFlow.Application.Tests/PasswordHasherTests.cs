using StoreFlow.Application.Security;
using Shouldly;

namespace StoreFlow.Application.Tests;

public sealed class PasswordHasherTests
{
    private readonly PasswordHasher _hasher = new();

    [Fact]
    public void A_hashed_password_verifies()
    {
        string hash = _hasher.Hash("correct horse battery staple");

        PasswordVerificationResult result = _hasher.Verify("correct horse battery staple", hash);

        result.IsVerified.ShouldBeTrue();
    }

    [Fact]
    public void A_wrong_password_does_not_verify()
    {
        string hash = _hasher.Hash("correct horse battery staple");

        _hasher.Verify("wrong password", hash).IsVerified.ShouldBeFalse();
    }

    [Fact]
    public void The_hash_never_contains_the_password()
    {
        string hash = _hasher.Hash("correct horse battery staple");

        hash.ShouldNotContain("correct horse battery staple");
        hash.ShouldStartWith("argon2id$");
    }

    [Fact]
    public void Two_hashes_of_the_same_password_differ()
    {
        _hasher.Hash("same password").ShouldNotBe(_hasher.Hash("same password"));
    }

    [Fact]
    public void A_legacy_pbkdf2_hash_verifies_and_is_marked_for_upgrade()
    {
        const string password = "legacy password";
        byte[] salt = System.Security.Cryptography.RandomNumberGenerator.GetBytes(16);
        byte[] derived = System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2(
            System.Text.Encoding.UTF8.GetBytes(password),
            salt,
            600_000,
            System.Security.Cryptography.HashAlgorithmName.SHA256,
            32);

        string stored = string.Join(
            '$',
            "pbkdf2",
            "sha256",
            "600000",
            Convert.ToBase64String(salt),
            Convert.ToBase64String(derived));

        PasswordVerificationResult result = _hasher.Verify(password, stored);

        result.IsVerified.ShouldBeTrue();
        result.NeedsRehash.ShouldBeTrue();
    }

    [Fact]
    public void A_fresh_hash_does_not_need_rehashing()
    {
        PasswordVerificationResult result = _hasher.Verify("a password", _hasher.Hash("a password"));

        result.IsVerified.ShouldBeTrue();
        result.NeedsRehash.ShouldBeFalse();
    }

    [Fact]
    public void An_unreadable_hash_never_authenticates()
    {
        _hasher.Verify("a password", "not-a-hash").IsVerified.ShouldBeFalse();
    }
}
