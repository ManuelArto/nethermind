// SPDX-FileCopyrightText: 2025 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System.Threading.Tasks;
using FluentAssertions;
using Nethermind.Blockchain;
using Nethermind.Consensus;
using Nethermind.Core;
using Nethermind.Core.Crypto;
using Nethermind.Core.Test.Builders;
using Nethermind.Logging;
using Nethermind.JsonRpc;
using Nethermind.Merge.Plugin.Data;
using Nethermind.Merge.Plugin.InvalidChainTracker;
using Nethermind.ZkValidation.Plugin.EthProofValidator;
using Nethermind.ZkValidation.Plugin.Handlers;
using NSubstitute;
using NUnit.Framework;

namespace Nethermind.ZkValidation.Plugin.Test;

[TestFixture]
public class ZkNewPayloadHandlerTests
{
    private ZkNewPayloadHandler _handler = null!;
    private ZkValidationService _validationService = null!;
    private IInvalidChainTracker _invalidChainTracker = null!;
    private IBlockValidator _blockValidator = null!;
    private IBlockTree _blockTree = null!;
    private IPoSSwitcher _poSSwitcher = null!;

    [SetUp]
    public void SetUp()
    {
        _invalidChainTracker = Substitute.For<IInvalidChainTracker>();
        _blockValidator = Substitute.For<IBlockValidator>();
        _blockTree = Substitute.For<IBlockTree>();
        _poSSwitcher = Substitute.For<IPoSSwitcher>();
        _validationService = new ZkValidationService(_blockValidator, _blockTree, _invalidChainTracker, new TestLogManager(LogLevel.Info));
        _handler = new ZkNewPayloadHandler(_validationService, _poSSwitcher, new TestLogManager(LogLevel.Info));
    }

    [Test]
    public async Task HandleAsync_ShouldReturnInvalid_WhenBlockHashMismatch()
    {
        // Arrange
        Block block = Build.A.Block
            .WithNumber(1)
            .TestObject;
        ExecutionPayload payload = ExecutionPayload.Create(block);
        // Tamper with the hash
        payload.BlockHash = Keccak.Zero;

        // Act
        ResultWrapper<PayloadStatusV1> result = await _handler.HandleAsync(payload);

        // Assert
        result.Data.Status.Should().Be(PayloadStatus.Invalid);
        result.Data.ValidationError.Should().Contain("Invalid block hash");
    }

    [Test]
    public async Task HandleAsync_ShouldReturnInvalid_WhenBlockCannotBeParsed()
    {
        // Arrange
        ExecutionPayload payload = new ExecutionPayload
        {
            BlockHash = Keccak.Zero,
            ParentHash = Keccak.Zero,
            FeeRecipient = Address.Zero,
            StateRoot = Keccak.Zero,
            ReceiptsRoot = Keccak.Zero,
            LogsBloom = Bloom.Empty,
            PrevRandao = Keccak.Zero,
            BlockNumber = 1,
            GasLimit = 1000000,
            GasUsed = 0,
            Timestamp = 1000,
            ExtraData = [],
            BaseFeePerGas = 1,
            Transactions = []
        };

        // Act
        ResultWrapper<PayloadStatusV1> result = await _handler.HandleAsync(payload);

        // Assert
        result.Data.Status.Should().Be(PayloadStatus.Invalid);
    }

    [Test]
    public async Task HandleAsync_ShouldReturnInvalid_WhenBlockIsOnInvalidChain()
    {
        // Arrange
        Block block = Build.A.Block
            .WithNumber(100)
            .TestObject;
        ExecutionPayload payload = ExecutionPayload.Create(block);

        // Mock the invalid chain tracker to return true for any block hash check
        _invalidChainTracker.IsOnKnownInvalidChain(Arg.Any<Hash256>(), out Arg.Any<Hash256?>())
            .Returns(x =>
            {
                x[1] = Keccak.Zero;
                return true;
            });

        // Act
        ResultWrapper<PayloadStatusV1> result = await _handler.HandleAsync(payload);

        // Assert
        result.Data.Status.Should().Be(PayloadStatus.Invalid);
    }

    // Note: Tests for Valid/Syncing responses after hash validation are covered
    // in ZkValidationServiceTests.cs. Handler tests focus on hash validation
    // and invalid chain detection which happen before ZK validation.
}
