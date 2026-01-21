// SPDX-FileCopyrightText: 2024 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System.Threading.Tasks;
using FluentAssertions;
using Nethermind.Core;
using Nethermind.Core.Test.Builders;
using Nethermind.Logging;
using Nethermind.JsonRpc;
using Nethermind.Merge.Plugin.Data;
using Nethermind.Merge.Plugin.Handlers;
using Nethermind.Merge.Plugin.InvalidChainTracker;
using Nethermind.ZkValidation.Plugin.Handlers;
using NSubstitute;
using NUnit.Framework;

namespace Nethermind.ZkValidation.Plugin.Test;

[TestFixture]
public class ZkValidationTests
{
    private ZkNewPayloadHandler _zkHandler = null!;
    private ZkValidationService _validationService = null!;
    private IBlockCacheService _blockCacheService = null!;
    private IInvalidChainTracker _invalidChainTracker = null!;

    [SetUp]
    public void SetUp()
    {
        _blockCacheService = Substitute.For<IBlockCacheService>();
        _invalidChainTracker = Substitute.For<IInvalidChainTracker>();
        _validationService = new ZkValidationService(
            _blockCacheService,
            _invalidChainTracker,
            new TestLogManager(LogLevel.Info));
        _zkHandler = new ZkNewPayloadHandler(_validationService, new TestLogManager(LogLevel.Info));
    }

    [Test]
    public async Task HandleAsync_ShouldReturnSyncing_WhenNoProofsAvailable()
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
        result.Data.Status.Should().Be(PayloadStatus.Syncing);
    }
}
