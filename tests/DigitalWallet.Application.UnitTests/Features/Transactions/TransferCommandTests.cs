namespace Application.UnitTests.Features.Transactions
{
    public class TransferCommandTests
    {
        private readonly Mock<IAccountRepository> _accountRepoMock = new();
        private readonly Mock<ITransactionRepository> _transactionRepoMock = new();
        private readonly Mock<IUserRepository> _userRepoMock = new();
        private readonly Mock<IKycSubmissionRepository> _kycRepoMock = new();
        private readonly Mock<ICurrentUserService> _currentUserMock = new();
        private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
        private readonly Mock<IMapper> _mapperMock = new();
        private readonly Mock<IMediator> _mediatorMock = new();
        private readonly Mock<ILogger<TransferCommandHandler>> _loggerMock = new();
        private readonly TransferDomainService _transferService = new();

        private readonly TransferCommandHandler _handler;

        private readonly User _user;
        private readonly Account _fromAccount;
        private readonly Account _toAccount;
        private readonly KycSubmission _kyc;

        public TransferCommandTests()
        {
            _user = new User("auth0|123", "[email protected]", "John", "Doe");
            _fromAccount = new Account(_user.Id, AccountType.Personal, Currency.USD, "From");
            _fromAccount.ApplyDebit(new Money(500, Currency.USD));
            _toAccount = new Account(_user.Id, AccountType.Personal, Currency.USD, "To");
            _kyc = new KycSubmission(_user.Id, "John", "Doe", DateTime.UtcNow.AddYears(-20),
                "Addr", "", "City", "", "", "US", "Passport", "123", "/file.pdf");
            _kyc.Approve("admin");

            _handler = new TransferCommandHandler(
                _accountRepoMock.Object,
                _transactionRepoMock.Object,
                _userRepoMock.Object,
                _kycRepoMock.Object,
                _currentUserMock.Object,
                _unitOfWorkMock.Object,
                _mapperMock.Object,
                _transferService,
                _mediatorMock.Object,
                _loggerMock.Object);
        }

        private void SetupBaseMocks()
        {
            _currentUserMock.Setup(x => x.IsAuthenticated).Returns(true);
            _currentUserMock.Setup(x => x.UserId).Returns("auth0|123");
            _userRepoMock.Setup(x => x.GetByAuth0IdAsync("auth0|123", It.IsAny<CancellationToken>()))
                .ReturnsAsync(_user);
            _kycRepoMock.Setup(x => x.GetByUserIdAsync(_user.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(_kyc);
            _accountRepoMock.Setup(x => x.GetByIdAsync(_fromAccount.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(_fromAccount);
            _accountRepoMock.Setup(x => x.GetByIdAsync(_toAccount.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(_toAccount);
        }

        [Fact]
        public async Task Handle_ValidTransfer_ShouldSucceed()
        {
            // Arrange
            SetupBaseMocks();

            var command = new TransferCommand
            {
                IdempotencyKey = "key",
                FromAccountId = _fromAccount.Id,
                ToAccountId = _toAccount.Id,
                Amount = 100,
                CurrencyCode = "USD",
                Description = "Test"
            };

            _unitOfWorkMock.Setup(x => x.BeginTransactionAsync(IsolationLevel.Serializable, It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            _unitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
            _unitOfWorkMock.Setup(x => x.CommitTransactionAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

            _mapperMock.Setup(m => m.Map<TransactionDto>(It.IsAny<Transaction>()))
                .Returns(new TransactionDto());

            // Act
            var result = await _handler.Handle(command, CancellationToken.None);

            // Assert
            result.Should().NotBeNull();
            _accountRepoMock.Verify(x => x.Update(_fromAccount), Times.Once);
            _accountRepoMock.Verify(x => x.Update(_toAccount), Times.Once);
            _transactionRepoMock.Verify(x => x.Add(It.IsAny<Transaction>()), Times.Once);
            _unitOfWorkMock.Verify(x => x.BeginTransactionAsync(IsolationLevel.Serializable, It.IsAny<CancellationToken>()), Times.Once);
            _unitOfWorkMock.Verify(x => x.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
            _mediatorMock.Verify(x => x.Publish(It.IsAny<MoneyTransferredNotification>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task Handle_KycNotApproved_ThrowsForbidden()
        {
            // Arrange
            _currentUserMock.Setup(x => x.IsAuthenticated).Returns(true);
            _currentUserMock.Setup(x => x.UserId).Returns("auth0|123");
            _userRepoMock.Setup(x => x.GetByAuth0IdAsync("auth0|123", It.IsAny<CancellationToken>()))
                .ReturnsAsync(_user);
            _kycRepoMock.Setup(x => x.GetByUserIdAsync(_user.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync((KycSubmission?)null); // not approved

            var command = new TransferCommand
            {
                IdempotencyKey = "key",
                FromAccountId = _fromAccount.Id,
                ToAccountId = _toAccount.Id,
                Amount = 100,
                CurrencyCode = "USD",
                Description = "Test"
            };

            // Act
            var act = () => _handler.Handle(command, CancellationToken.None);

            // Assert
            await act.Should().ThrowAsync<ForbiddenAccessException>()
                .WithMessage("KYC verification required to transfer.");
        }

        [Fact]
        public async Task Handle_FromAccountNotFound_ThrowsNotFound()
        {
            // Arrange
            SetupBaseMocks();
            _accountRepoMock.Setup(x => x.GetByIdAsync(_fromAccount.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync((Account?)null);

            var command = new TransferCommand
            {
                IdempotencyKey = "key",
                FromAccountId = _fromAccount.Id,
                ToAccountId = _toAccount.Id,
                Amount = 100,
                CurrencyCode = "USD",
                Description = "Test"
            };

            // Act
            var act = () => _handler.Handle(command, CancellationToken.None);

            // Assert
            await act.Should().ThrowAsync<NotFoundException>();
        }

        [Fact]
        public async Task Handle_UserDoesNotOwnFromAccount_ThrowsForbidden()
        {
            // Arrange
            SetupBaseMocks();
            var otherUserAccount = new Account(Guid.NewGuid(), AccountType.Personal, Currency.USD, "Other");
            _accountRepoMock.Setup(x => x.GetByIdAsync(otherUserAccount.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(otherUserAccount);

            var command = new TransferCommand
            {
                IdempotencyKey = "key",
                FromAccountId = otherUserAccount.Id,
                ToAccountId = _toAccount.Id,
                Amount = 100,
                CurrencyCode = "USD",
                Description = "Test"
            };

            // Act
            var act = () => _handler.Handle(command, CancellationToken.None);

            // Assert
            await act.Should().ThrowAsync<ForbiddenAccessException>();
        }

        [Fact]
        public async Task Handle_ExceptionDuringTransfer_RollsBack()
        {
            // Arrange
            SetupBaseMocks();
            var command = new TransferCommand
            {
                IdempotencyKey = "key",
                FromAccountId = _fromAccount.Id,
                ToAccountId = _toAccount.Id,
                Amount = 10000, // exceeds balance
                CurrencyCode = "USD",
                Description = "Test"
            };

            _unitOfWorkMock.Setup(x => x.BeginTransactionAsync(IsolationLevel.Serializable, It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            _unitOfWorkMock.Setup(x => x.RollbackTransactionAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            // Act
            var act = () => _handler.Handle(command, CancellationToken.None);

            // Assert
            await act.Should().ThrowAsync<DomainException>();
            _unitOfWorkMock.Verify(x => x.RollbackTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}