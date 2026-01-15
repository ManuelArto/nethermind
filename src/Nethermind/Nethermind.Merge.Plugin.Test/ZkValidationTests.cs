using System.Threading.Tasks;
using FluentAssertions;
using Nethermind.Blockchain;
using Nethermind.Core;
using Nethermind.Core.Test.Builders;
using Nethermind.Logging;
using Nethermind.JsonRpc;
using Nethermind.Merge.Plugin.Data;
using Nethermind.Merge.Plugin.ZkValidation;
using NSubstitute;
using NUnit.Framework;

namespace Nethermind.Merge.Plugin.Test;

[TestFixture]
public class ZkValidationTests
{
    private ZkNewPayloadHandler _zkHandler;
    private IBlockTree _blockTree;

    [SetUp]
    public void SetUp()
    {
        _blockTree = Substitute.For<IBlockTree>();
        _zkHandler = new ZkNewPayloadHandler(_blockTree, new TestLogManager(LogLevel.Info));
    }

    [Test]
    public async Task ExecuteAsync_ShouldReturnValid_ForHardcodedBlock()
    {
        // Arrange
        const long blockNumber = 24234077;
        Block block = Build.A.Block
            .WithNumber(blockNumber)
            .TestObject;

        var payload = ExecutionPayload.Create(block);

        // Act
        ResultWrapper<PayloadStatusV1> result = await _zkHandler.HandleAsync(payload);

        // Assert
        (result.Data.Status).Should().Be(PayloadStatus.Valid);
        await _blockTree.Received(1).SuggestBlockAsync(
            Arg.Is<Block>(b => b.Hash == block.Hash && b.Number == block.Number),
            BlockTreeSuggestOptions.ForceDontSetAsMain
        );
    }
}
