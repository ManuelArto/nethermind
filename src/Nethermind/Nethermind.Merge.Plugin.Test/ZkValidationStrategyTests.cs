using System.Threading.Tasks;
using FluentAssertions;
using Nethermind.Blockchain;
using Nethermind.Core;
using Nethermind.Core.Crypto;
using Nethermind.Core.Test.Builders;
using Nethermind.Logging;
using Nethermind.Consensus.Processing;
using Nethermind.Merge.Plugin.Handlers;
using Nethermind.Merge.Plugin.ZkValidation;
using NSubstitute;
using NUnit.Framework;

namespace Nethermind.Merge.Plugin.Test;

[TestFixture]
public class ZkValidationStrategyTests
{
    private ZkValidationStrategy _strategy;
    private IBlockTree _blockTree;

    [SetUp]
    public void SetUp()
    {
        _blockTree = Substitute.For<IBlockTree>();
        _strategy = new ZkValidationStrategy(_blockTree, new TestLogManager(LogLevel.Info));
    }

    [Test]
    public async Task ExecuteAsync_ShouldReturnValid_ForHardcodedBlock()
    {
        // Arrange
        const long blockNumber = 24234077;
        const string blockHashStr = "0x52068ecfad6fa31a68a5fc75b35fcf40aabce54424f5ce2726c220f5ec762180";
        var blockHash = new Hash256(blockHashStr);

        Block block = Build.A.Block
            .WithNumber(blockNumber)
            .TestObject;
        block.Header.Hash = blockHash;

        BlockHeader parent = Build.A.BlockHeader.TestObject;

        // Act
        (NewPayloadHandler.ValidationResult result, string? message) = await _strategy.ExecuteAsync(block, parent, ProcessingOptions.None);

        // Assert
        result.Should().Be(NewPayloadHandler.ValidationResult.Valid);
        await _blockTree.Received(1).SuggestBlockAsync(block, Arg.Any<BlockTreeSuggestOptions>());
    }
}
