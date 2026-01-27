// SPDX-FileCopyrightText: 2025 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System.Threading.Tasks;
using FluentAssertions;
using Nethermind.Blockchain;
using Nethermind.Core;
using Nethermind.Core.Crypto;
using Nethermind.Core.Test.Builders;
using Nethermind.Logging;
using Nethermind.Merge.Plugin.Data;
using Nethermind.Merge.Plugin.InvalidChainTracker;
using Nethermind.ZkValidation.Plugin.EthProofValidator;
using Nethermind.ZkValidation.Plugin.Handlers;
using NSubstitute;
using NUnit.Framework;

namespace Nethermind.ZkValidation.Plugin.Test;

[TestFixture]
public class ZkValidationServiceTests
{
    private ZkValidationService _service = null!;
    private IInvalidChainTracker _invalidChainTracker = null!;
    private IBlockValidator _blockValidator = null!;
    private IBlockTree _blockTree = null!;

    [SetUp]
    public void SetUp()
    {
        _invalidChainTracker = Substitute.For<IInvalidChainTracker>();
        _blockValidator = Substitute.For<IBlockValidator>();
        _blockTree = Substitute.For<IBlockTree>();
        _service = new ZkValidationService(_blockValidator, _blockTree, _invalidChainTracker, new TestLogManager(LogLevel.Info));
    }

    [Test]
    public async Task ValidateAsync_ShouldReturnValid_WhenValidatorReturnsValid()
    {
        // Arrange
        Block block = Build.A.Block.WithNumber(100).TestObject;
        _blockTree.FindBlock(block.Hash!, Arg.Any<BlockTreeLookupOptions>()).Returns((Block?)null);
        _blockValidator.ValidateBlockAsync(block.Number - 1).Returns(PayloadStatus.Valid);

        // Act
        string result = await _service.ValidateAsync(block);

        // Assert
        result.Should().Be(PayloadStatus.Valid);
        _blockTree.Received(1).Insert(block, Arg.Any<BlockTreeInsertBlockOptions>(), Arg.Any<BlockTreeInsertHeaderOptions>());
    }

    [Test]
    public async Task ValidateAsync_ShouldReturnValid_WhenBlockAlreadyInBlockTree()
    {
        // Arrange
        Block block = Build.A.Block.WithNumber(100).TestObject;
        _blockTree.FindBlock(block.Hash!, Arg.Any<BlockTreeLookupOptions>()).Returns(block);

        // Act
        string result = await _service.ValidateAsync(block);

        // Assert
        result.Should().Be(PayloadStatus.Valid);
        // Should NOT call validator when block already exists
        await _blockValidator.DidNotReceive().ValidateBlockAsync(Arg.Any<long>());
        // Should NOT insert again
        _blockTree.DidNotReceive().Insert(Arg.Any<Block>(), Arg.Any<BlockTreeInsertBlockOptions>(), Arg.Any<BlockTreeInsertHeaderOptions>());
    }

    [Test]
    public async Task ValidateAsync_ShouldReturnInvalid_WhenValidatorReturnsInvalid()
    {
        // Arrange
        Block block = Build.A.Block.WithNumber(100).TestObject;
        _blockTree.FindBlock(block.Hash!, Arg.Any<BlockTreeLookupOptions>()).Returns((Block?)null);
        _blockValidator.ValidateBlockAsync(block.Number - 1).Returns(PayloadStatus.Invalid);

        // Act
        string result = await _service.ValidateAsync(block);

        // Assert
        result.Should().Be(PayloadStatus.Invalid);
        // Verify invalid chain tracker was notified
        _invalidChainTracker.Received(1).OnInvalidBlock(block.Hash!, block.ParentHash);
        // Should NOT insert invalid block
        _blockTree.DidNotReceive().Insert(Arg.Any<Block>(), Arg.Any<BlockTreeInsertBlockOptions>(), Arg.Any<BlockTreeInsertHeaderOptions>());
    }

