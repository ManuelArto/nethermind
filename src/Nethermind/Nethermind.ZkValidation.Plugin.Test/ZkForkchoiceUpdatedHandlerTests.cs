// SPDX-FileCopyrightText: 2025 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System.Threading.Tasks;
using FluentAssertions;
using Nethermind.Blockchain;
using Nethermind.Consensus.Producers;
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
public class ZkForkchoiceUpdatedHandlerTests
{
    private ZkForkchoiceUpdatedHandler _handler = null!;
    private ZkValidationService _validationService = null!;
    private IInvalidChainTracker _invalidChainTracker = null!;
    private IBlockValidator _blockValidator = null!;
    private IBlockTree _blockTree = null!;

    [SetUp]
    public void SetUp()
    {
        _blockTree = Substitute.For<IBlockTree>();
        _invalidChainTracker = Substitute.For<IInvalidChainTracker>();
        _blockValidator = Substitute.For<IBlockValidator>();
        _validationService = new ZkValidationService(_blockValidator, _blockTree, _invalidChainTracker, new TestLogManager(LogLevel.Info));
        _handler = new ZkForkchoiceUpdatedHandler(_validationService, _blockTree, new TestLogManager(LogLevel.Info));
    }

    [Test]
    public async Task Handle_ShouldReturnSyncing_WhenBlockNotFound()
    {
        // Arrange
        Hash256 headBlockHash = Keccak.Compute("head");
        ForkchoiceStateV1 forkchoiceState = new(headBlockHash, Keccak.Zero, Keccak.Zero);
        _blockTree.FindBlock(headBlockHash, Arg.Any<BlockTreeLookupOptions>()).Returns((Block?)null);

        // Act
        ResultWrapper<ForkchoiceUpdatedV1Result> result = await _handler.Handle(forkchoiceState, null, 1);

        // Assert
        result.Data.PayloadStatus.Status.Should().Be(PayloadStatus.Syncing);
    }

    [Test]
    public async Task Handle_ShouldReturnInvalid_WhenBlockIsOnInvalidChain()
    {
        // Arrange
        Hash256 headBlockHash = Keccak.Compute("head");
        Hash256 lastValidHash = Keccak.Compute("lastValid");
        ForkchoiceStateV1 forkchoiceState = new(headBlockHash, Keccak.Zero, Keccak.Zero);

        _invalidChainTracker.IsOnKnownInvalidChain(headBlockHash, out Arg.Any<Hash256?>())
            .Returns(x =>
            {
                x[1] = lastValidHash;
                return true;
            });

        // Act
        ResultWrapper<ForkchoiceUpdatedV1Result> result = await _handler.Handle(forkchoiceState, null, 1);

        // Assert
        result.Data.PayloadStatus.Status.Should().Be(PayloadStatus.Invalid);
        result.Data.PayloadStatus.LatestValidHash.Should().Be(lastValidHash);
    }

    [Test]
    public async Task Handle_ShouldReturnError_WhenPayloadAttributesProvided()
    {
        // Arrange - payload attributes should cause error in ZK mode (no block production)
        Hash256 headBlockHash = Keccak.Compute("head");
        ForkchoiceStateV1 forkchoiceState = new(headBlockHash, Keccak.Zero, Keccak.Zero);
        PayloadAttributes payloadAttributes = new()
        {
            Timestamp = 12345,
            PrevRandao = Keccak.Zero,
            SuggestedFeeRecipient = Address.Zero
        };

        // Act
        ResultWrapper<ForkchoiceUpdatedV1Result> result = await _handler.Handle(forkchoiceState, payloadAttributes, 1);

        // Assert - should return error because block production is disabled
        result.Result.ResultType.Should().Be(ResultType.Failure);
        result.Result.Error.Should().Contain("Block production is disabled");
    }

    [Test]
    public async Task Handle_ShouldReturnValid_WhenBlockIsInBlockTree()
    {
        // Arrange
        Block block = Build.A.Block.WithNumber(100).TestObject;
        ForkchoiceStateV1 forkchoiceState = new(block.Hash!, Keccak.Zero, Keccak.Zero);

        _blockTree.FindBlock(block.Hash!, Arg.Any<BlockTreeLookupOptions>()).Returns(block);

        // Act
        ResultWrapper<ForkchoiceUpdatedV1Result> result = await _handler.Handle(forkchoiceState, null, 1);

        // Assert
        result.Data.PayloadStatus.Status.Should().Be(PayloadStatus.Valid);
        result.Data.PayloadStatus.LatestValidHash.Should().Be(block.Hash!);
    }

    [Test]
    public async Task Handle_ShouldUpdateMainChain_WhenHeadChanges()
    {
        // Arrange
        Block block = Build.A.Block.WithNumber(100).TestObject;
        Block currentHead = Build.A.Block.WithNumber(99).TestObject;
        ForkchoiceStateV1 forkchoiceState = new(block.Hash!, Keccak.Zero, Keccak.Zero);

        _blockTree.FindBlock(block.Hash!, Arg.Any<BlockTreeLookupOptions>()).Returns(block);
        _blockTree.Head.Returns(currentHead);

        // Act
        ResultWrapper<ForkchoiceUpdatedV1Result> result = await _handler.Handle(forkchoiceState, null, 1);

        // Assert
        result.Data.PayloadStatus.Status.Should().Be(PayloadStatus.Valid);
        _blockTree.Received(1).UpdateMainChain(Arg.Is<Block[]>(arr => arr.Length == 1 && arr[0] == block), true, true);
        _blockTree.Received(1).ForkChoiceUpdated(forkchoiceState.FinalizedBlockHash, forkchoiceState.SafeBlockHash);
    }

    [Test]
    public async Task Handle_ShouldNotUpdateMainChain_WhenHeadUnchanged()
    {
        // Arrange
        Block block = Build.A.Block.WithNumber(100).TestObject;
        ForkchoiceStateV1 forkchoiceState = new(block.Hash!, Keccak.Zero, Keccak.Zero);

        _blockTree.FindBlock(block.Hash!, Arg.Any<BlockTreeLookupOptions>()).Returns(block);
        _blockTree.Head.Returns(block); // Head is already this block

        // Act
        ResultWrapper<ForkchoiceUpdatedV1Result> result = await _handler.Handle(forkchoiceState, null, 1);

        // Assert
        result.Data.PayloadStatus.Status.Should().Be(PayloadStatus.Valid);
        _blockTree.DidNotReceive().UpdateMainChain(Arg.Any<Block[]>(), Arg.Any<bool>(), Arg.Any<bool>());
    }
}
