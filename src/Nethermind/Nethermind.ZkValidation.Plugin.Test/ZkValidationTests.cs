// SPDX-FileCopyrightText: 2025 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System.Threading.Tasks;
using FluentAssertions;
using Nethermind.Blockchain;
using Nethermind.Core;
using Nethermind.Core.Test.Builders;
using Nethermind.Logging;
using Nethermind.Merge.Plugin.Data;
using Nethermind.Merge.Plugin.InvalidChainTracker;
using Nethermind.ZkValidation.Plugin.EthProofValidator;
using Nethermind.ZkValidation.Plugin.Handlers;
using NSubstitute;
using NUnit.Framework;

namespace Nethermind.ZkValidation.Plugin.Test;

/// <summary>
/// Integration-style tests that verify the ZK validation service behavior.
/// Tests the validation logic directly without going through the handler
/// (which adds block hash validation complexity).
/// </summary>
[TestFixture]
public class ZkValidationTests
{
    private ZkValidationService _validationService = null!;
    private IBlockValidator _blockValidator = null!;
    private IBlockTree _blockTree = null!;
    private IInvalidChainTracker _invalidChainTracker = null!;

    [SetUp]
    public void SetUp()
    {
        _blockValidator = Substitute.For<IBlockValidator>();
        _blockTree = Substitute.For<IBlockTree>();
        _invalidChainTracker = Substitute.For<IInvalidChainTracker>();
        
        _validationService = new ZkValidationService(
            _blockValidator,
            _blockTree,
            _invalidChainTracker,
            new TestLogManager(LogLevel.Info));
    }

    [Test]
    public async Task ValidateAsync_ShouldReturnSyncing_WhenNoProofsAvailable()
    {
        // Arrange
        const long blockNumber = 24234077;
        Block block = Build.A.Block
            .WithNumber(blockNumber)
            .TestObject;
        
        // Simulate no proofs available (validator returns Syncing)
        _blockTree.FindBlock(block.Hash!, Arg.Any<BlockTreeLookupOptions>()).Returns((Block?)null);
        _blockValidator.ValidateBlockAsync(block.Number - 1).Returns(PayloadStatus.Syncing);

        // Act
        string result = await _validationService.ValidateAsync(block);

        // Assert
        result.Should().Be(PayloadStatus.Syncing);
    }

    [Test]
    public async Task ValidateAsync_ShouldReturnValid_WhenProofsAreValid()
    {
        // Arrange
        Block block = Build.A.Block
            .WithNumber(100)
            .TestObject;
        
        _blockTree.FindBlock(block.Hash!, Arg.Any<BlockTreeLookupOptions>()).Returns((Block?)null);
        _blockValidator.ValidateBlockAsync(block.Number - 1).Returns(PayloadStatus.Valid);

        // Act
        string result = await _validationService.ValidateAsync(block);

        // Assert
        result.Should().Be(PayloadStatus.Valid);
        _blockTree.Received(1).Insert(block, Arg.Any<BlockTreeInsertBlockOptions>(), Arg.Any<BlockTreeInsertHeaderOptions>());
    }

    [Test]
    public async Task ValidateAsync_ShouldReturnInvalid_WhenProofsAreInvalid()
    {
        // Arrange
        Block block = Build.A.Block
            .WithNumber(100)
            .TestObject;
        
        _blockTree.FindBlock(block.Hash!, Arg.Any<BlockTreeLookupOptions>()).Returns((Block?)null);
        _blockValidator.ValidateBlockAsync(block.Number - 1).Returns(PayloadStatus.Invalid);

        // Act
        string result = await _validationService.ValidateAsync(block);

        // Assert
        result.Should().Be(PayloadStatus.Invalid);
        _invalidChainTracker.Received(1).OnInvalidBlock(block.Hash!, block.ParentHash);
    }
}