    [Test]
    public async Task ValidateAsync_ShouldReturnSyncing_AfterMaxRetries()
    {
        // Arrange
        Block block = Build.A.Block.WithNumber(100).TestObject;
        _blockTree.FindBlock(block.Hash!, Arg.Any<BlockTreeLookupOptions>()).Returns((Block?)null);
        // Always return Syncing - simulates proofs not ready
        _blockValidator.ValidateBlockAsync(block.Number - 1).Returns(PayloadStatus.Syncing);

        // Act
        string result = await _service.ValidateAsync(block);

        // Assert
        result.Should().Be(PayloadStatus.Syncing);
        // Should have tried multiple times (MaxRetries + 1 = 9 attempts)
        await _blockValidator.Received(9).ValidateBlockAsync(block.Number - 1);
    }

    [Test]
    public async Task ValidateAsync_ShouldRetryUntilValid()
    {
        // Arrange
        Block block = Build.A.Block.WithNumber(100).TestObject;
        _blockTree.FindBlock(block.Hash!, Arg.Any<BlockTreeLookupOptions>()).Returns((Block?)null);
        
        // First 2 calls return Syncing, third returns Valid
        int callCount = 0;
        _blockValidator.ValidateBlockAsync(block.Number - 1).Returns(_ =>
        {
            callCount++;
            return callCount < 3 ? PayloadStatus.Syncing : PayloadStatus.Valid;
        });

        // Act
        string result = await _service.ValidateAsync(block);

        // Assert
        result.Should().Be(PayloadStatus.Valid);
        callCount.Should().Be(3);
        _blockTree.Received(1).Insert(block, Arg.Any<BlockTreeInsertBlockOptions>(), Arg.Any<BlockTreeInsertHeaderOptions>());
    }

    [Test]
    public void IsOnInvalidChain_ShouldReturnTrue_WhenBlockIsOnInvalidChain()
    {
        // Arrange
        Hash256 blockHash = Keccak.Compute("block");
        Hash256 lastValidHash = Keccak.Compute("lastValid");

        _invalidChainTracker.IsOnKnownInvalidChain(blockHash, out Arg.Any<Hash256?>())
            .Returns(x =>
            {
                x[1] = lastValidHash;
                return true;
            });

        // Act
        bool result = _service.IsOnInvalidChain(blockHash, out Hash256? returnedLastValidHash);

        // Assert
        result.Should().BeTrue();
        returnedLastValidHash.Should().Be(lastValidHash);
    }

    [Test]
    public void IsOnInvalidChain_ShouldReturnFalse_WhenBlockIsValid()
    {
        // Arrange
        Hash256 blockHash = Keccak.Compute("block");

        _invalidChainTracker.IsOnKnownInvalidChain(Arg.Any<Hash256>(), out Arg.Any<Hash256?>())
            .Returns(false);

        // Act
        bool result = _service.IsOnInvalidChain(blockHash, out Hash256? lastValidHash);

        // Assert
        result.Should().BeFalse();
        lastValidHash.Should().BeNull();
    }

    [Test]
    public void IsOnInvalidChain_ShouldSetChildParent_WhenParentHashProvided()
    {
        // Arrange
        Hash256 blockHash = Keccak.Compute("block");
        Hash256 parentHash = Keccak.Compute("parent");

        // Act
        _service.IsOnInvalidChain(blockHash, out _, parentHash);

        // Assert
        _invalidChainTracker.Received(1).SetChildParent(blockHash, parentHash);
    }

    [Test]
    public void IsOnInvalidChain_ShouldNotSetChildParent_WhenParentHashIsNull()
    {
        // Arrange
        Hash256 blockHash = Keccak.Compute("block");

        // Act
        _service.IsOnInvalidChain(blockHash, out _);

        // Assert
        _invalidChainTracker.DidNotReceive().SetChildParent(Arg.Any<Hash256>(), Arg.Any<Hash256>());
    }
}
