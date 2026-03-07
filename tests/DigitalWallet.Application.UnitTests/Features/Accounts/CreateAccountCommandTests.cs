namespace Application.UnitTests.Features.Accounts;

public class CreateAccountCommandTests
{
    private readonly Mock<IAccountRepository> _accountRepoMock = new();
    private readonly Mock<IUserRepository> _userRepoMock = new();
    private readonly Mock<ICurrentUserService> _currentUserMock = new();
    private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
    private readonly Mock<IMapper> _mapperMock = new();
    private readonly Mock<IKycSubmissionRepository> _kycRepoMock = new();

    private readonly CreateAccountCommandHandler _handler;

    public CreateAccountCommandTests()
    {
        _handler = new CreateAccountCommandHandler(
            _accountRepoMock.Object,
            _userRepoMock.Object,
            _currentUserMock.Object,
            _unitOfWorkMock.Object,
            _mapperMock.Object,
            _kycRepoMock.Object,
            Mock.Of<ILogger<CreateAccountCommandHandler>>());
    }

    [Fact]
    public async Task Handle_UserNotAuthenticated_ThrowsUnauthorized()
    {
        // Arrange
        _currentUserMock.Setup(x => x.IsAuthenticated).Returns(false);
        var command = new CreateAccountCommand();

        // Act
        var act = () => _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<UnauthorizedException>();
    }

    [Fact]
    public async Task Handle_UserNotFound_ThrowsNotFound()
    {
        // Arrange
        _currentUserMock.Setup(x => x.IsAuthenticated).Returns(true);
        _currentUserMock.Setup(x => x.UserId).Returns("auth0|123");
        _userRepoMock.Setup(x => x.GetByAuth0IdAsync("auth0|123", It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        var command = new CreateAccountCommand();

        // Act
        var act = () => _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_KycNotApproved_ThrowsForbidden()
    {
        // Arrange
        var user = new User("auth0|123", "[email protected]", "John", "Doe");
        _currentUserMock.Setup(x => x.IsAuthenticated).Returns(true);
        _currentUserMock.Setup(x => x.UserId).Returns("auth0|123");
        _userRepoMock.Setup(x => x.GetByAuth0IdAsync("auth0|123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _kycRepoMock.Setup(x => x.GetByUserIdAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((KycSubmission?)null);

        var command = new CreateAccountCommand
        {
            IdempotencyKey = "key",
            CurrencyCode = "USD",
            Name = "Wallet",
            AccountType = "Personal"
        };

        // Act
        var act = () => _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ForbiddenAccessException>()
            .WithMessage("KYC verification required to create an account");
    }

    [Fact]
    public async Task Handle_ValidRequest_CreatesAccount()
    {
        // Arrange
        var user = new User("auth0|123", "[email protected]", "John", "Doe");
        var kyc = new KycSubmission(user.Id, "John", "Doe", DateTime.UtcNow.AddYears(-20),
            "Addr", "", "City", "", "", "US", "Passport", "123", "/file.pdf");
        kyc.Approve("admin");

        _currentUserMock.Setup(x => x.IsAuthenticated).Returns(true);
        _currentUserMock.Setup(x => x.UserId).Returns("auth0|123");
        _userRepoMock.Setup(x => x.GetByAuth0IdAsync("auth0|123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _kycRepoMock.Setup(x => x.GetByUserIdAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(kyc);

        _accountRepoMock.Setup(x => x.GetByUserAndCurrencyAsync(user.Id, "USD", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Account?)null);

        var accountDto = new AccountDto { Id = Guid.NewGuid(), Name = "Wallet" };
        _mapperMock.Setup(m => m.Map<AccountDto>(It.IsAny<Account>())).Returns(accountDto);

        var command = new CreateAccountCommand
        {
            IdempotencyKey = "key",
            CurrencyCode = "USD",
            Name = "Wallet",
            AccountType = "Personal"
        };

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Name.Should().Be("Wallet");
        _accountRepoMock.Verify(x => x.Add(It.IsAny<Account>()), Times.Once);
        _unitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}